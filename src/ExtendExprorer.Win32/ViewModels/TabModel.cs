namespace ExtendExprorer.ViewModels;

/// <summary>タブ 1 枚が覚えていること。
///
/// <para><b>一覧の中身は持たない。</b>ここにあるのはパスと履歴と並び順だけで、
/// 項目の配列はペインがひとつだけ持つ <see cref="FileListViewModel"/> の側にある
/// （<c>docs/win32-migration/design.md</c>）。現行 WinUI 版はタブごとに全件の
/// ViewModel を抱えていて、タブを増やすたびにフォルダ列挙が走っていた（BUG-016）。
/// この構成なら、タブが 30 枚あっても一覧のデータは 1 枚ぶんしか存在しない。</para></summary>
internal sealed class TabModel
{
    internal string Path { get; set; } = "";

    /// <summary>このタブの移動履歴。文字列だけなので、枚数が増えても軽い。</summary>
    internal List<string> History { get; } = [];

    internal int HistoryIndex { get; set; } = -1;

    internal SortColumn SortColumn { get; set; } = SortColumn.Name;
    internal bool SortAscending { get; set; } = true;

    /// <summary>タブ見出し。フォルダ名（ドライブルートはパスそのもの）。</summary>
    internal string Title
    {
        get
        {
            if (string.IsNullOrEmpty(Path))
            {
                return "新しいタブ";
            }
            var name = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(Path));
            return string.IsNullOrEmpty(name) ? Path : name;
        }
    }
}
