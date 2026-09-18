using ExtendExprorer.Models.Session;
using static ExtendExprorer.Interop.NativeMethods;

namespace ExtendExprorer.Services;

/// <summary>フォルダごとの「フォルダを先頭にまとめるか」。
///
/// <para><b>既定と違うものだけ覚える。</b>全部のフォルダを覚えると session が
/// 際限なく太る。既定（ダウンロードは混合・それ以外はフォルダ先頭）と同じ値に
/// 戻されたら、表からも消す。</para>
///
/// <para>ダウンロードだけ既定を変えているのは、そこに落ちてくるものが
/// <b>フォルダとファイルの区別より、新しいかどうかで探される</b>ため
/// （2026-09-18 のご要望）。</para></summary>
internal static class FolderSortSettings
{
    private static readonly Dictionary<string, bool> Overrides = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>ダウンロードフォルダ。<b>場所は移せる</b>ので、決め打ちにせず OS に聞く。</summary>
    private static string? _downloads;
    private static bool _downloadsAsked;

    private static string? Downloads
    {
        get
        {
            if (!_downloadsAsked)
            {
                _downloadsAsked = true;
                try
                {
                    // {374DE290-123F-4565-9164-39C4925E467B} = FOLDERID_Downloads
                    var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
                    if (SHGetKnownFolderPath(in id, 0, 0, out var buffer) >= 0 && buffer != 0)
                    {
                        _downloads = System.Runtime.InteropServices.Marshal.PtrToStringUni(buffer);
                        CoTaskMemFree(buffer);
                    }
                }
                catch (Exception ex)
                {
                    UI.Diagnostics.Report("FolderSortSettings.Downloads", ex);
                }
            }
            return _downloads;
        }
    }

    /// <summary>そのフォルダの既定。ダウンロードだけ「まとめない」。</summary>
    internal static bool DefaultFor(string path) =>
        !(Downloads is { Length: > 0 } downloads
            && path.TrimEnd('\\').Equals(downloads.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));

    /// <summary>そのフォルダでの設定。覚えていなければ既定。</summary>
    internal static bool FoldersFirst(string path) =>
        path.Length > 0 && Overrides.TryGetValue(path, out var value) ? value : DefaultFor(path);

    /// <summary>設定を変える。<b>既定に戻ったら表から消す</b>（session を太らせない）。</summary>
    internal static void Set(string path, bool foldersFirst)
    {
        if (path.Length == 0)
        {
            return;
        }
        if (foldersFirst == DefaultFor(path))
        {
            Overrides.Remove(path);
        }
        else
        {
            Overrides[path] = foldersFirst;
        }
        UI.Diagnostics.Write($"[sort] フォルダ先頭={foldersFirst} {path}（覚えている数={Overrides.Count}）");
    }

    /// <summary>session から読む。</summary>
    internal static void Restore(IEnumerable<FolderSortSnapshot>? saved)
    {
        Overrides.Clear();
        foreach (var item in saved ?? [])
        {
            if (!string.IsNullOrEmpty(item.Path) && item.FoldersFirst != DefaultFor(item.Path))
            {
                Overrides[item.Path] = item.FoldersFirst;
            }
        }
    }

    /// <summary>session へ書く。既定と違うものだけ。</summary>
    internal static List<FolderSortSnapshot> Capture() =>
        [.. Overrides.Select(pair => new FolderSortSnapshot { Path = pair.Key, FoldersFirst = pair.Value })];
}
