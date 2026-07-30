using System.Runtime.InteropServices.WindowsRuntime;
using ExtendExprorer.Interop;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ExtendExprorer.Services;

/// <summary>シェルの関連付けアイコンを ImageSource として取得・キャッシュする（shell-icons）。
/// キャッシュは UI スレッドからのみ触る前提（EntryViewModel.Icon の getter 経由）なのでロック不要。
/// 取得・ピクセル変換は Task.Run、WriteableBitmap 生成のみ UI スレッドで行う。
///
/// **失敗はキャッシュしない**（BUG-010）。フォルダの汎用アイコンはキー <c>&lt;dir&gt;</c> を全フォルダで
/// 共有しているため、失敗した Task を残すと一度の失敗で以後すべてのフォルダがグリフ表示に落ち、
/// 再起動するまで戻らなくなる。</summary>
internal static class ShellIconCache
{
    // 取得できたアイコンだけを保持する。取得中のものは別に持ち、完了時に必ず取り除く
    private static readonly Dictionary<string, ImageSource> Resolved = new();
    private static readonly Dictionary<string, Task<ImageSource?>> InFlight = new();

    /// <summary>失敗時は null（呼び出し側は従来グリフのまま）。同一キーの同時要求は Task を共有する。
    /// 失敗した場合は記録しないので、次の要求で再試行される。
    /// <paramref name="distinctByPath"/>=true はドライブ・ホームなど固有アイコンを持つ項目用で、
    /// 汎用フォルダアイコンではなく実際のパスからアイコンを引く（キャッシュもパス単位）。</summary>
    public static Task<ImageSource?> GetAsync(string fullPath, bool isDirectory, bool distinctByPath = false)
    {
        var key = distinctByPath ? fullPath.ToLowerInvariant() : CacheKey(fullPath, isDirectory);
        if (Resolved.TryGetValue(key, out var cached))
        {
            return Task.FromResult<ImageSource?>(cached);
        }
        if (InFlight.TryGetValue(key, out var pending))
        {
            return pending;
        }
        var task = LoadAndCacheAsync(key, fullPath, isDirectory, distinctByPath);
        if (!task.IsCompleted)
        {
            InFlight[key] = task;
        }
        return task;
    }

    private static async Task<ImageSource?> LoadAndCacheAsync(string key, string fullPath, bool isDirectory, bool distinctByPath)
    {
        try
        {
            var icon = await LoadAsync(fullPath, isDirectory, distinctByPath);
            if (icon is not null)
            {
                Resolved[key] = icon;
            }
            return icon;
        }
        finally
        {
            // 成否にかかわらず取得中から外す。失敗はここで忘れられ、次の要求で再試行される
            InFlight.Remove(key);
        }
    }

    private static string CacheKey(string fullPath, bool isDirectory)
    {
        if (isDirectory)
        {
            return "<dir>";
        }
        var ext = Path.GetExtension(fullPath).ToLowerInvariant();
        // 埋め込みアイコンを持つ種類はファイルごとに異なるためパス単位でキャッシュ
        return ext is ".exe" or ".ico" or ".lnk"
            ? fullPath.ToLowerInvariant()
            : (ext.Length > 1 ? ext : "<none>");
    }

    private static async Task<ImageSource?> LoadAsync(string fullPath, bool isDirectory, bool distinctByPath)
    {
        try
        {
            // 一時的な失敗（シェル側の都合で SHGetFileInfoW が空を返すことがある）は 1 回だけ即リトライする
            var pixels = await Task.Run(() =>
                ExtractIconPixels(fullPath, isDirectory, distinctByPath)
                ?? ExtractIconPixels(fullPath, isDirectory, distinctByPath));
            if (pixels is not { } icon)
            {
                return null;
            }
            // WriteableBitmap は UI スレッドでのみ生成できる（await 後はディスパッチャ経由で UI に戻る）
            var bitmap = new WriteableBitmap(icon.Width, icon.Height);
            using (var stream = bitmap.PixelBuffer.AsStream())
            {
                stream.Write(icon.Bgra, 0, icon.Bgra.Length);
            }
            bitmap.Invalidate();
            return bitmap;
        }
        catch
        {
            return null; // 失敗時は従来グリフのまま(spec のエラーケース)
        }
    }

