using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace ExtendExprorer.Views;

/// <summary>左のフォルダツリーと右の一覧領域の境界バー。ドラッグでツリーの幅を変える。
/// ペイン分割の <see cref="SplitterBar"/> は比率(Star)を動かすが、こちらは固定幅(px)を動かす。
/// <see cref="Grid"/> を継承しているのは <c>ProtectedCursor</c> を設定するため（Border は sealed）。</summary>
internal sealed class PanelSplitter : Grid
{
    /// <summary>右の一覧に必ず残す幅。ツリーを広げてもこれ以下にはしない。</summary>
    private const double MinContentWidth = 240;

    private FolderTreePanel? _panel;
    private FrameworkElement? _owner;
    private bool _dragging;

    /// <summary>ドラッグ確定時に発火（セッション保存のトリガー）。</summary>
    public event Action? WidthCommitted;

    public PanelSplitter()
    {
        Width = 5;
        Background = SplitterBar.BarBrush;
        ProtectedCursor = SplitterCursors.WestEast;

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
    }

    /// <summary>幅を変える対象と、座標の基準になる親を渡す（MainWindow が配線する）。</summary>
    public void Attach(FolderTreePanel panel, FrameworkElement owner)
    {
        _panel = panel;
        _owner = owner;
        _panel.LayoutStateChanged += UpdateVisibility;
        UpdateVisibility();
    }

    /// <summary>折りたたみ中はドラッグする意味がないので隠す。</summary>
    private void UpdateVisibility() =>
        Visibility = _panel is { IsCollapsed: false } ? Visibility.Visible : Visibility.Collapsed;

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _dragging = CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging || _panel is null || _owner is null)
        {
            return;
        }
        // バーの中心がポインタに来るようにツリーの幅を決める
        var width = e.GetCurrentPoint(_owner).Position.X - (Width / 2);
        // 右の一覧が潰れないよう、ウィンドウ幅に応じて上限も詰める
        var max = Math.Max(FolderTreePanel.MinExpandedWidth, _owner.ActualWidth - MinContentWidth - Width);
        _panel.SetExpandedWidth(Math.Min(width, max), notify: false);
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }
        _dragging = false;
        ReleasePointerCapture(e.Pointer);
        WidthCommitted?.Invoke(); // 確定時だけ保存する（ドラッグ中は保存しない）
        e.Handled = true;
    }
}
