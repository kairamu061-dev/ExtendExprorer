using System.ComponentModel;
using ExtendExprorer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace ExtendExprorer.Views;

public sealed partial class FileListView : UserControl
{
    private TabViewModel? _viewModel;

    public TabViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }
            _viewModel = value;
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
                List.ItemsSource = _viewModel.Entries;
            }
            else
            {
                List.ItemsSource = null;
            }
            UpdateState();
        }
    }

    public FileListView()
    {
        InitializeComponent();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateState();

    private void UpdateState()
    {
        var error = _viewModel?.ErrorMessage;
        ErrorText.Text = error ?? "";
        ErrorText.Visibility = string.IsNullOrEmpty(error) ? Visibility.Collapsed : Visibility.Visible;
        List.Visibility = string.IsNullOrEmpty(error) ? Visibility.Visible : Visibility.Collapsed;
        LoadingRing.IsActive = _viewModel?.IsLoading == true;
    }

    private void OnItemDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }
        if ((e.OriginalSource as FrameworkElement)?.DataContext is EntryViewModel entry && entry.IsDirectory)
        {
            _ = _viewModel.NavigateAsync(System.IO.Path.Combine(_viewModel.Path, entry.Name));
        }
    }

    /// <summary>右クリック: 項目上なら選択してシェルの項目メニュー、空白なら表示中フォルダの背景メニュー。</summary>
    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (_viewModel is null || string.IsNullOrEmpty(_viewModel.Path))
        {
            return;
        }
        var hwnd = GetWindowHandle();
        if (hwnd == 0)
        {
            return;
        }
        if ((e.OriginalSource as FrameworkElement)?.DataContext is EntryViewModel entry)
        {
            List.SelectedItem = entry;
            Services.ShellContextMenuService.ShowForItem(
                hwnd, System.IO.Path.Combine(_viewModel.Path, entry.Name));
        }
        else
        {
            Services.ShellContextMenuService.ShowForBackground(hwnd, _viewModel.Path);
        }
        e.Handled = true;
    }

    private nint GetWindowHandle()
    {
        // シェルメニューの所有者に使う HWND。XamlRoot からこの View が載っているウィンドウを引く
        var environment = XamlRoot?.ContentIslandEnvironment;
        return environment is null
            ? 0
            : Microsoft.UI.Win32Interop.GetWindowFromWindowId(environment.AppWindowId);
    }

    private void OnSortName(object sender, RoutedEventArgs e) => _viewModel?.SetSort(SortColumn.Name);
    private void OnSortModified(object sender, RoutedEventArgs e) => _viewModel?.SetSort(SortColumn.Modified);
    private void OnSortType(object sender, RoutedEventArgs e) => _viewModel?.SetSort(SortColumn.Type);
    private void OnSortSize(object sender, RoutedEventArgs e) => _viewModel?.SetSort(SortColumn.Size);
}
