using System.ComponentModel;
using ExtendExprorer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace ExtendExprorer.Views;

/// <summary>タブ帯のタブ 1 枚。<see cref="TabStrip"/> がコードで生成する。
/// 「×」ボタンは置かず、ホイールクリックと右クリックメニューで閉じる（2026-08-09 ユーザ要望）。</summary>
public sealed partial class TabStripItem : UserControl
{
    private static readonly SolidColorBrush NormalBrush = Solid(0xF0, 0xF0, 0xF0);
    private static readonly SolidColorBrush HoverBrush = Solid(0xE5, 0xF1, 0xFB);
    private static readonly SolidColorBrush ActiveBrush = Solid(0xFF, 0xFF, 0xFF);
    private static readonly SolidColorBrush AccentBrush = Solid(0x00, 0x78, 0xD4);
    private static readonly SolidColorBrush NoBrush = new(Windows.UI.Color.FromArgb(0, 0, 0, 0));

    private MenuFlyout? _menu;
    private TabViewModel? _tab;
    private bool _hovered;
    private bool _active;

    /// <summary>クリックされた（アクティブにしてほしい）。</summary>
    public event Action<TabViewModel>? Selected;

    /// <summary>ホイールクリック、または右クリックメニューの「タブを閉じる」。</summary>
    public event Action<TabViewModel>? CloseRequested;

    /// <summary>右クリックメニューの「他のタブを閉じる」。</summary>
    public event Action<TabViewModel>? CloseOthersRequested;

    public TabStripItem()
    {
        InitializeComponent();
        PointerEntered += (_, _) => { _hovered = true; Repaint(); };
        PointerExited += (_, _) => { _hovered = false; Repaint(); };
        PointerPressed += OnPointerPressed;
        ContextRequested += OnContextRequested;
        Repaint();
    }

    /// <summary>表示するタブ。差し替えると購読も張り替える。</summary>
    public TabViewModel? Tab
    {
        get => _tab;
        set
        {
            if (ReferenceEquals(_tab, value))
            {
                return;
            }
            if (_tab is not null)
            {
                _tab.PropertyChanged -= OnTabPropertyChanged;
            }
            _tab = value;
            if (_tab is not null)
            {
                _tab.PropertyChanged += OnTabPropertyChanged;
            }
            UpdateContent();
        }
    }

    /// <summary>アクティブなタブか。背景と下線が変わり、<see cref="TabWrapPanel"/> が背を高くする。</summary>
    public bool IsActive
    {
        get => _active;
        set
        {
            if (_active == value)
            {
                return;
            }
            _active = value;
            Repaint();
        }
    }

    /// <summary>破棄前に購読を切る（生存 ViewModel が古い View を掴み続けるのを防ぐ・BUG-002）。</summary>
    public void Detach() => Tab = null;

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateContent();

    private void UpdateContent()
    {
        TitleText.Text = _tab?.Title ?? "";
        IconImage.Source = _tab?.Icon;
        FallbackIcon.Visibility = _tab?.FallbackIconVisibility ?? Visibility.Visible;
        ToolTipService.SetToolTip(this, _tab?.Path ?? "");
        AutomationProperties.SetName(this, _tab?.Title ?? "");
    }

    private void Repaint()
    {
        Root.Background = _active ? ActiveBrush : _hovered ? HoverBrush : NormalBrush;
        Root.BorderBrush = _active ? AccentBrush : NoBrush;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_tab is null)
        {
            return;
        }
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsMiddleButtonPressed)
        {
            CloseRequested?.Invoke(_tab);
            e.Handled = true;
        }
        else if (props.IsLeftButtonPressed)
        {
            Selected?.Invoke(_tab);
            // ペインの活性化には使わせたいので Handled にしない
        }
    }

    /// <summary>右クリックメニューは<b>初めて必要になったときに作る</b>。
    /// タブごとに作ると生成が重く、タブが増えるほど 1 枚追加が遅くなる（BUG-016）。</summary>
    private void OnContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (_tab is null)
        {
            return;
        }
        _menu ??= BuildContextMenu();
        if (args.TryGetPosition(this, out var position))
        {
            _menu.ShowAt(this, new FlyoutShowOptions { Position = position });
        }
        else
        {
            _menu.ShowAt(this);
        }
        args.Handled = true;
    }

    private MenuFlyout BuildContextMenu()
    {
        var close = new MenuFlyoutItem { Text = "タブを閉じる" };
        close.Click += (_, _) =>
        {
            if (_tab is not null)
            {
                CloseRequested?.Invoke(_tab);
            }
        };
        var closeOthers = new MenuFlyoutItem { Text = "他のタブを閉じる" };
        closeOthers.Click += (_, _) =>
        {
            if (_tab is not null)
            {
                CloseOthersRequested?.Invoke(_tab);
            }
        };
        var menu = new MenuFlyout();
        menu.Items.Add(close);
        menu.Items.Add(closeOthers);
        return menu;
    }

    private static SolidColorBrush Solid(byte r, byte g, byte b) =>
        new(Windows.UI.Color.FromArgb(0xFF, r, g, b));
}
