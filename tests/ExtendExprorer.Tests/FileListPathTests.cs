using ExtendExprorer.Models;
using ExtendExprorer.Services;
using ExtendExprorer.UI;
using ExtendExprorer.ViewModels;
using Xunit;

namespace ExtendExprorer.Tests;

/// <summary>行から実パスを作るところ（BUG-048）。
///
/// <para><b>守っているのはこれ。</b>列挙は非同期なので、<c>Path</c>（これから見せる先）が
/// 新しくなってから行が入れ替わるまでに隙間がある。その隙間で行を解釈すると、
/// <b>新しいパス＋古い行の名前</b>になり、<c>D:\写真\写真</c> のような二重のパスになる。
/// 削除・切り取り・持ち出しも同じ組を読んでいたので、<b>別のフォルダに当たり得た</b>。</para>
///
/// <para><b>この隙間は、実機を使わずに組める。</b><c>ListAsync</c> を完了済みの
/// <c>Task</c> で返させ、<c>UiDispatcher.Drain()</c> を<b>呼ばない</b>でおけば、
/// 「パスは新しく、行は古い」状態がそのまま再現する。
/// 6 週間「全部 UI だから自動テストは無理」と思っていた類の場所がこれ。</para></summary>
public sealed class FileListPathTests
{
    private const string FolderA = @"C:\A";
    private const string FolderPhotos = @"C:\A\写真";

    /// <summary>列挙の結果を言われたとおりに返すだけの代用。<b>完了済みの Task を返す</b>ので、
    /// <c>Load</c> から <c>UiDispatcher.Post</c> までが同期で進み、
    /// 「掃き出す前」の状態で止められる。</summary>
    private sealed class FakeFs : IFileSystemService
    {
        private readonly Dictionary<string, IReadOnlyList<Entry>> _byPath = new(StringComparer.OrdinalIgnoreCase);

        internal FakeFs Add(string path, params string[] names)
        {
            _byPath[path] = [.. names.Select(n => new Entry(n, true, 0L, default, false))];
            return this;
        }

        public string HomePath => FolderA;

        public Task<ListResult> ListAsync(string path) => Task.FromResult<ListResult>(
            _byPath.TryGetValue(path, out var entries)
                ? new ListOk(entries)
                : new ListError(ListErrorKind.NotFound, "ない"));

        public Task<IReadOnlyList<Entry>> ListDirectoriesAsync(string path) =>
            Task.FromResult<IReadOnlyList<Entry>>([]);

        public Task<IReadOnlyList<DriveRow>> ListDrivesAsync() =>
            Task.FromResult<IReadOnlyList<DriveRow>>([]);

        public Task<string?> ResolveNavigationTargetAsync(string input) => Task.FromResult<string?>(input);
    }

    /// <summary>A を開いて行が並んだところまで進める。</summary>
    private static FileListViewModel Showing(string folder, FakeFs fs)
    {
        UiDispatcher.Drain();   // 前の試験が残した分を掃く（行列は static の 1 本）
        var vm = new FileListViewModel(fs);
        vm.Navigate(folder);
        UiDispatcher.Drain();   // ここで Apply が走り、行と EntriesPath が揃う
        return vm;
    }

    // --- 遷移が終わる前（これが BUG-048 の窓） ---

    /// <summary>★ この 1 件が本題。</summary>
    [Fact]
    public void 遷移中のダブルクリックは二重のパスにならない()
    {
        var fs = new FakeFs().Add(FolderA, "写真").Add(FolderPhotos, "子");
        using var vm = Showing(FolderA, fs);

        vm.Navigate(FolderPhotos);   // Path は進むが、行はまだ A のもの（Drain を呼ばない）

        Assert.Equal(FolderPhotos, vm.Path);        // 行き先は進んでいる
        Assert.Equal(FolderA, vm.EntriesPath);      // 行の持ち主は A のまま
        Assert.Equal(FolderPhotos, vm.PathOf(0));   // ★ C:\A\写真\写真 ではない
    }

