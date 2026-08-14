using ExtendExprorer.Models;
using ExtendExprorer.Services;
using ExtendExprorer.UI;

namespace ExtendExprorer.ViewModels;

public enum SortColumn { Name, Modified, Type, Size }

/// <summary>一覧 1 つ分の状態（表示中のパス・履歴・並び順・項目）。
/// 現行 WinUI 版の <c>TabViewModel</c> の移植だが、XAML への依存を外してある。
///
/// <para><b>通知は素の C# イベント</b>。バインドが無いので、ビュー側はイベントを受けて
/// <c>LVM_SETITEMCOUNT</c> や再描画を出す。<c>ObservableCollection</c> も使わない
/// （オーナーデータの一覧はコレクションを見に来ないため）。</para>
///
/// <para><b>ディスクに触る処理は必ずワーカースレッド</b>で行い、結果は
/// <see cref="UiDispatcher"/> 経由で UI スレッドへ戻す。素の Win32 には同期コンテキストが
/// 無いので <c>await</c> の続きはスレッドプールで走ってしまう。</para></summary>
internal sealed class FileListViewModel : IDisposable
{
    private readonly IFileSystemService _fs;
    private readonly List<string> _history = [];
    private int _historyIndex = -1;
    private readonly List<Entry> _entries = [];

    /// <summary>読込の世代。移動を連打したときに、古い読込の結果を捨てるために使う。</summary>
    private int _loadToken;

    private FileSystemWatcher? _watcher;
    private readonly System.Threading.Timer _refreshTimer;
    private bool _refreshScheduled;
    private bool _disposed;

    internal string Path { get; private set; } = "";
    internal string? ErrorMessage { get; private set; }
    internal SortColumn SortColumn { get; private set; } = SortColumn.Name;
    internal bool SortAscending { get; private set; } = true;

    internal IReadOnlyList<Entry> Entries => _entries;

    internal bool CanGoBack => _historyIndex > 0;
    internal bool CanGoForward => _historyIndex >= 0 && _historyIndex < _history.Count - 1;
    internal bool CanGoUp => System.IO.Path.GetDirectoryName(Path) is not null;

    /// <summary>一覧が全面的に入れ替わった（移動・再読込・並べ替え）。</summary>
    internal event Action? EntriesReset;

    /// <summary>末尾に 1 件増えた。既存の行番号は動かないので、選択は付け直さなくてよい。</summary>
    internal event Action<int>? EntryAdded;

    /// <summary>1 件消えた（その行以降の行番号がずれる）。</summary>
    internal event Action<int>? EntryRemoved;

    /// <summary>1 件だけ内容が変わった（リネーム。位置は動かない）。</summary>
    internal event Action<int>? EntryUpdated;

    /// <summary>パス・エラー・履歴の可否が変わった。</summary>
    internal event Action? StateChanged;

