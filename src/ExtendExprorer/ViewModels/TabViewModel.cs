using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ExtendExprorer.Models;
using ExtendExprorer.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ExtendExprorer.ViewModels;

public enum SortColumn { Name, Modified, Type, Size }

public partial class TabViewModel : ObservableObject, IDisposable
{
    private readonly IFileSystemService _fs;
    private readonly List<string> _history = new();
    private int _historyIndex = -1;
    private List<EntryViewModel> _allEntries = new();

    // 表示中フォルダの変更を監視して一覧へ反映する(BUG-008)。
    // シェル操作(D&D・貼り付け・削除)は非同期に完了し、移動元ペインには通知すら来ないため、
    // 明示的な RefreshAsync では取りこぼす。反映は差分で行う（下の「差分更新」を参照）。
    private readonly DispatcherQueue? _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly DispatcherQueueTimer? _refreshTimer;
    private FileSystemWatcher? _watcher;
    private int _autoRefreshSuspendCount;
    private bool _refreshPending;
    private readonly List<Action> _deferred = new();
    private const int MaxDeferredChanges = 200;
    private bool _disposed;

    // [ObservableProperty] は AOT 非対応(MVVMTK0045)のため手書きプロパティにしている
    private string _path = "";
    public string Path
    {
        get => _path;
        private set
        {
            if (SetProperty(ref _path, value))
            {
                OnPropertyChanged(nameof(Title));
                // フォルダが変わったらアイコンも取り直す（次に Icon が読まれた時点で開始）
                _icon = null;
                _iconRequested = false;
                OnPropertyChanged(nameof(Icon));
                OnPropertyChanged(nameof(FallbackIconVisibility));
            }
        }
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    private SortColumn _sortColumn = SortColumn.Name;
    public SortColumn SortColumn
    {
        get => _sortColumn;
        private set => SetProperty(ref _sortColumn, value);
    }

    private bool _sortAscending = true;
    public bool SortAscending
    {
        get => _sortAscending;
        private set => SetProperty(ref _sortAscending, value);
    }

    public ObservableCollection<EntryViewModel> Entries { get; } = new();

    // タブ見出しのアイコン。file-list / folder-tree と同じシェルアイコンを使う（遅延読込）。
    // ドライブルートは固有アイコンを持つのでパス単位で引く
    private ImageSource? _icon;
    private bool _iconRequested;
    public ImageSource? Icon
    {
        get
        {
            if (!_iconRequested && !string.IsNullOrEmpty(Path))
            {
                _iconRequested = true;
                _ = LoadIconAsync(Path);
            }
            return _icon;
        }
    }

    /// <summary>シェルアイコンが解決するまでは従来グリフを見せる（一覧・ツリーと同じ方式）。</summary>
    public Visibility FallbackIconVisibility => _icon is null ? Visibility.Visible : Visibility.Collapsed;

    private async Task LoadIconAsync(string path)
    {
        var isRoot = string.Equals(System.IO.Path.GetPathRoot(path), path, StringComparison.OrdinalIgnoreCase);
        var icon = await ShellIconCache.GetAsync(path, isDirectory: true, distinctByPath: isRoot);
        // 読込中に別のフォルダへ移っていたら捨てる
        if (icon is not null && string.Equals(path, Path, StringComparison.OrdinalIgnoreCase))
        {
            _icon = icon;
            OnPropertyChanged(nameof(Icon));
            OnPropertyChanged(nameof(FallbackIconVisibility));
        }
    }

    /// <summary>読み込み（移動・再読込）完了時に発火。ビュー側の状態リセットに使う。</summary>
    public event Action? Navigated;

    /// <summary>表示フォルダが実際に変わったときだけ発火。セッションの自動保存トリガー。
    /// 自動再読込(BUG-008)でも <see cref="Navigated"/> は毎回発火するため、保存の起点は分けている
    /// （%LOCALAPPDATA% を表示していると session.json 保存 → 変更通知 → 保存…と回り続けるのを防ぐ）。</summary>
    public event Action? PathChanged;

    public bool CanGoBack => _historyIndex > 0;
    public bool CanGoForward => _historyIndex >= 0 && _historyIndex < _history.Count - 1;
    public bool CanGoUp => System.IO.Path.GetDirectoryName(Path) is not null;

    /// <summary>タブ見出し。フォルダ名（ドライブルートはパスそのもの）。</summary>
    public string Title
    {
        get
        {
            if (string.IsNullOrEmpty(Path))
            {
                return "新しいタブ";
            }
            var name = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(Path));
            return string.IsNullOrEmpty(name) ? Path : name;
        }
    }

