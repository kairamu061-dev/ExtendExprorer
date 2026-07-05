using ExtendExprorer.ViewModels;
using Microsoft.UI.Xaml;

namespace ExtendExprorer;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Title = "ExtendExprorer";

        Pane.ViewModel = ViewModel.ActivePane;
        Pane.AddTabRequested += () => ViewModel.DuplicateActiveTab(ViewModel.ActivePane);
        Pane.TabCloseRequested += tab => ViewModel.CloseTab(ViewModel.ActivePane, tab);
    }
}
