using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ExtendExprorer.ViewModels;

public partial class PaneViewModel : LayoutNodeViewModel
{
    public ObservableCollection<TabViewModel> Tabs { get; } = new();

    // [ObservableProperty] は AOT 非対応(MVVMTK0045)のため手書きプロパティにしている
    private TabViewModel? _activeTab;
    public TabViewModel? ActiveTab
    {
        get => _activeTab;
        set => SetProperty(ref _activeTab, value);
    }
}
