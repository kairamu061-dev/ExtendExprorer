using System.Collections.Specialized;
using System.ComponentModel;
using ExtendExprorer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace ExtendExprorer.Views;

public sealed partial class PaneView : UserControl
{
    // アクティブペインの枠線色(docs/project_overview.md のパレット)
    private static readonly SolidColorBrush ActiveBorderBrush =
        new(Windows.UI.Color.FromArgb(0xFF, 0x00, 0x78, 0xD4));
    private static readonly SolidColorBrush InactiveBorderBrush =
        new(Windows.UI.Color.FromArgb(0xFF, 0xD0, 0xD0, 0xD0));

    private PaneViewModel? _viewModel;
    private TabViewModel? _observedTab;

    /// <summary>「＋」ボタン。タブ複製の実行は MainViewModel 側の規則に委ねる。</summary>
    public event Action? AddTabRequested;

    /// <summary>タブの「×」/ ホイールクリック。クローズ規則は MainViewModel 側に委ねる。</summary>
    public event Action<TabViewModel>? TabCloseRequested;

    /// <summary>「縦分割」「横分割」ボタン。分割はレイアウト木の操作なので MainViewModel に委ねる。</summary>
    public event Action<SplitDirection>? SplitRequested;

    /// <summary>ペイン内の任意箇所クリック（アクティブペイン切替）。</summary>
    public event Action? Activated;

    public PaneViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged -= OnPanePropertyChanged;
                _viewModel.Tabs.CollectionChanged -= OnTabsCollectionChanged;
            }
            _viewModel = value;
            Tabs.TabItemsSource = _viewModel?.Tabs;
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged += OnPanePropertyChanged;
                _viewModel.Tabs.CollectionChanged += OnTabsCollectionChanged;
            }
            SyncSelectionFromViewModel();
            ObserveActiveTab();
            UpdateAddTabButton();
        }
    }

    public PaneView()
    {
        InitializeComponent();
        SetActive(false);
        // 子要素が処理済みのクリック(一覧の行選択など)でもペインを活性化したいので handledEventsToo
        AddHandler(PointerPressedEvent, new PointerEventHandler(OnPanePointerPressed), handledEventsToo: true);
    }

    private void OnPanePointerPressed(object sender, PointerRoutedEventArgs e) => Activated?.Invoke();

    /// <summary>アクティブペインの枠線強調を切り替える（LayoutHost が呼ぶ）。</summary>
    public void SetActive(bool active) =>
        RootBorder.BorderBrush = active ? ActiveBorderBrush : InactiveBorderBrush;

    /// <summary>ペイン数上限時に分割ボタンを無効化する（LayoutHost が呼ぶ）。</summary>
    public void SetSplitEnabled(bool enabled)
    {
        SplitVButton.IsEnabled = enabled;
        SplitHButton.IsEnabled = enabled;
    }

    private void OnSplitVertical(object sender, RoutedEventArgs e) => SplitRequested?.Invoke(SplitDirection.Vertical);

    private void OnSplitHorizontal(object sender, RoutedEventArgs e) => SplitRequested?.Invoke(SplitDirection.Horizontal);

    private void OnPanePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PaneViewModel.ActiveTab))
        {
            SyncSelectionFromViewModel();
            ObserveActiveTab();
        }
    }

    private void SyncSelectionFromViewModel()
    {
        var active = _viewModel?.ActiveTab;
        if (!ReferenceEquals(Tabs.SelectedItem, active))
        {
            Tabs.SelectedItem = active;
        }
    }

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is not null &&
            Tabs.SelectedItem is TabViewModel tab &&
            !ReferenceEquals(_viewModel.ActiveTab, tab))
        {
            _viewModel.ActiveTab = tab;
        }
    }

    private void OnTabsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateAddTabButton();

    private void UpdateAddTabButton() =>
        Tabs.IsAddTabButtonVisible = (_viewModel?.Tabs.Count ?? 0) < MainViewModel.MaxTabsPerPane;

    private void OnAddTabButtonClick(TabView sender, object args) => AddTabRequested?.Invoke();

    private void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Item is TabViewModel tab)
        {
            TabCloseRequested?.Invoke(tab);
        }
    }

    private void ObserveActiveTab()
    {
        if (_observedTab is not null)
        {
            _observedTab.PropertyChanged -= OnTabPropertyChanged;
        }
        _observedTab = _viewModel?.ActiveTab;
        if (_observedTab is not null)
        {
            _observedTab.PropertyChanged += OnTabPropertyChanged;
        }
        FileList.ViewModel = _observedTab;
        UpdateToolbar();
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateToolbar();

    private void UpdateToolbar()
    {
        PathText.Text = _observedTab?.Path ?? "";
        BackButton.IsEnabled = _observedTab?.CanGoBack == true;
        ForwardButton.IsEnabled = _observedTab?.CanGoForward == true;
        UpButton.IsEnabled = _observedTab?.CanGoUp == true;
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (_observedTab is { } tab)
        {
            _ = tab.GoBackAsync();
        }
    }

    private void OnForward(object sender, RoutedEventArgs e)
    {
        if (_observedTab is { } tab)
        {
            _ = tab.GoForwardAsync();
        }
    }

    private void OnUp(object sender, RoutedEventArgs e)
    {
        if (_observedTab is { } tab)
        {
            _ = tab.GoUpAsync();
        }
    }
}
