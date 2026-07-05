using System.Collections.Specialized;
using System.ComponentModel;
using ExtendExprorer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ExtendExprorer.Views;

public sealed partial class PaneView : UserControl
{
    private PaneViewModel? _viewModel;
    private TabViewModel? _observedTab;

    /// <summary>「＋」ボタン。タブ複製の実行は MainViewModel 側の規則に委ねる。</summary>
    public event Action? AddTabRequested;

    /// <summary>タブの「×」/ ホイールクリック。クローズ規則は MainViewModel 側に委ねる。</summary>
    public event Action<TabViewModel>? TabCloseRequested;

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
    }

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
