using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ExtendExprorer.ViewModels;

public partial class PaneViewModel : LayoutNodeViewModel
{
    public ObservableCollection<TabViewModel> Tabs { get; } = new();

    [ObservableProperty]
    private TabViewModel? activeTab;
}
