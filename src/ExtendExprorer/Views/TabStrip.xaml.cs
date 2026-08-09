using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ExtendExprorer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ExtendExprorer.Views;

/// <summary>ペイン上部のタブ帯。WinUI の <c>TabView</c> の代わりに使う（2026-08-09）。
/// <c>TabView</c> は 1 行固定で、入りきらないタブは横スクロールになり、
/// <b>2 列目への折り返しがテンプレート差し替えでは実現できない</b>ため差し替えた。
/// <para>タブの中身（<see cref="TabViewModel"/>）とタブ規則（<c>MainViewModel</c>）はそのまま使う。
/// このコントロールが持つのは並べ方と入力だけ。</para></summary>
public sealed partial class TabStrip : UserControl
{
    private ObservableCollection<TabViewModel>? _source;
    private TabViewModel? _selected;
    private readonly TabWrapPanel _panel = new();
    private readonly Button _addButton;
    private bool _addButtonVisible = true;

    /// <summary>タブがクリックされた（アクティブにしてほしい）。</summary>
    public event Action<TabViewModel>? SelectionChanged;

    /// <summary>「＋」が押された。何を追加するかは呼び出し側の規則に委ねる。</summary>
    public event Action? AddRequested;

    /// <summary>ホイールクリック / 右クリックメニューでタブを閉じる要求。</summary>
    public event Action<TabViewModel>? CloseRequested;

    /// <summary>右クリックメニューの「他のタブを閉じる」。</summary>
    public event Action<TabViewModel>? CloseOthersRequested;

    public TabStrip()
    {
        InitializeComponent();
        // 内側の Panel は XAML ではなくコードで作る（PanelSplitter と同じ扱い）
        Root.Child = _panel;
        _addButton = new Button
        {
            // PUA のグリフはソース上で文字にせずエスケープで書く（E710 = Add）
            Content = "\uE710",
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets"),
            FontSize = 10,
            Width = 24,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            BorderThickness = new Thickness(0),
            // 透明でも塗っておく。null にするとヒットテストに乗らずクリックできない
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(0, 0, 0, 0)),
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        AutomationProperties.SetName(_addButton, "新しいタブ");
        ToolTipService.SetToolTip(_addButton, "新しいタブ");
        _addButton.Click += (_, _) => AddRequested?.Invoke();
    }

    /// <summary>表示するタブの並び。増減に追随する。</summary>
    public ObservableCollection<TabViewModel>? ItemsSource
    {
        get => _source;
        set
        {
            if (ReferenceEquals(_source, value))
            {
                return;
            }
            if (_source is not null)
            {
                _source.CollectionChanged -= OnItemsChanged;
            }
            _source = value;
            if (_source is not null)
            {
                _source.CollectionChanged += OnItemsChanged;
            }
            Rebuild();
        }
    }

    /// <summary>アクティブなタブ。見た目の更新だけを行い、<see cref="SelectionChanged"/> は発火しない
    /// （呼び出し側からの反映と、ユーザー操作による変更を区別するため）。</summary>
    public TabViewModel? SelectedItem
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value))
            {
                return;
            }
            _selected = value;
            UpdateActiveStates();
        }
    }

    /// <summary>タブ数が上限のときは「＋」を隠す。</summary>
    public bool IsAddButtonVisible
    {
        get => _addButtonVisible;
        set
        {
            if (_addButtonVisible == value)
            {
                return;
            }
            _addButtonVisible = value;
            _addButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            _panel.InvalidateMeasure();
        }
    }

    /// <summary>破棄前に購読をすべて切る（BUG-002 と同じ扱い）。</summary>
    public void Detach()
    {
        ItemsSource = null;
        SelectedItem = null;
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    /// <summary>タブの増減に合わせて子要素を組み直す。タイトル・アイコンの変化は
    /// <see cref="TabStripItem"/> が自分で拾うので、ここでは扱わない。</summary>
    private void Rebuild()
    {
        foreach (var item in _panel.Children.OfType<TabStripItem>())
        {
            item.Detach();
        }
        _panel.Children.Clear();
        if (_source is not null)
        {
            foreach (var tab in _source)
            {
                var item = new TabStripItem { Tab = tab };
                item.Selected += OnItemSelected;
                item.CloseRequested += t => CloseRequested?.Invoke(t);
                item.CloseOthersRequested += t => CloseOthersRequested?.Invoke(t);
                _panel.Children.Add(item);
            }
        }
        // 「＋」は最後のタブの直後に流す（折り返した場合は 2 列目以降の末尾に付く）
        _panel.Children.Add(_addButton);
        UpdateActiveStates();
    }

    private void OnItemSelected(TabViewModel tab) => SelectionChanged?.Invoke(tab);

    private void UpdateActiveStates()
    {
        UIElement? active = null;
        foreach (var item in _panel.Children.OfType<TabStripItem>())
        {
            item.IsActive = ReferenceEquals(item.Tab, _selected);
            if (item.IsActive)
            {
                active = item;
            }
        }
        // アクティブなタブだけ背を高くするのはレイアウト側の仕事
        _panel.ActiveChild = active;
        _panel.InvalidateArrange();
    }
}
