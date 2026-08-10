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

    /// <summary>変わったぶんだけ子要素を足し引きする。**全部作り直してはいけない**（BUG-016）:
    /// タブ 1 枚の生成は XAML の実体化を伴って重く、毎回 N 枚作り直すとタブが増えるほど
    /// 1 枚追加が遅くなり、20〜30 枚でアプリがハングする。</summary>
    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems is not null:
                for (var i = 0; i < e.NewItems.Count; i++)
                {
                    if (e.NewItems[i] is TabViewModel tab)
                    {
                        _panel.Children.Insert(e.NewStartingIndex + i, CreateItem(tab));
                    }
                }
                break;
            case NotifyCollectionChangedAction.Remove when e.OldItems is not null:
                for (var i = e.OldItems.Count - 1; i >= 0; i--)
                {
                    RemoveItemAt(e.OldStartingIndex + i);
                }
                break;
            case NotifyCollectionChangedAction.Move:
                if (_panel.Children[e.OldStartingIndex] is TabStripItem moved)
                {
                    _panel.Children.RemoveAt(e.OldStartingIndex);
                    _panel.Children.Insert(e.NewStartingIndex, moved);
                }
                break;
            default:
                Rebuild();
                return;
        }
        // 差分適用がずれていたら（想定外の通知順など）作り直して整合させる
        if (_panel.Children.Count != (_source?.Count ?? 0) + 1)
        {
            Rebuild();
            return;
        }
        UpdateActiveStates();
    }

    private void RemoveItemAt(int index)
    {
        if (index < 0 || index >= _panel.Children.Count || _panel.Children[index] is not TabStripItem item)
        {
            return;
        }
        item.Detach();
        _panel.Children.RemoveAt(index);
    }

    private TabStripItem CreateItem(TabViewModel tab)
    {
        var item = new TabStripItem { Tab = tab };
        item.Selected += OnItemSelected;
        item.CloseRequested += t => CloseRequested?.Invoke(t);
        item.CloseOthersRequested += t => CloseOthersRequested?.Invoke(t);
        return item;
    }

    /// <summary>全部作り直す。並びの差し替え（ItemsSource 変更・Reset）のときだけ使う。</summary>
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
                _panel.Children.Add(CreateItem(tab));
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