    public TabViewModel(IFileSystemService fs)
    {
        _fs = fs;
        if (_dispatcher is not null)
        {
            _refreshTimer = _dispatcher.CreateTimer();
            _refreshTimer.Interval = TimeSpan.FromMilliseconds(400);
            _refreshTimer.IsRepeating = false;
            _refreshTimer.Tick += (_, _) => RunPendingRefresh();
        }
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

    /// <summary>インライン リネーム編集中など、一覧に触られると困る間だけ追随を止める。
    /// 抑制中に届いた変更は順番どおり貯めておき、解除時にそのまま適用する
    /// （読み直すと並べ替えが走って行が動いてしまうため）。</summary>
    public void SuspendAutoRefresh() => _autoRefreshSuspendCount++;

    public void ResumeAutoRefresh()
    {
        if (_autoRefreshSuspendCount == 0 || --_autoRefreshSuspendCount > 0)
        {
            return;
        }
        if (_refreshPending)
        {
            _refreshPending = false;
            _deferred.Clear();
            ScheduleFullRefresh();
            return;
        }
        var pending = _deferred.ToArray();
        _deferred.Clear();
        foreach (var action in pending)
        {
            action();
        }
    }

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
            watcher.Created += (_, e) => Post(() => AddEntry(e.Name));
            watcher.Deleted += (_, e) => Post(() => RemoveEntry(e.Name));
            watcher.Renamed += (_, e) => Post(() => RenameEntry(e.OldName, e.Name));
            // バッファ溢れ等で個別通知を落としたときは、まとめて読み直せば整合する
            watcher.Error += (_, _) => Post(ScheduleFullRefresh);
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
    public bool IsWatching => _watcher is not null;

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

    /// <summary>監視スレッドから届く通知を UI スレッドへ載せ替える。
    /// 抑制中（リネーム編集中）は個別反映せず、解除時の読み直しに委ねる。</summary>
    private void Post(Action action)
    {
        if (_disposed)
        {
            return;
        }
        _dispatcher?.TryEnqueue(() =>
        {
            if (_disposed)
            {
                return;
            }
            if (_autoRefreshSuspendCount > 0)
            {
                // 貯めすぎるくらいなら読み直した方が安い
                if (_deferred.Count >= MaxDeferredChanges)
                {
                    _refreshPending = true;
                }
                else
                {
                    _deferred.Add(action);
                }
                return;
            }
            action();
        });
    }

    /// <summary>追加された項目を末尾に足す（並べ替えはしない）。</summary>
    private void AddEntry(string name)
    {
        if (string.IsNullOrEmpty(name) || IndexOf(name) >= 0)
        {
            return;
        }
        if (ReadEntry(name) is not { } entry)
        {
            return; // すぐ消えた等
        }
        var vm = new EntryViewModel(entry, Path);
        _allEntries.Add(vm);
        Entries.Add(vm);
    }

    private void RemoveEntry(string name)
    {
        var index = IndexOf(name);
        if (index < 0)
        {
            return;
        }
        var vm = Entries[index];
        Entries.RemoveAt(index);
        _allEntries.Remove(vm);
    }

    /// <summary>リネームされた項目を同じ位置のまま差し替える（行を動かさない）。</summary>
    private void RenameEntry(string oldName, string newName)
    {
        var index = IndexOf(oldName);
        if (index < 0)
        {
            AddEntry(newName); // 元が一覧に無い（フィルタ外など）なら追加として扱う
            return;
        }
        if (ReadEntry(newName) is not { } entry)
        {
            RemoveEntry(oldName);
            return;
        }
        var vm = new EntryViewModel(entry, Path);
        var oldVm = Entries[index];
        var sourceIndex = _allEntries.IndexOf(oldVm);
        if (sourceIndex >= 0)
        {
            _allEntries[sourceIndex] = vm;
        }
        Entries[index] = vm;
    }

    private int IndexOf(string name)
    {
        for (var i = 0; i < Entries.Count; i++)
        {
            if (string.Equals(Entries[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>1 項目分の情報を取り直す。取れなければ null（消えた・アクセス不可）。</summary>
    private Entry? ReadEntry(string name)
    {
        try
        {
            var full = System.IO.Path.Combine(Path, name);
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

    /// <summary>取りこぼし時などの全体読み直し（デバウンス付き）。</summary>
    private void ScheduleFullRefresh()
    {
        if (_disposed || _refreshTimer is null)
        {
            return;
        }
        if (!_refreshTimer.IsRunning)
        {
            _refreshTimer.Start();
        }
    }

    private void RunPendingRefresh()
    {
        if (_disposed || string.IsNullOrEmpty(Path))
        {
            return;
        }
        if (IsLoading)
        {
            _refreshTimer?.Start(); // 読込中はもう一度待ってから
            return;
        }
        _ = RefreshAsync();
    }

    /// <summary>タブを閉じるときに呼ぶ（監視スレッド・ハンドルを解放する）。</summary>
    public void Dispose()
    {
        _disposed = true;
        _refreshTimer?.Stop();
        StopWatching();
    }

    /// <summary>履歴に追加して移動する（ダブルクリック・アドレスバー・上へ）。</summary>
    public async Task NavigateAsync(string newPath)
    {
        if (_historyIndex < _history.Count - 1)
        {
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        }
        _history.Add(newPath);
        _historyIndex = _history.Count - 1;
        await LoadAsync(newPath);
    }

    public async Task GoBackAsync()
    {
        if (!CanGoBack)
        {
            return;
        }
        _historyIndex--;
        await LoadAsync(_history[_historyIndex]);
    }

    public async Task GoForwardAsync()
    {
        if (!CanGoForward)
        {
            return;
        }
        _historyIndex++;
        await LoadAsync(_history[_historyIndex]);
    }

    public async Task GoUpAsync()
    {
        if (System.IO.Path.GetDirectoryName(Path) is { } parent)
        {
            await NavigateAsync(parent);
        }
    }

    /// <summary>アドレスバー入力で移動する。存在すれば移動して true、無効なら移動せず false（address-bar）。</summary>
    public async Task<bool> TryNavigateAsync(string input)
    {
        var target = await _fs.ResolveNavigationTargetAsync(input);
        if (target is null)
        {
            return false;
        }
        await NavigateAsync(target);
        return true;
    }

    public void SetSort(SortColumn column)
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
        ApplySort();
    }

    /// <summary>表示中フォルダの再読込（リネーム・貼り付け等の後）。ソート状態は保持する。</summary>
    public Task RefreshAsync() => LoadAsync(Path, resetSort: false);

    private async Task LoadAsync(string targetPath, bool resetSort = true)
    {
        var samePath = string.Equals(Path, targetPath, StringComparison.OrdinalIgnoreCase);
        Path = targetPath;
        IsLoading = true;
        ErrorMessage = null;
        if (resetSort)
        {
            // ソート状態はフォルダ単位（移動したら既定の名前昇順に戻す）
            SortColumn = SortColumn.Name;
            SortAscending = true;
        }
        var result = await _fs.ListAsync(targetPath);

        switch (result)
        {
            case ListOk ok:
                _allEntries = BuildEntries(ok.Entries, targetPath, reuse: samePath);
                ApplySort();
                StartWatching(targetPath);
                break;
            case ListError err:
                _allEntries = new();
                Entries.Clear();
                StopWatching();
                ErrorMessage = err.Kind switch
                {
                    ListErrorKind.AccessDenied => "アクセスが拒否されました",
                    ListErrorKind.NotFound => "パスが見つかりません",
                    _ => $"読み込みに失敗しました: {err.Message}",
                };
                break;
        }
        // Entries の入れ替え中は IsLoading を立てたままにする。
        // ビュー側はこれを見て「再読込に伴う一時的な選択解除」を無視できる（自動再読込での選択維持）
        IsLoading = false;

        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoUp));
        if (!samePath)
        {
            PathChanged?.Invoke();
        }
        Navigated?.Invoke();
    }

    /// <summary>一覧の ViewModel を作る。再読込（同じフォルダ）では内容が変わっていない項目の
    /// インスタンスを使い回し、ListView の選択（＝オブジェクト同一性で保持される）が
    /// 自動再読込のたびに外れないようにする。</summary>
    private List<EntryViewModel> BuildEntries(IReadOnlyList<Entry> entries, string folderPath, bool reuse)
    {
        if (!reuse || _allEntries.Count == 0)
        {
            return entries.Select(e => new EntryViewModel(e, folderPath)).ToList();
        }
        var previous = new Dictionary<string, EntryViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var vm in _allEntries)
        {
            previous[vm.Name] = vm;
        }
        // Entry は record なので、サイズ・更新日時まで含めて等しいときだけ使い回す（表示のずれを防ぐ）
        return entries
            .Select(e => previous.TryGetValue(e.Name, out var old) && old.Model == e
                ? old
                : new EntryViewModel(e, folderPath))
            .ToList();
    }

    private void ApplySort()
    {
        // 名前は自然順（`File2` < `File10`）。エクスプローラーと並び順を揃える
        IEnumerable<EntryViewModel> sorted = SortColumn switch
        {
            SortColumn.Name => _allEntries.OrderBy(e => e.Name, NaturalStringComparer.Instance),
            SortColumn.Modified => _allEntries.OrderBy(e => e.Model.Modified),
            SortColumn.Type => _allEntries.OrderBy(e => e.TypeLabel, NaturalStringComparer.Instance),
            SortColumn.Size => _allEntries.OrderBy(e => e.Model.Size),
            _ => _allEntries,
        };
        if (!SortAscending)
        {
            sorted = sorted.Reverse();
        }
        // OrderBy は安定ソートなので、最後に適用するフォルダ優先が列ソート順を保つ
        sorted = sorted.OrderByDescending(e => e.IsDirectory);

        Entries.Clear();
        foreach (var entry in sorted)
        {
            Entries.Add(entry);
        }
    }
}
