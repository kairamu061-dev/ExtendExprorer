using ExtendExprorer.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace ExtendExprorer.Views;

/// <summary>ペイン境界のドラッグ用バー。ドラッグ中は Grid の Star 値を直接更新し、
/// 完了時に SplitNodeViewModel.Ratio へ書き戻す（セッション保存対象のため）。</summary>
internal sealed class SplitterBar : Border
{
    private const double MinRatio = 0.1;
    private const double MaxRatio = 0.9;

    private readonly SplitNodeViewModel _node;
    private readonly Grid _owner;
    private bool _dragging;
    private double _ratio;

    public SplitterBar(SplitNodeViewModel node, Grid owner)
    {
        _node = node;
        _owner = owner;
        _ratio = node.Ratio;

        var vertical = node.Direction == SplitDirection.Vertical;
        if (vertical)
        {
            Width = 6;
        }
        else
        {
            Height = 6;
        }
        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x30, 0x80, 0x80, 0x80));
        ProtectedCursor = InputSystemCursor.Create(
            vertical ? InputSystemCursorShape.SizeWestEast : InputSystemCursorShape.SizeNorthSouth);

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _dragging = CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }
        var position = e.GetCurrentPoint(_owner).Position;
        var ratio = _node.Direction == SplitDirection.Vertical
            ? position.X / Math.Max(1, _owner.ActualWidth)
            : position.Y / Math.Max(1, _owner.ActualHeight);
        _ratio = Math.Clamp(ratio, MinRatio, MaxRatio);
        Apply(_ratio);
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
        _node.Ratio = _ratio;
        e.Handled = true;
    }

    private void Apply(double ratio)
    {
        if (_node.Direction == SplitDirection.Vertical)
        {
            _owner.ColumnDefinitions[0].Width = new GridLength(ratio, GridUnitType.Star);
            _owner.ColumnDefinitions[2].Width = new GridLength(1 - ratio, GridUnitType.Star);
        }
        else
        {
            _owner.RowDefinitions[0].Height = new GridLength(ratio, GridUnitType.Star);
            _owner.RowDefinitions[2].Height = new GridLength(1 - ratio, GridUnitType.Star);
        }
    }
}
