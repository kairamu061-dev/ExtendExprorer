namespace ExtendExprorer.Models;

/// <summary>一覧の各列に出す文字列の作り方。
///
/// <para><b>あらかじめ持たせない</b>のが現行 WinUI 版との違い。あちらは行ごとの
/// <c>EntryViewModel</c> が整形済みの文字列を 3 つ抱えていたが、こちらは
/// <c>LVN_GETDISPINFOW</c> で聞かれたときに作る。1 万件のフォルダでも、
/// 実際に作るのは画面に見えている数十行分だけで済む。</para></summary>
internal static class EntryFormat
{
    /// <summary>種類の列。拡張子の大文字表記（file-list 仕様）。
    /// シェルの <c>SHGFI_TYPENAME</c>（「テキスト ドキュメント」等）ではなく、
    /// 現行版と同じ表記を保つ。</summary>
    internal static string TypeLabel(Entry entry) =>
        entry.IsDirectory
            ? "フォルダ"
            : System.IO.Path.GetExtension(entry.Name) is { Length: > 1 } ext
                ? ext.TrimStart('.').ToUpperInvariant()
                : "ファイル";

    internal static string SizeLabel(Entry entry) =>
        entry.IsDirectory ? "—" : FormatSize(entry.Size);

    internal static string ModifiedLabel(Entry entry) =>
        entry.Modified.ToString("yyyy/MM/dd HH:mm");

    private static string FormatSize(long bytes)
    {
        // エクスプローラー同様に KB 切り上げ、1MB 以上は単位を上げる
        if (bytes < 1024 * 1024)
        {
            return $"{Math.Max(1, (bytes + 1023) / 1024):N0} KB";
        }
        if (bytes < 1024L * 1024 * 1024)
        {
            return $"{bytes / (1024.0 * 1024):0.0} MB";
        }
        return $"{bytes / (1024.0 * 1024 * 1024):0.0} GB";
    }
}