    [Fact]
    public void 遷移が終われば新しいフォルダの行を指す()
    {
        var fs = new FakeFs().Add(FolderA, "写真").Add(FolderPhotos, "子");
        using var vm = Showing(FolderA, fs);

        vm.Navigate(FolderPhotos);
        UiDispatcher.Drain();

        Assert.Equal(FolderPhotos, vm.EntriesPath);
        Assert.Equal(@"C:\A\写真\子", vm.PathOf(0));
    }

    /// <summary>★ 同じ名前のファイルが両方にあるときが、いちばん危ない形。
    /// 削除がこれを使うので、間違えると<b>見えていないフォルダのファイルが消える</b>。</summary>
    [Fact]
    public void 遷移中は同名の行でも元のフォルダを指す()
    {
        var fs = new FakeFs().Add(@"C:\A", "report.txt").Add(@"C:\B", "report.txt");
        using var vm = Showing(@"C:\A", fs);

        vm.Navigate(@"C:\B");

        Assert.Equal(@"C:\A\report.txt", vm.PathOf(0));   // 画面に出ているのは A の行
    }

    [Fact]
    public void 読み込めなかったフォルダでは行が残らない()
    {
        var fs = new FakeFs().Add(FolderA, "写真");
        using var vm = Showing(FolderA, fs);

        vm.Navigate(@"C:\ない");
        UiDispatcher.Drain();

        Assert.Empty(vm.Entries);
        Assert.Null(vm.PathOf(0));
    }

    // --- 行が無い・範囲外 ---

    [Fact]
    public void 最初の一覧が届く前は何も指さない()
    {
        UiDispatcher.Drain();
        using var vm = new FileListViewModel(new FakeFs().Add(FolderA, "写真"));

        vm.Navigate(FolderA);   // Drain を呼ばない＝行はまだ無い

        Assert.Equal("", vm.EntriesPath);
        Assert.Null(vm.PathOf(0));
    }

    [Fact]
    public void 範囲外の行は何も指さない()
    {
        var fs = new FakeFs().Add(FolderA, "写真");
        using var vm = Showing(FolderA, fs);

        Assert.Null(vm.PathOf(1));
        Assert.Null(vm.PathOf(-1));
    }

    // --- 履歴（二重のパスのもう半分） ---

    [Fact]
    public void 同じパスを連続して履歴に積まない()
    {
        var fs = new FakeFs().Add(FolderA, "写真").Add(FolderPhotos, "子");
        using var vm = Showing(FolderA, fs);

        vm.Navigate(FolderPhotos);
        vm.Navigate(FolderPhotos);   // 遷移前にもう一度ダブルクリックした形
        UiDispatcher.Drain();

        Assert.True(vm.CanGoBack);
        vm.GoBack();
        UiDispatcher.Drain();
        // ★ 1 回で A に戻る。積んでいると 1 回目が空振りする
        Assert.Equal(FolderA, vm.EntriesPath);
    }

    [Fact]
    public void 別のパスなら履歴に積む()
    {
        var fs = new FakeFs().Add(FolderA, "写真").Add(FolderPhotos, "子");
        using var vm = Showing(FolderA, fs);

        vm.Navigate(FolderPhotos);
        UiDispatcher.Drain();
        vm.GoBack();
        UiDispatcher.Drain();

        Assert.Equal(FolderA, vm.EntriesPath);
        Assert.True(vm.CanGoForward);
    }

    // --- 遷移中か（2026-10-23 のご要望。BUG-049 の隣） ---
    //
    // ★ ここを自動テストに入れた理由。Navigating を**下ろし忘れると、
    //   一覧のダブルクリックが永久に効かなくなる**。しかも画面は正常に見える——
    //   「開かない」という報告が来ても、原因が遠い。
    //   下ろす道が 3 つある（差し替え・読み込み失敗・タブ切替）ので、全部試す。

    [Fact]
    public void 遷移が始まると遷移中になる()
    {
        var fs = new FakeFs().Add(FolderA, "写真").Add(FolderPhotos, "子");
        using var vm = Showing(FolderA, fs);

        vm.Navigate(FolderPhotos);   // Drain を呼ばない＝まだ差し替わっていない

        Assert.True(vm.Navigating);
    }

