using System.ComponentModel;
using ExtendExprorer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace ExtendExprorer.Views;

public sealed partial class FileListView : UserControl
{
    private TabViewModel? _viewModel;

    // インライン リネーム: 選択済み単一項目をもう一度クリックすると開始する(エクスプローラーと同じ)。
    // ダブルクリック(開く)と区別するため、OS のダブルクリック間隔だけ待ってから編集に入る。
    // _renameCandidate = 直前のタップ完了時点で「単独選択されていた項目」。次のタップが
    // 同じ項目なら＝2回目クリックとみなしてリネーム待機に入る（PointerPressed の選択タイミングに依存しない）。
    private readonly DispatcherTimer _renameTimer = new();
    private EntryViewModel? _renameCandidate;
    private EntryViewModel? _pendingRename;
    private EntryViewModel? _renamingEntry;
    private TextBox? _renameBox;

    // 自動再読込(BUG-008)で Entries を作り直しても選択が外れないよう、名前で覚えて復元する
    private readonly HashSet<string> _selectedNames = new(StringComparer.OrdinalIgnoreCase);
    private string _lastLoadedPath = "";
    private bool _restoringSelection;

    public TabViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                _viewModel.Navigated -= OnNavigated;
            }
            ResetRenameState();
            // 選択の記憶はタブ単位。切り替えたら持ち越さない
            _selectedNames.Clear();
            _lastLoadedPath = value?.Path ?? "";
            _viewModel = value;
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
                // 同一タブ内の移動では ViewModel は入れ替わらない。移動のたびに Entries が作り直され
                // 編集中の項目は消えるため、リネーム状態はここで確実に落とす(BUG-007)
                _viewModel.Navigated += OnNavigated;
                List.ItemsSource = _viewModel.Entries;
            }
            else
            {
                List.ItemsSource = null;
            }
            UpdateState();
        }
    }

    public FileListView()
    {
        InitializeComponent();
        _renameTimer.Interval = TimeSpan.FromMilliseconds(Interop.NativeMethods.GetDoubleClickTime() + 100);
        _renameTimer.Tick += OnRenameTimerTick;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateState();

    private void UpdateState()
    {
        var error = _viewModel?.ErrorMessage;
        ErrorText.Text = error ?? "";
        ErrorText.Visibility = string.IsNullOrEmpty(error) ? Visibility.Collapsed : Visibility.Visible;
        List.Visibility = string.IsNullOrEmpty(error) ? Visibility.Visible : Visibility.Collapsed;
        LoadingRing.IsActive = _viewModel?.IsLoading == true;
    }

    // ---- インライン リネーム ----

    private void OnNavigated()
    {
        ResetRenameState();
        // 同じフォルダの再読込（自動再読込を含む）なら選択を復元する。別フォルダへ移ったら捨てる
        var path = _viewModel?.Path ?? "";
        if (string.Equals(path, _lastLoadedPath, StringComparison.OrdinalIgnoreCase) && _selectedNames.Count > 0)
        {
            RestoreSelection();
        }
        else
        {
            _selectedNames.Clear();
        }
        _lastLoadedPath = path;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 再読込で Entries を作り直している最中の解除・復元は「ユーザーの選択」ではない
        if (_restoringSelection || _viewModel?.IsLoading == true)
        {
            return;
        }
        _selectedNames.Clear();
        foreach (var entry in List.SelectedItems.OfType<EntryViewModel>())
        {
            _selectedNames.Add(entry.Name);
        }
    }

    private void RestoreSelection()
    {
        if (_viewModel is null)
        {
            return;
        }
        _restoringSelection = true;
        try
        {
            foreach (var entry in _viewModel.Entries)
            {
                if (_selectedNames.Contains(entry.Name) && !List.SelectedItems.Contains(entry))
                {
                    List.SelectedItems.Add(entry);
                }
            }
        }
        finally
        {
            _restoringSelection = false;
        }
    }

    private void OnListPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // 新しいクリック操作が始まったら保留中のリネーム待機は取り消す（別項目・空白へ移った等）
        CancelPendingRename();
        // 編集ボックスの外（別の項目・一覧の空白）をクリックしたら、その場で確定する。
        // TextBox 内のクリックはこのハンドラまで届かない（TextBox がポインタ入力を処理する）
        if (_renamingEntry is not null && _renameBox is { } box)
        {
            CommitRename(box, cancel: false);
        }
    }

    private void OnItemTapped(object sender, TappedRoutedEventArgs e)
    {
        if (_renamingEntry is not null)
        {
            return; // 編集中は無視
        }
        var entry = (e.OriginalSource as FrameworkElement)?.DataContext as EntryViewModel;
        var soleSelected = List.SelectedItems.Count == 1 ? List.SelectedItem as EntryViewModel : null;

        // このタップの「前」から単独選択されていた項目を再クリック＝2回目クリック → 編集待機に入る。
        // ダブルクリック(開く)なら待機満了前に DoubleTapped が発火して取り消される。
        if (entry is not null && ReferenceEquals(entry, _renameCandidate) && ReferenceEquals(entry, soleSelected))
        {
            _pendingRename = entry;
            _renameTimer.Start();
        }

        // 次タップの判定用に、今回のタップ完了時点の単独選択項目を候補として覚える
        _renameCandidate = soleSelected;
    }

    private void OnRenameTimerTick(object? sender, object e)
    {
        _renameTimer.Stop();
        if (_pendingRename is { } entry &&
            ReferenceEquals(List.SelectedItem, entry) &&
            List.SelectedItems.Count == 1)
        {
            _renamingEntry = entry;
            // 編集中に一覧が作り直されると編集ボックスごと消えるので、自動再読込を止める(BUG-008 の副作用対策)
            _viewModel?.SuspendAutoRefresh();
            entry.IsRenaming = true; // TextBox が表示され Loaded でフォーカスされる
        }
        _pendingRename = null;
    }

    private void CancelPendingRename()
    {
        _renameTimer.Stop();
        _pendingRename = null;
    }

    /// <summary>リネームに関する状態をすべて初期化する（移動・タブ切替・ViewModel 差し替え時）。
    /// ここを通らずに <see cref="_renamingEntry"/> が残ると、以後のタップがすべて無視される(BUG-007)。</summary>
    private void ResetRenameState()
    {
        CancelPendingRename();
        _renameCandidate = null;
        if (_renamingEntry is { } renaming)
        {
            renaming.IsRenaming = false;
            EndRename();
        }
    }

    /// <summary>編集終了時の共通後始末。どの経路から抜けても状態が残らないようにする。</summary>
    private void EndRename()
    {
        if (_renamingEntry is null)
        {
            return;
        }
        _renamingEntry = null;
        _renameBox = null;
        _viewModel?.ResumeAutoRefresh();
    }

    private void OnRenameBoxLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not EntryViewModel entry || !entry.IsRenaming)
        {
            return;
        }
        _renameBox = box;
        SizeRenameBox(box);
        box.Focus(FocusState.Programmatic);
        // エクスプローラー同様、ファイルは拡張子を除いた部分だけを選択（フォルダは全選択）
        var stem = entry.IsDirectory ? entry.Name.Length : System.IO.Path.GetFileNameWithoutExtension(entry.Name).Length;
        box.Select(0, stem);
    }

    private void OnRenameBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox box)
        {
            SizeRenameBox(box);
        }
    }

    /// <summary>編集ボックスの幅を文字列に合わせる（エクスプローラー同様に名前の長さにフィット）。</summary>
    private void SizeRenameBox(TextBox box)
    {
        var probe = new TextBlock { FontSize = box.FontSize, FontFamily = box.FontFamily, Text = box.Text };
        probe.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        // 文字幅 + 左右パディング/カーソル分の余白。極端に短い/長い名前をクランプ
        box.Width = Math.Clamp(probe.DesiredSize.Width + 20, 48, 560);
    }

    private void OnRenameBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not TextBox box)
        {
            return;
        }
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            CommitRename(box, cancel: false);
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            CommitRename(box, cancel: true);
            e.Handled = true;
        }
    }

    private void OnRenameBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box)
        {
            CommitRename(box, cancel: false);
        }
    }

    private void CommitRename(TextBox box, bool cancel)
    {
        // 編集中の項目は _renamingEntry を正とする。ListView のコンテナ再利用で box.DataContext が
        // 別項目に差し替わっていても、編集状態が残らないよう必ず後始末する(BUG-007)
        var entry = _renamingEntry;
        if (entry is null)
        {
            return;
        }
        entry.IsRenaming = false;
        EndRename();
        var stale = !ReferenceEquals(box.DataContext, entry);
        var newName = box.Text.Trim();
        if (cancel || stale || newName.Length == 0 || newName == entry.Name || _viewModel is null)
        {
            if (!stale)
            {
                box.Text = entry.Name; // 次回表示に備えて戻す
            }
            return;
        }
        // 不正な名前・衝突のダイアログはシェル任せ。結果は再読込で反映する
        Services.ShellFileOperations.Rename(GetWindowHandle(), entry.FullPath, newName);
        _ = _viewModel.RefreshAsync();
    }

    private void OnItemDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        CancelPendingRename();
        if (_viewModel is null)
        {
            return;
        }
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not EntryViewModel entry)
        {
            return;
        }
        var fullPath = System.IO.Path.Combine(_viewModel.Path, entry.Name);
        if (entry.IsDirectory)
        {
            _ = _viewModel.NavigateAsync(fullPath);
        }
        else
        {
            // 拡張子の既定アプリで開く。関連付けなし等の失敗時はシェルが自前でダイアログを出すため
            // アプリ側でのエラー表示はしない
            Services.ShellContextMenuService.OpenWithDefault(GetWindowHandle(), fullPath);
        }
    }

    /// <summary>右クリック: 項目上なら選択してシェルの項目メニュー、空白なら表示中フォルダの背景メニュー。</summary>
    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (_viewModel is null || string.IsNullOrEmpty(_viewModel.Path))
        {
            return;
        }
        var hwnd = GetWindowHandle();
        if (hwnd == 0)
        {
            return;
        }
        if ((e.OriginalSource as FrameworkElement)?.DataContext is EntryViewModel entry)
        {
            // 選択済み項目上の右クリックは複数選択を維持し、未選択項目上ならそこだけ選択し直す
            //（エクスプローラーと同じ挙動）
            if (!List.SelectedItems.Contains(entry))
            {
                List.SelectedItem = entry;
            }
            Services.ShellContextMenuService.ShowForItems(hwnd, _viewModel.Path, SelectedNames());
        }
        else
        {
            Services.ShellContextMenuService.ShowForBackground(hwnd, _viewModel.Path);
        }
        e.Handled = true;
    }

    private List<string> SelectedNames() =>
        List.SelectedItems.OfType<EntryViewModel>().Select(x => x.Name).ToList();

    private List<string> SelectedPaths() =>
        List.SelectedItems.OfType<EntryViewModel>().Select(x => x.FullPath).ToList();

    /// <summary>リネーム編集中は Ctrl+C/X/V・Delete を TextBox の既定動作（文字編集）に譲る。</summary>
    private bool IsRenameEditing => _renamingEntry is not null;

    private void OnCopyShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsRenameEditing)
        {
            return;
        }
        var paths = SelectedPaths();
        if (paths.Count > 0)
        {
            Services.ShellFileOperations.CopyToClipboard(paths, cut: false);
        }
        args.Handled = true;
    }

    private void OnCutShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsRenameEditing)
        {
            return;
        }
        var paths = SelectedPaths();
        if (paths.Count > 0)
        {
            Services.ShellFileOperations.CopyToClipboard(paths, cut: true);
        }
        args.Handled = true;
    }

    private void OnPasteShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsRenameEditing)
        {
            return;
        }
        if (_viewModel is { } vm && !string.IsNullOrEmpty(vm.Path))
        {
            Services.ShellFileOperations.PasteFromClipboard(GetWindowHandle(), vm.Path);
        }
        args.Handled = true;
    }

    private void OnDeleteShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsRenameEditing)
        {
            return;
        }
        var paths = SelectedPaths();
        if (paths.Count > 0)
        {
            // ごみ箱へ（確認・進捗ダイアログはシェル任せ）
            Services.ShellFileOperations.Delete(GetWindowHandle(), paths);
        }
        args.Handled = true;
    }

    private nint GetWindowHandle()
    {
        // シェルメニューの所有者に使う HWND。XamlRoot からこの View が載っているウィンドウを引く
        var environment = XamlRoot?.ContentIslandEnvironment;
        return environment is null
            ? 0
            : Microsoft.UI.Win32Interop.GetWindowFromWindowId(environment.AppWindowId);
    }

    /// <summary>D&amp;D ソース: 選択項目を StorageItems としてデータパッケージに載せる
    /// （エクスプローラー等の外部アプリへのドロップにもそのまま使える形式）。</summary>
    private void OnDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        CancelPendingRename();
        var items = e.Items.OfType<EntryViewModel>()
            .Select(x => (x.FullPath, x.IsDirectory))
            .ToList();
        if (items.Count == 0)
        {
            e.Cancel = true;
            return;
        }
        e.Data.RequestedOperation = DataPackageOperation.Copy | DataPackageOperation.Move;
        // DragItemsStarting は同期のため、StorageItem 化は遅延プロバイダで行う
        e.Data.SetDataProvider(StandardDataFormats.StorageItems, async request =>
        {
            var deferral = request.GetDeferral();
            try
            {
                var storageItems = new List<IStorageItem>();
                foreach (var (path, isDirectory) in items)
                {
                    try
                    {
                        storageItems.Add(isDirectory
                            ? await StorageFolder.GetFolderFromPathAsync(path)
                            : await StorageFile.GetFileFromPathAsync(path));
                    }
                    catch
                    {
                        // 消えた項目はスキップ
                    }
                }
                request.SetData(storageItems);
            }
            finally
            {
                deferral.Complete();
            }
        });
    }

    /// <summary>D&amp;D ターゲット: 既定は移動、Ctrl 押下でコピー（エクスプローラーの主要動作に合わせる）。</summary>
    private void OnListDragOver(object sender, DragEventArgs e)
    {
        if (_viewModel is null || string.IsNullOrEmpty(_viewModel.Path) ||
            !e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }
        e.AcceptedOperation = e.Modifiers.HasFlag(Windows.ApplicationModel.DataTransfer.DragDrop.DragDropModifiers.Control)
            ? DataPackageOperation.Copy
            : DataPackageOperation.Move;
    }

    private async void OnListDrop(object sender, DragEventArgs e)
    {
        if (_viewModel is null || string.IsNullOrEmpty(_viewModel.Path) ||
            !e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }
        var deferral = e.GetDeferral();
        try
        {
            var storageItems = await e.DataView.GetStorageItemsAsync();
            var paths = storageItems.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
            if (paths.Count > 0)
            {
                // 実コピー/移動はシェル(IFileOperation)へ。同フォルダへの移動は Transfer 側で無視される
                Services.ShellFileOperations.Transfer(
                    GetWindowHandle(), paths, _viewModel.Path,
                    move: e.AcceptedOperation == DataPackageOperation.Move);
            }
        }
        catch
        {
            // ドロップ失敗でアプリを落とさない
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnSortName(object sender, RoutedEventArgs e) => _viewModel?.SetSort(SortColumn.Name);
    private void OnSortModified(object sender, RoutedEventArgs e) => _viewModel?.SetSort(SortColumn.Modified);
    private void OnSortType(object sender, RoutedEventArgs e) => _viewModel?.SetSort(SortColumn.Type);
    private void OnSortSize(object sender, RoutedEventArgs e) => _viewModel?.SetSort(SortColumn.Size);
}
