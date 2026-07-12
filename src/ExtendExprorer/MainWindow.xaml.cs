using ExtendExprorer.Services;
using ExtendExprorer.ViewModels;
using Microsoft.UI.Xaml;

namespace ExtendExprorer;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow(MainViewModel viewModel, IFileSystemService fileSystem)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Title = "ExtendExprorer";
        Host.ViewModel = ViewModel;
        TreePanel.Initialize(fileSystem);
        TreePanel.FolderInvoked += ViewModel.NavigateActiveTab;
    }
}
