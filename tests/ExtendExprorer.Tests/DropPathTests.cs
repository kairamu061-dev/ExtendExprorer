using ExtendExprorer.UI;
using Xunit;

namespace ExtendExprorer.Tests;

/// <summary>ドロップ先の判定。<b>境界（<c>C:\A</c> と <c>C:\AB</c>）がまさに間違えやすい</b>ので、
/// 「前方一致しているが別のフォルダ」を必ず 1 件入れる。</summary>
public sealed class DropPathTests
{
    // --- 自分の中には落とせない ---

    [Fact]
    public void IsInsideDragged_自分自身() =>
        Assert.True(ListDropTarget.IsInsideDragged(@"C:\A", [@"C:\A"]));

    [Fact]
    public void IsInsideDragged_末尾の区切りは無視する() =>
        Assert.True(ListDropTarget.IsInsideDragged(@"C:\A\", [@"C:\A"]));

    [Fact]
    public void IsInsideDragged_大文字小文字は区別しない() =>
        Assert.True(ListDropTarget.IsInsideDragged(@"c:\a", [@"C:\A"]));

    [Fact]
    public void IsInsideDragged_子フォルダ() =>
        Assert.True(ListDropTarget.IsInsideDragged(@"C:\A\B", [@"C:\A"]));

    [Fact]
    public void IsInsideDragged_孫フォルダ() =>
        Assert.True(ListDropTarget.IsInsideDragged(@"C:\A\B\C", [@"C:\A"]));

    /// <summary>★ ここが本題。<c>C:\AB</c> は <c>C:\A</c> で始まるが、中ではない。</summary>
    [Fact]
    public void IsInsideDragged_名前が前方一致しているだけの兄弟は中ではない() =>
        Assert.False(ListDropTarget.IsInsideDragged(@"C:\AB", [@"C:\A"]));

    [Fact]
    public void IsInsideDragged_無関係() =>
        Assert.False(ListDropTarget.IsInsideDragged(@"D:\X", [@"C:\A"]));

    [Fact]
    public void IsInsideDragged_複数のうち1つでも当たれば真() =>
        Assert.True(ListDropTarget.IsInsideDragged(@"C:\A\B", [@"D:\X", @"C:\A"]));

    // --- すでに落とし先の中にある（＝移動しても動かない） ---

    [Fact]
    public void AllAlreadyIn_全部が直下() =>
        Assert.True(ListDropTarget.AllAlreadyIn([@"C:\A\1.txt", @"C:\A\2.txt"], @"C:\A"));

    [Fact]
    public void AllAlreadyIn_1つでも外なら偽() =>
        Assert.False(ListDropTarget.AllAlreadyIn([@"C:\A\1.txt", @"C:\B\2.txt"], @"C:\A"));

    /// <summary>孫は「直下」ではない——移動すれば動くので偽。</summary>
    [Fact]
    public void AllAlreadyIn_孫は直下ではない() =>
        Assert.False(ListDropTarget.AllAlreadyIn([@"C:\A\B\1.txt"], @"C:\A"));

    [Fact]
    public void AllAlreadyIn_落とし先の末尾の区切りは無視する() =>
        Assert.True(ListDropTarget.AllAlreadyIn([@"C:\A\1.txt"], @"C:\A\"));

    [Fact]
    public void AllAlreadyIn_名前が前方一致しているだけの兄弟は偽() =>
        Assert.False(ListDropTarget.AllAlreadyIn([@"C:\AB\1.txt"], @"C:\A"));

    // --- コピーか移動か（2026-10-20: タブからタブへは移動） ---
    //
    // ★ 既定が 2 つある状態なので、必ず**両方**を試す。
    //   このアプリ発＝ドライブを越えても移動／外から来た分＝エクスプローラーの既定。
    //   片方だけ書くと、どちらを変えたのかが試験から読めない。

    private const uint Copy = ExtendExprorer.Interop.NativeMethods.DROPEFFECT_COPY;
    private const uint Move = ExtendExprorer.Interop.NativeMethods.DROPEFFECT_MOVE;

    [Fact]
    public void 既定_このアプリ発は同じドライブなら移動() =>
        Assert.Equal(Move, ListDropTarget.DefaultEffect(false, false, true, @"C:\", @"C:\"));

    /// <summary>★ これが今回のご要望そのもの。</summary>
    [Fact]
    public void 既定_このアプリ発はドライブが違っても移動() =>
        Assert.Equal(Move, ListDropTarget.DefaultEffect(false, false, true, @"C:\", @"D:\"));

    [Fact]
    public void 既定_外から来た分は同じドライブなら移動() =>
        Assert.Equal(Move, ListDropTarget.DefaultEffect(false, false, false, @"C:\", @"C:\"));

    /// <summary>★ 外から来た分はエクスプローラーの既定のまま。
    /// ここが移動になっていたら、他のアプリの既定を書き換えてしまっている。</summary>
    [Fact]
    public void 既定_外から来た分はドライブが違えばコピー() =>
        Assert.Equal(Copy, ListDropTarget.DefaultEffect(false, false, false, @"C:\", @"D:\"));

    [Fact]
    public void 既定_ドライブ文字の大文字小文字は区別しない() =>
        Assert.Equal(Move, ListDropTarget.DefaultEffect(false, false, false, @"c:\", @"C:\"));

    // --- 修飾キーはどの規則よりも強い ---

    [Fact]
    public void Ctrl_はこのアプリ発でもコピー() =>
        Assert.Equal(Copy, ListDropTarget.DefaultEffect(true, false, true, @"C:\", @"D:\"));

    [Fact]
    public void Ctrl_は同じドライブでもコピー() =>
        Assert.Equal(Copy, ListDropTarget.DefaultEffect(true, false, false, @"C:\", @"C:\"));

    [Fact]
    public void Shift_はドライブが違っても移動() =>
        Assert.Equal(Move, ListDropTarget.DefaultEffect(false, true, false, @"C:\", @"D:\"));

    [Fact]
    public void Ctrl_と_Shift_が同時なら_Ctrl_が勝つ() =>
        Assert.Equal(Copy, ListDropTarget.DefaultEffect(true, true, true, @"C:\", @"C:\"));

    // --- 開いたときの並び順の既定（2026-10-23: ダウンロードだけ更新日時） ---
    //
    // ★ ダウンロードの場所は **OS に聞く**（移せるので決め打ちにしない）。
    //   だから試験でも同じ API から取る——**決め打ちのパスで試すと、
    //   場所を移している機械で通らないか、あるいは「何も試していない」ことになる。**

    private static string? DownloadsPath()
    {
        var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");   // FOLDERID_Downloads
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

    [Fact]
    public void 既定の並び_ダウンロードは更新日時の降順()
    {
        var downloads = DownloadsPath();
        Assert.False(string.IsNullOrEmpty(downloads), "ダウンロードの場所が取れない機械では判定できない");

        var (column, ascending) = ExtendExprorer.Services.FolderSortSettings.DefaultSort(downloads!);

        Assert.Equal(ExtendExprorer.ViewModels.SortColumn.Modified, column);
        Assert.False(ascending);   // ★ 新しいものが上
    }

    [Fact]
    public void 既定の並び_末尾の区切りがあっても同じ()
    {
        var downloads = DownloadsPath();
        Assert.False(string.IsNullOrEmpty(downloads));

        var (column, _) = ExtendExprorer.Services.FolderSortSettings.DefaultSort(downloads! + @"\");

        Assert.Equal(ExtendExprorer.ViewModels.SortColumn.Modified, column);
    }

    /// <summary>★ 前方一致しているだけの兄弟を取り違えない
    /// （`Downloads` と `Downloads2`）。ドロップ先の判定で同じ間違いをしている。</summary>
    [Fact]
    public void 既定の並び_名前が前方一致する別フォルダは巻き込まない()
    {
        var downloads = DownloadsPath();
        Assert.False(string.IsNullOrEmpty(downloads));

        var (column, ascending) = ExtendExprorer.Services.FolderSortSettings.DefaultSort(downloads! + "2");

        Assert.Equal(ExtendExprorer.ViewModels.SortColumn.Name, column);
        Assert.True(ascending);
    }

    [Fact]
    public void 既定の並び_ほかのフォルダは名前の昇順()
    {
        var (column, ascending) = ExtendExprorer.Services.FolderSortSettings.DefaultSort(@"C:\Windows");

        Assert.Equal(ExtendExprorer.ViewModels.SortColumn.Name, column);
        Assert.True(ascending);
    }

    /// <summary>★ 2 つの既定が<b>同じ判定</b>から出ていること。
    /// 別々に書くと、片方だけ直す日が来る。</summary>
    [Fact]
    public void ダウンロードの2つの既定は同じ判定から出ている()
    {
        var downloads = DownloadsPath();
        Assert.False(string.IsNullOrEmpty(downloads));

        // まとめない（2026-09-18）＋ 更新日時の降順（2026-10-23）
        Assert.False(ExtendExprorer.Services.FolderSortSettings.DefaultFor(downloads!));
        Assert.Equal(ExtendExprorer.ViewModels.SortColumn.Modified,
            ExtendExprorer.Services.FolderSortSettings.DefaultSort(downloads!).Column);

        Assert.True(ExtendExprorer.Services.FolderSortSettings.DefaultFor(@"C:\Windows"));
        Assert.Equal(ExtendExprorer.ViewModels.SortColumn.Name,
            ExtendExprorer.Services.FolderSortSettings.DefaultSort(@"C:\Windows").Column);
    }
}
