using System.ComponentModel;
using ExtendExprorer.ViewModels;
using Microsoft.UI.Xaml;

namespace ExtendExprorer;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    private TabViewModel Tab => ViewModel.ActivePane.ActiveTab!;

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Title = "ExtendExprorer";
        FileList.ViewModel = Tab;
        Tab.PropertyChanged += OnTabPropertyChanged;
        UpdateToolbar();
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateToolbar();

    private void UpdateToolbar()
    {
        PathText.Text = Tab.Path;
        BackButton.IsEnabled = Tab.CanGoBack;
        ForwardButton.IsEnabled = Tab.CanGoForward;
        UpButton.IsEnabled = Tab.CanGoUp;
    }

    private void OnBack(object sender, RoutedEventArgs e) => _ = Tab.GoBackAsync();
    private void OnForward(object sender, RoutedEventArgs e) => _ = Tab.GoForwardAsync();
    private void OnUp(object sender, RoutedEventArgs e) => _ = Tab.GoUpAsync();
}