    internal FileListViewModel(IFileSystemService fs)
    {
        _fs = fs;
        // 取りこぼし時の全体読み直しのデバウンス（WinUI 版の DispatcherQueueTimer にあたる）
        _refreshTimer = new System.Threading.Timer(_ => UiDispatcher.Post(RunPendingRefresh),
            null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
    }

    // --- 移動 ---

    /// <summary>履歴に積んで移動する（ダブルクリック・上へ・アドレスバー）。</summary>
    internal void Navigate(string newPath)
    {
        if (_historyIndex < _history.Count - 1)
        {
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        }
        _history.Add(newPath);
        _historyIndex = _history.Count - 1;
        Load(newPath);
    }

    internal void GoBack()
    {
        if (CanGoBack)
        {
            _historyIndex--;
            Load(_history[_historyIndex]);
        }
    }

    internal void GoForward()
    {
        if (CanGoForward)
        {
            _historyIndex++;
            Load(_history[_historyIndex]);
        }
    }

    internal void GoUp()
    {
        if (System.IO.Path.GetDirectoryName(Path) is { } parent)
        {
            Navigate(parent);
        }
    }

    /// <summary>表示中フォルダの再読込。並び順は保つ。</summary>
    internal void Refresh() => Load(Path, resetSort: false);

    private void Load(string targetPath, bool resetSort = true)
    {
        if (_disposed || string.IsNullOrEmpty(targetPath))
        {
            return;
        }
        Path = targetPath;
        ErrorMessage = null;
        if (resetSort)
        {
            // 並び順はフォルダ単位（移動したら既定の名前昇順に戻す）
            SortColumn = SortColumn.Name;
            SortAscending = true;
        }
        var token = ++_loadToken;
        StateChanged?.Invoke();

        _ = LoadCoreAsync(targetPath, token);
    }

    private async Task LoadCoreAsync(string targetPath, int token)
    {
        try
        {
            var result = await _fs.ListAsync(targetPath).ConfigureAwait(false);
            UiDispatcher.Post(() => Apply(targetPath, token, result));
        }
        catch (Exception ex)
        {
            Diagnostics.Report($"LoadCoreAsync({targetPath})", ex);
        }
    }

    private void Apply(string targetPath, int token, ListResult result)
    {
        // 読み込んでいる間に別のフォルダへ移っていたら捨てる
        if (_disposed || token != _loadToken)
        {
            return;
        }
        _entries.Clear();
        switch (result)
        {
            case ListOk ok:
                _entries.AddRange(ok.Entries);
                Sort();
                StartWatching(targetPath);
                break;
            case ListError err:
                StopWatching();
                ErrorMessage = err.Kind switch
                {
                    ListErrorKind.AccessDenied => "アクセスが拒否されました",
                    ListErrorKind.NotFound => "パスが見つかりません",
                    _ => $"読み込みに失敗しました: {err.Message}",
                };
                break;
        }
        EntriesReset?.Invoke();
        StateChanged?.Invoke();
    }

    // --- 並べ替え ---

    internal void SetSort(SortColumn column)
    {
        if (SortColumn == column)
        {
            SortAscending = !SortAscending;
        }
        else
        {
            SortColumn = column;
            SortAscending = true;
        }
        Sort();
        EntriesReset?.Invoke();
        StateChanged?.Invoke();
    }

    private void Sort()
    {
        var column = SortColumn;
        var ascending = SortAscending;
        _entries.Sort((a, b) =>
        {
            // フォルダ先頭は並び順によらず維持する（file-list 仕様）
            if (a.IsDirectory != b.IsDirectory)
            {
                return a.IsDirectory ? -1 : 1;
            }
            var result = column switch
            {
                SortColumn.Modified => a.Modified.CompareTo(b.Modified),
                SortColumn.Type => NaturalStringComparer.Instance.Compare(
                    EntryFormat.TypeLabel(a), EntryFormat.TypeLabel(b)),
                SortColumn.Size => a.Size.CompareTo(b.Size),
                _ => 0,
            };
            if (!ascending)
            {
                result = -result;
            }
            // 名前は自然順（`File2` < `File10`）。値が同じときの決着にも使う
            // （List.Sort は安定ではないので、決着を付けないと同値の行の順が読込ごとに変わる）
            return result != 0 ? result : NaturalStringComparer.Instance.Compare(a.Name, b.Name);
        });
    }

    // --- 表示中フォルダの追随（差分更新） ---
    //
    // 一覧全体を読み直すと並べ替えが走り、追加やリネームのたびに行が動いて落ち着かない
    // （2026-08-04 ユーザ要望）。エクスプローラー系のツールと同じく、
    //   * 追加   → 末尾に足すだけ（並べ替えない）
    //   * 削除   → その行を消すだけ
    //   * リネーム → 同じ位置のまま名前を差し替える
    // とし、全体の読み直しは「移動・明示的な再読込・通知の取りこぼし」だけに限定する。
    // 内容の変化（サイズ・更新日時）は監視対象から外す。ホームのように常時書き込みが
    // 起きるフォルダで、勝手に更新がかかり続けるのを防ぐため。

    private void StartWatching(string path)
    {
        if (_disposed || string.IsNullOrEmpty(path))
        {
            StopWatching();
            return;
        }
        // 再読込のたびに張り替えると、その隙間の変更を取りこぼす。同じフォルダなら使い回す
        if (_watcher is { } current && string.Equals(current.Path, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        StopWatching();
        try
        {
            var watcher = new FileSystemWatcher(path)
            {
                // 項目の増減・改名のみ。サイズ・更新日時の変化では動かさない
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                IncludeSubdirectories = false,
            };
            // 通知はワーカースレッドで届く。項目情報の取得（ディスクアクセス）はここで済ませ、
            // UI スレッドでは一覧の書き換えだけを行う。
            // e.Name は理屈上 null になりうる（パスが長すぎる等）。名前が分からない通知は
            // 個別には扱えないので、全体の読み直しに倒す
            watcher.Created += (_, e) =>
            {
                if (e.Name is not { } name)
                {
                    UiDispatcher.Post(ScheduleFullRefresh);
                    return;
                }
                var entry = ReadEntry(path, name);
                UiDispatcher.Post(() => AddEntry(path, entry));
            };
            watcher.Deleted += (_, e) =>
            {
                if (e.Name is not { } name)
                {
                    UiDispatcher.Post(ScheduleFullRefresh);
                    return;
                }
                UiDispatcher.Post(() => RemoveEntry(path, name));
            };
            watcher.Renamed += (_, e) =>
            {
                if (e.Name is not { } name || e.OldName is not { } oldName)
                {
                    UiDispatcher.Post(ScheduleFullRefresh);
                    return;
                }
                var entry = ReadEntry(path, name);
                UiDispatcher.Post(() => RenameEntry(path, oldName, entry));
            };
            // バッファ溢れ等で個別通知を落としたときは、まとめて読み直せば整合する
            watcher.Error += (_, _) => UiDispatcher.Post(ScheduleFullRefresh);
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch
        {
            // 監視できないパス（ネットワーク・権限なし・直後に消えた等）は自動更新なしで続行する
            _watcher = null;
        }
    }

    /// <summary>フォルダ監視が動いているか。監視できないパスでは、操作後に自分で読み直す必要がある。</summary>
    internal bool IsWatching => _watcher is not null;

    private void StopWatching()
    {
        if (_watcher is { } watcher)
        {
            _watcher = null;
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            catch
            {
                // 破棄時の失敗は無視（監視は既に止まっている）
            }
        }
    }

    /// <summary>通知が届いた時点と、それを反映する時点で表示フォルダが変わっていないか。</summary>
    private bool StillShowing(string folder) =>
        !_disposed && string.Equals(folder, Path, StringComparison.OrdinalIgnoreCase);

    private void AddEntry(string folder, Entry? entry)
    {
        if (entry is null || !StillShowing(folder) || IndexOf(entry.Name) >= 0)
        {
            return;
        }
        _entries.Add(entry);
        EntryAdded?.Invoke(_entries.Count - 1);
    }

    private void RemoveEntry(string folder, string name)
    {
        if (!StillShowing(folder))
        {
            return;
        }
        var index = IndexOf(name);
        if (index < 0)
        {
            return;
        }
        _entries.RemoveAt(index);
        EntryRemoved?.Invoke(index);
    }

    /// <summary>リネームされた項目を同じ位置のまま差し替える（行を動かさない）。</summary>
    private void RenameEntry(string folder, string oldName, Entry? entry)
    {
        if (!StillShowing(folder))
        {
            return;
        }
        var index = IndexOf(oldName);
        if (index < 0)
        {
            AddEntry(folder, entry); // 元が一覧に無いなら追加として扱う
            return;
        }
        if (entry is null)
        {
            RemoveEntry(folder, oldName); // 改名先が読めない（移動された等）
            return;
        }
        _entries[index] = entry;
        EntryUpdated?.Invoke(index);
    }

    private int IndexOf(string name)
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            if (string.Equals(_entries[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>1 項目分の情報を取り直す。取れなければ null（消えた・アクセス不可）。
    /// 監視スレッドから呼ぶ（ディスクアクセスを UI スレッドに持ち込まない）。</summary>
    private static Entry? ReadEntry(string folder, string name)
    {
        try
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }
            var full = System.IO.Path.Combine(folder, name);
            if (Directory.Exists(full))
            {
                var info = new DirectoryInfo(full);
                return new Entry(info.Name, true, 0L, info.LastWriteTime, IsHiddenOrSystem(info.Attributes));
            }
            if (File.Exists(full))
            {
                var info = new FileInfo(full);
                return new Entry(info.Name, false, info.Length, info.LastWriteTime, IsHiddenOrSystem(info.Attributes));
            }
        }
        catch
        {
            // 取得できないものは一覧に出さない
        }
        return null;
    }

    private static bool IsHiddenOrSystem(FileAttributes attributes) =>
        (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;

    /// <summary>取りこぼし時などの全体読み直し（デバウンス付き）。UI スレッドから呼ぶ。</summary>
    private void ScheduleFullRefresh()
    {
        if (_disposed || _refreshScheduled)
        {
            return;
        }
        _refreshScheduled = true;
        _refreshTimer.Change(400, System.Threading.Timeout.Infinite);
    }

    private void RunPendingRefresh()
    {
        _refreshScheduled = false;
        if (!_disposed && !string.IsNullOrEmpty(Path))
        {
            Refresh();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _refreshTimer.Dispose();
        StopWatching();
    }
}