    [Fact]
    public void 差し替えが済むと遷移中でなくなる()
    {
        var fs = new FakeFs().Add(FolderA, "写真").Add(FolderPhotos, "子");
        using var vm = Showing(FolderA, fs);

        vm.Navigate(FolderPhotos);
        UiDispatcher.Drain();

        Assert.False(vm.Navigating);
    }

    /// <summary>★ 読み込めなくても下りる。下りないと、
    /// 届かないパスを 1 回開いただけで一覧が死ぬ。</summary>
    [Fact]
    public void 読み込めなかったときも遷移中でなくなる()
    {
        var fs = new FakeFs().Add(FolderA, "写真");
        using var vm = Showing(FolderA, fs);

        vm.Navigate(@"C:\ない");
        UiDispatcher.Drain();

        Assert.False(vm.Navigating);
    }

    /// <summary>★★ タブ切替は読み込みを<b>無効化するだけ</b>で、
    /// 無効にした分の差し替えはもう来ない。ここで下ろさないと永久に残る。</summary>
    [Fact]
    public void 遷移中にタブを切り替えても遷移中が残らない()
    {
        var fs = new FakeFs().Add(FolderA, "写真").Add(FolderPhotos, "子");
        using var vm = Showing(FolderA, fs);

        vm.Navigate(FolderPhotos);
        Assert.True(vm.Navigating);

        // 行き先が空のタブへ切り替える＝Load を通らない経路
        vm.SwitchTo(new TabModel());

        Assert.False(vm.Navigating);
    }

    [Fact]
    public void 別のタブへ切り替えて読み込めば遷移中は下りる()
    {
        var fs = new FakeFs().Add(FolderA, "写真").Add(FolderPhotos, "子");
        using var vm = Showing(FolderA, fs);

        vm.Navigate(FolderPhotos);
        var tab = new TabModel { Path = FolderA };
        tab.History.Add(FolderA);
        tab.HistoryIndex = 0;
        vm.SwitchTo(tab);
        UiDispatcher.Drain();

        Assert.False(vm.Navigating);
        Assert.Equal(FolderA, vm.EntriesPath);
    }

    [Fact]
    public void 最初の読み込み中も遷移中になる()
    {
        UiDispatcher.Drain();
        using var vm = new FileListViewModel(new FakeFs().Add(FolderA, "写真"));

        vm.Navigate(FolderA);

        Assert.True(vm.Navigating);
        UiDispatcher.Drain();
        Assert.False(vm.Navigating);
    }

    /// <summary>経過は判定に使わないが、<b>0 でない</b>ことだけ確かめる
    /// （遷移中でなければ 0 を返す約束）。</summary>
    [Fact]
    public void 遷移中でなければ経過は0()
    {
        var fs = new FakeFs().Add(FolderA, "写真");
        using var vm = Showing(FolderA, fs);

        Assert.False(vm.Navigating);
        Assert.Equal(0L, vm.NavigatingMs);
    }

    // --- まだ見ていないタブは、フォルダの既定で開く（BUG-050） ---
    //
    // ★ ここが抜けていた理由: TabModel.SortColumn は**既定値を持たざるを得ない**ので、
    //   「名前の昇順」と「まだ何も決まっていない」が同じ値になっていた。
    //   だから試験も**その 2 つを区別できる形**で書く——
    //   「名前順になった」だけでは、覚えていたのか既定が外れたのか分からない。

    [Fact]
    public void まだ見ていないタブは既定で読み込む()
    {
        var fs = new FakeFs().Add(FolderA, "写真").Add(FolderPhotos, "子");
        using var vm = Showing(FolderA, fs);

        var fresh = new TabModel { Path = FolderPhotos };
        fresh.History.Add(FolderPhotos);
        fresh.HistoryIndex = 0;
        Assert.False(fresh.SortRemembered);   // ★ 作った直後は「覚えていない」

        vm.SwitchTo(fresh);
        UiDispatcher.Drain();

        // FolderPhotos はダウンロードではないので既定は名前の昇順。
        // ここで見たいのは**既定が当たったこと**なので、SortRemembered で区別する
        Assert.Equal(SortColumn.Name, vm.SortColumn);
        Assert.True(vm.SortAscending);
    }

