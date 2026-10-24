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

    /// <summary><b>このタブが並び順を「覚えている」か</b>（BUG-050）。
    ///
    /// <para>上の 2 つは<b>既定値を持たざるを得ない</b>ので、
    /// 「名前の昇順」と「まだ何も決まっていない」が同じ値で表せない。
    /// <see cref="FileListViewModel.SaveTo"/> を通った＝一度このタブを見た、
    /// というときだけ true にして区別する。</para>
    ///
    /// <para>★ <b>false のタブはフォルダの既定で開く</b>（ダウンロードなら更新日時の降順）。
    /// これが無かったので、<b>起動時に復元したタブと <c>Ctrl+T</c> のタブだけ
    /// 名前順で開いていた</b>——いちばん多い開き方で外れていた。</para>
    ///
    /// <para><b>session には保存しない。</b>並び順そのものが保存されていないので、
    /// 復元したタブは「覚えていない」が正しい。</para></summary>
    internal bool SortRemembered { get; set; }

    /// <summary>タブ見出し。フォルダ名（ドライブルートはパスそのもの）。</summary>
    internal string Title
    {
        get
        {
            if (string.IsNullOrEmpty(Path))
            {
                return "新しいタブ";
            }
            // 「PC」のような合言葉は、そのまま見出しに出さない
            if (FileListViewModel.IsDrivesPath(Path))
            {
                return FileListViewModel.DisplayPath(Path);
            }
            var name = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(Path));
            return string.IsNullOrEmpty(name) ? Path : name;
        }
    }
}
