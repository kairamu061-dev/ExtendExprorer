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

    /// <summary>タブを閉じる。アクティブタブなら右隣（なければ左隣）へ。最後のタブはペインを閉じる（最後のペインならホームを開き直す）。</summary>
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
            if (FindParent(Layout, pane) is null)
            {
                // 最後のペイン: ホームのタブを開き直す
                var home = new TabViewModel(_fs);
                pane.Tabs.Add(home);
                pane.ActiveTab = home;
                _ = home.NavigateAsync(_fs.HomePath);
            }
            else
            {
                ClosePane(pane);
            }
            return;
        }
        if (wasActive)
        {
            pane.ActiveTab = pane.Tabs[Math.Min(index, pane.Tabs.Count - 1)];
        }
    }

    /// <summary>レイアウト木の構造変更（分割・ペインクローズ）を LayoutHost に通知する。Ratio 変更では発火しない。</summary>
    public event Action? LayoutChanged;

    public void ActivatePane(PaneViewModel pane) => ActivePane = pane;

    /// <summary>アクティブペインを 2 分割し、同じパスのタブを 1 つ持つ新ペインを右/下に作ってアクティブ化する。</summary>
    public void SplitPane(PaneViewModel pane, SplitDirection direction)
    {
        if (CountPanes(Layout) >= MaxPanes || pane.ActiveTab is not { } active)
        {
            return;
        }
        var newTab = new TabViewModel(_fs);
        var newPane = new PaneViewModel();
        newPane.Tabs.Add(newTab);
        newPane.ActiveTab = newTab;

        var split = new SplitNodeViewModel { Direction = direction, First = pane, Second = newPane };
        ReplaceNode(pane, split);
        ActivePane = newPane;
        LayoutChanged?.Invoke();
        _ = newTab.NavigateAsync(active.Path);
    }

    /// <summary>ペインを閉じて分割を解消し、兄弟ノードが領域を埋める。最後の 1 ペインは対象外。</summary>
    public void ClosePane(PaneViewModel pane)
    {
        var parent = FindParent(Layout, pane);
        if (parent is null)
        {
            return;
        }
        var sibling = ReferenceEquals(parent.First, pane) ? parent.Second : parent.First;
        ReplaceNode(parent, sibling);
        if (ReferenceEquals(ActivePane, pane))
        {
            ActivePane = FirstPane(sibling);
        }
        LayoutChanged?.Invoke();
    }

    private void ReplaceNode(LayoutNodeViewModel oldNode, LayoutNodeViewModel newNode)
    {
        if (ReferenceEquals(Layout, oldNode))
        {
            Layout = newNode;
            return;
        }
        var parent = FindParent(Layout, oldNode);
        if (parent is null)
        {
            return;
        }
        if (ReferenceEquals(parent.First, oldNode))
        {
            parent.First = newNode;
        }
        else
        {
            parent.Second = newNode;
        }
    }

    private static SplitNodeViewModel? FindParent(LayoutNodeViewModel root, LayoutNodeViewModel child)
    {
        if (root is not SplitNodeViewModel split)
        {
            return null;
        }
        if (ReferenceEquals(split.First, child) || ReferenceEquals(split.Second, child))
        {
            return split;
        }
        return FindParent(split.First, child) ?? FindParent(split.Second, child);
    }

    private static int CountPanes(LayoutNodeViewModel node) => node switch
    {
        SplitNodeViewModel split => CountPanes(split.First) + CountPanes(split.Second),
        _ => 1,
    };

    private static PaneViewModel FirstPane(LayoutNodeViewModel node) => node switch
    {
        SplitNodeViewModel split => FirstPane(split.First),
        PaneViewModel pane => pane,
        _ => throw new InvalidOperationException($"unknown layout node: {node.GetType()}"),
    };

    public const int MaxTabsPerPane = 50;
    public const int MaxPanes = 8;
}
