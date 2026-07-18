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

    public TabViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }
            _viewModel = value;
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
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

    private void OnItemDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
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

    private void OnCopyShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        var paths = SelectedPaths();
        if (paths.Count > 0)
        {
            Services.ShellFileOperations.CopyToClipboard(paths, cut: false);
        }
        args.Handled = true;
    }

    private void OnCutShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        var paths = SelectedPaths();
        if (paths.Count > 0)
        {
            Services.ShellFileOperations.CopyToClipboard(paths, cut: true);
        }
        args.Handled = true;
    }

    private void OnPasteShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_viewModel is { } vm && !string.IsNullOrEmpty(vm.Path))
        {
            Services.ShellFileOperations.PasteFromClipboard(GetWindowHandle(), vm.Path);
        }
        args.Handled = true;
    }

    private void OnDeleteShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
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