    // async メソッドと unsafe は同居できない(CS4004)ため、ポインタを使う 2 メソッドだけ unsafe にする
    private static unsafe (byte[] Bgra, int Width, int Height)? ExtractIconPixels(string fullPath, bool isDirectory, bool distinctByPath)
    {
        var info = default(SHFILEINFOW);
        var flags = NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_SMALLICON;
        uint attributes = 0;
        var lookupPath = fullPath;

        // distinctByPath はドライブ・ホーム等。属性ベースだと汎用フォルダアイコンになるため、
        // 実パスをそのまま引く（この分岐はフラグを足さない）
        if (distinctByPath)
        {
            lookupPath = fullPath;
        }
        else if (isDirectory)
        {
            // 汎用フォルダアイコン: 属性ベース取得でディスクに触れない
            flags |= NativeMethods.SHGFI_USEFILEATTRIBUTES;
            attributes = NativeMethods.FILE_ATTRIBUTE_DIRECTORY;
            lookupPath = "folder";
        }
        else
        {
            var ext = Path.GetExtension(fullPath).ToLowerInvariant();
            if (ext is not (".exe" or ".ico" or ".lnk"))
            {
                // 拡張子の関連付けアイコン: 同じくディスクに触れない（存在しないファイルでも取れる）
                flags |= NativeMethods.SHGFI_USEFILEATTRIBUTES;
                attributes = NativeMethods.FILE_ATTRIBUTE_NORMAL;
                lookupPath = ext.Length > 1 ? "dummy" + ext : "dummy";
            }
        }

        if (NativeMethods.SHGetFileInfoW(lookupPath, attributes, ref info, (uint)sizeof(SHFILEINFOW), flags) == 0 ||
            info.hIcon == 0)
        {
            return null;
        }
        try
        {
            return IconToBgra(info.hIcon);
        }
        finally
        {
            NativeMethods.DestroyIcon(info.hIcon);
        }
    }

    private static unsafe (byte[] Bgra, int Width, int Height)? IconToBgra(nint hIcon)
    {
        if (!NativeMethods.GetIconInfo(hIcon, out var iconInfo))
        {
            return null;
        }
        try
        {
            if (iconInfo.hbmColor == 0)
            {
                return null; // モノクロアイコン(hbmMask のみ)は対象外 → グリフのまま
            }
            var bitmap = default(BITMAP);
            if (NativeMethods.GetObjectW(iconInfo.hbmColor, sizeof(BITMAP), (nint)(&bitmap)) == 0)
            {
                return null;
            }
            var width = bitmap.bmWidth;
            var height = bitmap.bmHeight;
            if (width <= 0 || height <= 0 || width > 256 || height > 256)
            {
                return null;
            }

            var header = new BITMAPINFOHEADER
            {
                biSize = sizeof(BITMAPINFOHEADER),
                biWidth = width,
                biHeight = -height, // トップダウン
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0, // BI_RGB
            };
            var data = new byte[width * height * 4];
            var hdc = NativeMethods.GetDC(0);
            try
            {
                fixed (byte* pData = data)
                {
                    if (NativeMethods.GetDIBits(hdc, iconInfo.hbmColor, 0, (uint)height,
                            (nint)pData, (nint)(&header), NativeMethods.DIB_RGB_COLORS) == 0)
                    {
                        return null;
                    }
                }
            }
            finally
            {
                NativeMethods.ReleaseDC(0, hdc);
            }

            // 旧式(アルファなし 32bpp 変換)アイコンはアルファが全ゼロになる → 不透明にフォールバック
            var hasAlpha = false;
            for (var i = 3; i < data.Length; i += 4)
            {
                if (data[i] != 0)
                {
                    hasAlpha = true;
                    break;
                }
            }
            if (!hasAlpha)
            {
                for (var i = 3; i < data.Length; i += 4)
                {
                    data[i] = 0xFF;
                }
            }
            return (data, width, height);
        }
        finally
        {
            NativeMethods.DeleteObject(iconInfo.hbmColor);
            NativeMethods.DeleteObject(iconInfo.hbmMask);
        }
    }
}
