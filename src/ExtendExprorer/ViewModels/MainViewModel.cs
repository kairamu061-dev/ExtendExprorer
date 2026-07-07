using CommunityToolkit.Mvvm.ComponentModel;
using ExtendExprorer.Services;

namespace ExtendExprorer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IFileSystemService _fs;

    // [ObservableProperty] は AOT 非対応(MVVMTK0045)のため手書きプロパティにしている
    private LayoutNodeViewModel _layout;
    public LayoutNodeViewModel Layout
    {
        get => _layout;
        set => SetProperty(ref _layout, value);
    }

    private PaneViewModel _activePane;
    public PaneViewModel ActivePane
    {
        get => _activePane;
        set => SetProperty(ref _activePane, value);
    }

    public MainViewModel(IFileSystemService fs)
    {
        _fs = fs;
        var pane = new PaneViewModel();
        var tab = new TabViewModel(fs);
        pane.Tabs.Add(tab);
        pane.ActiveTab = tab;
        _layout = pane;
        _activePane = pane;
    }

    public async Task InitializeAsync()
    {
        if (ActivePane.ActiveTab is { } tab)
        {
            await tab.NavigateAsync(_fs.HomePath);
        }
    }

    /// <summary>「＋」: アクティブタブと同じパスの新規タブを右隣に開いてアクティブ化する（履歴は引き継がない）。</summary>
    public void DuplicateActiveTab(PaneViewModel pane)
    {
        if (pane.Tabs.Count >= MaxTabsPerPane || pane.ActiveTab is not { } active)
        {
            return;
        }
        var tab = new TabViewModel(_fs);
        pane.Tabs.Insert(pane.Tabs.IndexOf(active) + 1, tab);
        pane.ActiveTab = tab;
        _ = tab.NavigateAsync(active.Path);
    }

    /// <summary>タブを閉じる。アクティブタブなら右隣（なければ左隣）へ。最後のタブならホームを開き直す（ペインが 1 つの間）。</summary>
    public void CloseTab(PaneViewModel pane, TabViewModel tab)
    {
        var index = pane.Tabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }
        var wasActive = pane.ActiveTab == tab;
        pane.Tabs.RemoveAt(index);

        if (pane.Tabs.Count == 0)
        {
            // pane-split 実装まではペインは常に 1 つ = 「最後のペイン」規則を適用
            var home = new TabViewModel(_fs);
            pane.Tabs.Add(home);
            pane.ActiveTab = home;
            _ = home.NavigateAsync(_fs.HomePath);
            return;
        }
        if (wasActive)
        {
            pane.ActiveTab = pane.Tabs[Math.Min(index, pane.Tabs.Count - 1)];
        }
    }

    public const int MaxTabsPerPane = 50;
}
