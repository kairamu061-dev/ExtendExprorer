using ExtendExprorer.Services;

namespace ExtendExprorer.ViewModels;

/// <summary>ペイン 1 つ分。タブの束と、<b>1 つだけ</b>の一覧を持つ。
///
/// <para>一覧をタブごとに持たないのがこの設計の要点。タブを切り替えるときに
/// 状態（パス・履歴・並び順）を載せ替えるだけなので、タブが何枚あっても
/// 項目の配列は 1 枚ぶんしか存在しない。</para></summary>
internal sealed class PaneModel : IDisposable
{
    private readonly List<TabModel> _tabs = [];

    internal FileListViewModel FileList { get; }

    internal IReadOnlyList<TabModel> Tabs => _tabs;

    internal int ActiveIndex { get; private set; } = -1;

    internal TabModel? ActiveTab =>
        (uint)ActiveIndex < (uint)_tabs.Count ? _tabs[ActiveIndex] : null;

    /// <summary>タブの数・並び・見出し・選択が変わった（帯を描き直す合図）。</summary>
    internal event Action? TabsChanged;

    internal PaneModel(IFileSystemService fs)
    {
        FileList = new FileListViewModel(fs);
        // 表示中フォルダが変わったら見出しも変わる
        FileList.StateChanged += OnFileListStateChanged;
    }

    private void OnFileListStateChanged()
    {
        if (ActiveTab is { } tab && !string.Equals(tab.Path, FileList.Path, StringComparison.OrdinalIgnoreCase))
        {
            tab.Path = FileList.Path;
            TabsChanged?.Invoke();
        }
    }

    /// <summary>タブを増やす。<paramref name="activate"/> が false なら裏で開くだけ
    /// （フォルダ列挙は走らない＝増やすコストがほぼ無い）。</summary>
    internal TabModel AddTab(string path, bool activate = true)
    {
        var tab = new TabModel { Path = path };
        tab.History.Add(path);
        tab.HistoryIndex = 0;
        _tabs.Add(tab);
        if (activate || ActiveIndex < 0)
        {
            Activate(_tabs.Count - 1);
        }
        else
        {
            TabsChanged?.Invoke();
        }
        return tab;
    }

    internal void Activate(int index)
    {
        if ((uint)index >= (uint)_tabs.Count || index == ActiveIndex)
        {
            return;
        }
        ActiveTab?.Let(FileList.SaveTo);
        ActiveIndex = index;
        FileList.SwitchTo(_tabs[index]);
        TabsChanged?.Invoke();
    }

    /// <summary>タブを閉じる。<b>最後の 1 枚は閉じない</b>（ペインが空になると
    /// 何も操作できなくなるため。ペインごと閉じるのは第 2 段の分割で扱う）。</summary>
    internal void CloseTab(int index)
    {
        if ((uint)index >= (uint)_tabs.Count || _tabs.Count <= 1)
        {
            return;
        }
        var wasActive = index == ActiveIndex;
        _tabs.RemoveAt(index);

        if (wasActive)
        {
            // 閉じた位置の 1 つ前（先頭を閉じたら新しい先頭）へ移る
            var next = Math.Clamp(index - 1, 0, _tabs.Count - 1);
            ActiveIndex = -1;
            Activate(next);
            return;
        }
        if (index < ActiveIndex)
        {
            ActiveIndex--;
        }
        TabsChanged?.Invoke();
    }

    /// <summary>指定したタブ以外を閉じる。</summary>
    internal void CloseOthers(int index)
    {
        if ((uint)index >= (uint)_tabs.Count)
        {
            return;
        }
        var keep = _tabs[index];
        _tabs.RemoveAll(t => !ReferenceEquals(t, keep));
        ActiveIndex = -1;
        Activate(0);
    }

    /// <summary>指定したタブより右を閉じる。</summary>
    internal void CloseToTheRight(int index)
    {
        if ((uint)index >= (uint)_tabs.Count || index == _tabs.Count - 1)
        {
            return;
        }
        _tabs.RemoveRange(index + 1, _tabs.Count - index - 1);
        if (ActiveIndex > index)
        {
            ActiveIndex = -1;
            Activate(index);
            return;
        }
        TabsChanged?.Invoke();
    }

    public void Dispose() => FileList.Dispose();
}

internal static class TabModelExtensions
{
    /// <summary>null でなければ渡す（<c>ActiveTab?.Let(...)</c> と書くため）。</summary>
    internal static void Let(this TabModel tab, Action<TabModel> action) => action(tab);
}
