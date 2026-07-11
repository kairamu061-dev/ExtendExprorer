using System.ComponentModel;
using ExtendExprorer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ExtendExprorer.Views;

/// <summary>レイアウト木（LayoutNodeViewModel の二分木）を再帰的に Grid へ描画するルートビュー。</summary>
public sealed partial class LayoutHost : UserControl
{
    private MainViewModel? _viewModel;
    private readonly Dictionary<PaneViewModel, PaneView> _paneViews = new();

    public MainViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (_viewModel is not null)
            {
                _viewModel.LayoutChanged -= Rebuild;
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }
            _viewModel = value;
            if (_viewModel is not null)
            {
                _viewModel.LayoutChanged += Rebuild;
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            }
            Rebuild();
        }
    }

    public LayoutHost()
    {
        InitializeComponent();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ActivePane))
        {
            UpdateActiveHighlight();
        }
    }

    /// <summary>構造変更（分割・ペインクローズ）時にツリー全体を作り直す。Ratio 変更では呼ばれない。</summary>
    private void Rebuild()
    {
        _paneViews.Clear();
        RootGrid.Children.Clear();
        if (_viewModel is null)
        {
            return;
        }
        RootGrid.Children.Add(Build(_viewModel.Layout));
        UpdateSplitEnabled();
        UpdateActiveHighlight();
    }

    private FrameworkElement Build(LayoutNodeViewModel node)
    {
        if (node is PaneViewModel pane)
        {
            var view = new PaneView { ViewModel = pane };
            view.AddTabRequested += () => _viewModel?.DuplicateActiveTab(pane);
            view.TabCloseRequested += tab => _viewModel?.CloseTab(pane, tab);
            view.SplitRequested += direction => _viewModel?.SplitPane(pane, direction);
            view.Activated += () => _viewModel?.ActivatePane(pane);
            _paneViews[pane] = view;
            return view;
        }

        var split = (SplitNodeViewModel)node;
        var grid = new Grid();
        var first = Build(split.First);
        var second = Build(split.Second);
        var splitter = new SplitterBar(split, grid);

        if (split.Direction == SplitDirection.Vertical)
        {
            // 縦分割 = 縦のスプリッターで左右に並べる
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(split.Ratio, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - split.Ratio, GridUnitType.Star) });
            Grid.SetColumn(first, 0);
            Grid.SetColumn(splitter, 1);
            Grid.SetColumn(second, 2);
        }
        else
        {
            // 横分割 = 横のスプリッターで上下に並べる
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(split.Ratio, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1 - split.Ratio, GridUnitType.Star) });
            Grid.SetRow(first, 0);
            Grid.SetRow(splitter, 1);
            Grid.SetRow(second, 2);
        }

        grid.Children.Add(first);
        grid.Children.Add(splitter);
        grid.Children.Add(second);
        return grid;
    }

    private void UpdateSplitEnabled()
    {
        var canSplit = _paneViews.Count < MainViewModel.MaxPanes;
        foreach (var view in _paneViews.Values)
        {
            view.SetSplitEnabled(canSplit);
        }
    }

    private void UpdateActiveHighlight()
    {
        foreach (var (pane, view) in _paneViews)
        {
            view.SetActive(ReferenceEquals(pane, _viewModel?.ActivePane));
        }
    }
}
