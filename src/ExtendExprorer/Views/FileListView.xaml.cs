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
        Loading.IsActive = _viewModel?.IsLoading == true;
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

    private void OnSortName(object sender, RoutedEventArgs e) => _viewModel?.SetSort(SortColumn.Name);
    private void OnSortModified(object sender, RoutedEventArgs e) => _viewModel?.SetSort(SortColumn.Modified);
    private void OnSortType(object sender, RoutedEventArgs e) => _viewModel?.SetSort(SortColumn.Type);
    private void OnSortSize(object sender, RoutedEventArgs e) => _viewModel?.SetSort(SortColumn.Size);
}
