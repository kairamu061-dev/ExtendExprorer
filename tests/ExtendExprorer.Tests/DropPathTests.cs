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
}
