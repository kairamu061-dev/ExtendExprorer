using ExtendExprorer.Models;
using static ExtendExprorer.Interop.NativeMethods;

namespace ExtendExprorer.Services;

/// <summary>OS のシステムイメージリストと、その中の番号（アイコンのインデックス）を引く仕組み。
///
/// <para><b>移行の中心となる判断</b>（<c>docs/win32-migration/design.md</c>）。現行 WinUI 版は
/// 項目ごとに <c>HICON</c> を取り出して <c>WriteableBitmap</c> に変換し、ビットマップを抱えていた。
/// ここでは画像の実体を OS 側に置いたまま、コントロールにイメージリストを<b>借りて</b>渡し、
/// 項目ごとに持つのは <c>int</c> ひとつにする。</para>
///
/// <para><b>引き方は拡張子単位でキャッシュする。</b><c>SHGFI_USEFILEATTRIBUTES</c> を付けると
/// 実在しないパスでも「その拡張子の既定アイコン」を返してくれるので、ディスクに触らずに済む。
/// 1 万件のフォルダでも呼ぶのは拡張子の種類の数だけになる。
/// 例外は <see cref="PerFileExtensions"/>（実行ファイルやショートカットなど、
/// ファイルごとに固有のアイコンを持つもの）で、これだけ実パスで引く。</para></summary>
internal static unsafe class ShellImageList
{
    /// <summary>ファイルごとに固有のアイコンを持つ拡張子。ここだけ実パスで引く
    /// （ディスクに触るので、それ以外は拡張子キャッシュで済ませる）。</summary>
    private static readonly HashSet<string> PerFileExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".exe", ".lnk", ".ico", ".cur", ".ani", ".scr", ".msc", ".cpl", ".url" };

    private static readonly Dictionary<string, int> ByExtension = new(StringComparer.OrdinalIgnoreCase);

    private static nint _handle;
    private static int _folderIndex = -1;
    private static int _fileIndex = -1;

    /// <summary>システムイメージリスト（小アイコン）のハンドル。
    /// <b>破棄してはいけない</b>（OS の共有物なので <c>LVS_SHAREIMAGELISTS</c> で借りる）。</summary>
    internal static nint Handle
    {
        get
        {
            if (_handle == 0)
            {
                var info = default(SHFILEINFOW);
                _handle = QueryIcon("folder", FILE_ATTRIBUTE_DIRECTORY, ref info,
                    SHGFI_SYSICONINDEX | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);
                _folderIndex = _handle != 0 ? info.iIcon : -1;
            }
            return _handle;
        }
    }

    /// <summary>この項目のアイコン番号。取れないときはフォルダ／ファイルの既定に落とす。</summary>
    internal static int IndexOf(string folderPath, Entry entry)
    {
        try
        {
            if (entry.IsDirectory)
            {
                return FolderIndex;
            }
            var extension = System.IO.Path.GetExtension(entry.Name);
            if (extension.Length == 0)
            {
                return FileIndex;
            }
            if (PerFileExtensions.Contains(extension))
            {
                // 実パスで引く。読めない（消えた・権限なし）ときは拡張子の既定に落とす
                var info = default(SHFILEINFOW);
                var full = System.IO.Path.Combine(folderPath, entry.Name);
                if (QueryIcon(full, 0, ref info, SHGFI_SYSICONINDEX | SHGFI_SMALLICON) != 0)
                {
                    return info.iIcon;
                }
            }
            if (ByExtension.TryGetValue(extension, out var cached))
            {
                return cached;
            }
            var index = QueryByAttributes("x" + extension, FILE_ATTRIBUTE_NORMAL);
            if (index < 0)
            {
                index = FileIndex;
            }
            ByExtension[extension] = index;
            return index;
        }
        catch (Exception ex)
        {
            // アイコンが出ないことより、一覧が描けないことの方が困る
            UI.Diagnostics.Report($"ShellImageList.IndexOf({entry.Name})", ex);
            return entry.IsDirectory ? FolderIndex : FileIndex;
        }
    }

    /// <summary>実在するパスのアイコン番号（ツリーのルート＝ホーム・ドライブ用）。
    /// ドライブは種類ごとに絵が違い、ホームにも固有の絵があるので、ここだけは実パスで引く。
    /// 数が少なく（ドライブの台数＋1）増えないので、そのまま覚えておく。</summary>
    internal static int IndexOfPath(string path)
    {
        try
        {
            if (ByPath.TryGetValue(path, out var cached))
            {
                return cached;
            }
            var info = default(SHFILEINFOW);
            var index = QueryIcon(path, 0, ref info, SHGFI_SYSICONINDEX | SHGFI_SMALLICON) != 0
                ? info.iIcon
                : FolderIndex;
            ByPath[path] = index;
            return index;
        }
        catch (Exception ex)
        {
            UI.Diagnostics.Report($"ShellImageList.IndexOfPath({path})", ex);
            return FolderIndex;
        }
    }

    private static readonly Dictionary<string, int> ByPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>ふつうのフォルダの番号。ツリーの枝はすべてこれを使う。</summary>
    internal static int Folder => FolderIndex;

    private static int FolderIndex
    {
        get
        {
            _ = Handle; // 初回にフォルダの番号も一緒に取れている
            return _folderIndex;
        }
    }

    private static int FileIndex
    {
        get
        {
            if (_fileIndex < 0)
            {
                _fileIndex = QueryByAttributes("file", FILE_ATTRIBUTE_NORMAL);
            }
            return _fileIndex;
        }
    }

    /// <summary>実在しないパスでも「その属性・拡張子の既定アイコン」を返してもらう
    /// （<c>SHGFI_USEFILEATTRIBUTES</c>）。ディスクには触らない。</summary>
    private static int QueryByAttributes(string pseudoPath, uint attributes)
    {
        var info = default(SHFILEINFOW);
        return QueryIcon(pseudoPath, attributes, ref info,
            SHGFI_SYSICONINDEX | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES) != 0
            ? info.iIcon
            : -1;
    }

    private static nint QueryIcon(string path, uint attributes, ref SHFILEINFOW info, uint flags) =>
        SHGetFileInfoW(path, attributes, ref info, (uint)sizeof(SHFILEINFOW), flags);
}