    /// <summary>★ 一度見たタブは覚える。<b>これが false のままだと、
    /// 切り替えるたびに並び順が既定へ戻る</b>（E-135 が落ちる形）。</summary>
    [Fact]
    public void 一度見たタブは並び順を覚える()
    {
        var fs = new FakeFs().Add(FolderA, "写真").Add(FolderPhotos, "子");
        using var vm = Showing(FolderA, fs);

        var tab = new TabModel { Path = FolderPhotos };
        tab.History.Add(FolderPhotos);
        tab.HistoryIndex = 0;
        vm.SwitchTo(tab);
        UiDispatcher.Drain();

        vm.SetSort(SortColumn.Size);        // 利用者が並べ替えた
        vm.SaveTo(tab);                     // 別のタブへ移るときに控える

        Assert.True(tab.SortRemembered);
        Assert.Equal(SortColumn.Size, tab.SortColumn);
    }

    /// <summary>★★ 覚えているタブへ戻ったら、既定で上書きしない。
    /// ここが壊れると「タブを切り替えるたびに並び順が消える」。</summary>
    [Fact]
    public void 覚えているタブへ戻ると既定で上書きしない()
    {
        var fs = new FakeFs().Add(FolderA, "写真").Add(FolderPhotos, "子");
        using var vm = Showing(FolderA, fs);

        var tab = new TabModel { Path = FolderPhotos, SortColumn = SortColumn.Size, SortAscending = false };
        tab.History.Add(FolderPhotos);
        tab.HistoryIndex = 0;
        tab.SortRemembered = true;          // 一度見たタブ

        vm.SwitchTo(tab);
        UiDispatcher.Drain();

        Assert.Equal(SortColumn.Size, vm.SortColumn);
        Assert.False(vm.SortAscending);
    }

    /// <summary>★★ ダウンロードなら、まだ見ていないタブは<b>更新日時の降順</b>で開く
    /// （BUG-050 そのもの。復元・<c>Ctrl+T</c> がこの道を通る）。</summary>
    [Fact]
    public void まだ見ていないタブがダウンロードなら更新日時の降順()
    {
        var downloads = DownloadsFolder();
        Assert.False(string.IsNullOrEmpty(downloads), "ダウンロードの場所が取れない機械では判定できない");

        var fs = new FakeFs().Add(FolderA, "写真").Add(downloads!, "a.txt", "b.txt");
        using var vm = Showing(FolderA, fs);

        var fresh = new TabModel { Path = downloads! };
        fresh.History.Add(downloads!);
        fresh.HistoryIndex = 0;

        vm.SwitchTo(fresh);
        UiDispatcher.Drain();

        Assert.Equal(SortColumn.Modified, vm.SortColumn);
        Assert.False(vm.SortAscending);     // ★ 新しいものが上
    }

    /// <summary>★ 対照。<b>覚えているタブなら、ダウンロードでも覚えた順</b>。
    /// 上の 1 件だけだと「いつでも降順にしている」のと区別が付かない。</summary>
    [Fact]
    public void 覚えているタブならダウンロードでも覚えた順()
    {
        var downloads = DownloadsFolder();
        Assert.False(string.IsNullOrEmpty(downloads));

        var fs = new FakeFs().Add(FolderA, "写真").Add(downloads!, "a.txt");
        using var vm = Showing(FolderA, fs);

        var tab = new TabModel { Path = downloads!, SortColumn = SortColumn.Name, SortAscending = true };
        tab.History.Add(downloads!);
        tab.HistoryIndex = 0;
        tab.SortRemembered = true;

        vm.SwitchTo(tab);
        UiDispatcher.Drain();

        Assert.Equal(SortColumn.Name, vm.SortColumn);
        Assert.True(vm.SortAscending);
    }

    private static string? DownloadsFolder()
    {
        var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        if (ExtendExprorer.Interop.NativeMethods.SHGetKnownFolderPath(in id, 0, 0, out var buffer) < 0
            || buffer == 0)
        {
            return null;
        }
        try
        {
            return System.Runtime.InteropServices.Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            ExtendExprorer.Interop.NativeMethods.CoTaskMemFree(buffer);
        }
    }
}
