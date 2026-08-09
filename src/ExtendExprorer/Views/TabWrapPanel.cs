using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ExtendExprorer.Views;

/// <summary>タブ帯のレイアウト。左から詰めて、入らなくなったら次の行へ折り返す。
/// <para>行の<b>下端</b>を揃えて配置するのが要点。アクティブなタブだけ背が高いので、
/// 上端を揃えると下辺がずれてタブ帯と一覧の境目が崩れる。</para>
/// <para><c>partial</c> は必須（WinRT 型を継承するため・BUG-013）。</para></summary>
internal sealed partial class TabWrapPanel : Panel
{
    /// <summary>1 行の高さ。非アクティブなタブの高さ＋アクティブが伸びるぶん。</summary>
    internal const double RowHeight = 23;

    /// <summary>非アクティブなタブの高さ。</summary>
    internal const double TabHeight = 20;

    /// <summary>アクティブなタブを上に伸ばす量（開いているタブを目立たせる）。</summary>
    internal const double ActiveExtraHeight = RowHeight - TabHeight;

    internal const double TabMinWidth = 60;
    internal const double TabMaxWidth = 240;

    /// <summary>測定で決めた各子の幅。配置でも同じ値を使う（測り直すと結果がぶれる）。</summary>
    private readonly List<double> _widths = new();

    /// <summary>アクティブなタブ。この要素だけ背を高くする。</summary>
    internal UIElement? ActiveChild { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var limit = LineLimit(availableSize.Width);
        _widths.Clear();
        double x = 0, widest = 0;
        var rows = 1;
        foreach (var child in Children)
        {
            // まず中身の希望幅を測り、クランプした幅で測り直す。
            // こうしないと、上限で切り詰めたときに文字の省略記号が出ない
            child.Measure(new Size(double.PositiveInfinity, RowHeight));
            var width = child is TabStripItem
                ? Math.Clamp(child.DesiredSize.Width, TabMinWidth, TabMaxWidth)
                : child.DesiredSize.Width;
            child.Measure(new Size(width, RowHeight));
            _widths.Add(width);

            if (x > 0 && x + width > limit)
            {
                widest = Math.Max(widest, x);
                x = 0;
                rows++;
            }
            x += width;
        }
        widest = Math.Max(widest, x);
        // 幅が未確定（無限大）のときだけ実測幅を返す。決まっているならそれに従う
        var width2 = double.IsInfinity(availableSize.Width) ? widest : availableSize.Width;
        return new Size(width2, rows * RowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var limit = LineLimit(finalSize.Width);
        double x = 0;
        var row = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            // 測定で決めた幅を使う。測定していない子（追加直後）は自前の希望幅で置く
            var width = i < _widths.Count ? _widths[i] : child.DesiredSize.Width;
            if (x > 0 && x + width > limit)
            {
                x = 0;
                row++;
            }
            var height = ReferenceEquals(child, ActiveChild) ? RowHeight : TabHeight;
            // 行の下端を揃える（アクティブだけ上へ伸びる）
            var y = (row * RowHeight) + (RowHeight - height);
            child.Arrange(new Rect(x, y, width, height));
            x += width;
        }
        return new Size(finalSize.Width, (row + 1) * RowHeight);
    }

    /// <summary>折り返し幅。幅が未確定のうちは折り返さない（1 行として扱う）。</summary>
    private static double LineLimit(double availableWidth) =>
        double.IsInfinity(availableWidth) || availableWidth <= 0 ? double.PositiveInfinity : availableWidth;
}
