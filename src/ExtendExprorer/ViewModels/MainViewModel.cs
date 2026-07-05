using CommunityToolkit.Mvvm.ComponentModel;
using ExtendExprorer.Services;

namespace ExtendExprorer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IFileSystemService _fs;

    [ObservableProperty]
    private LayoutNodeViewModel layout;

    [ObservableProperty]
    private PaneViewModel activePane;

    public MainViewModel(IFileSystemService fs)
    {
        _fs = fs;
        var pane = new PaneViewModel();
        var tab = new TabViewModel(fs);
        pane.Tabs.Add(tab);
        pane.ActiveTab = tab;
        layout = pane;
        activePane = pane;
    }

    public async Task InitializeAsync()
    {
        if (ActivePane.ActiveTab is { } tab)
        {
            await tab.NavigateAsync(_fs.HomePath);
        }
    }
}
