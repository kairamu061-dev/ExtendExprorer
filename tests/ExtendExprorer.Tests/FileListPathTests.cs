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
}
