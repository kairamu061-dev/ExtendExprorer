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
}
