using System.Collections.ObjectModel;
using ExtendExprorer.Services;
using ExtendExprorer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ExtendExprorer.Views;

/// <summary>ウィンドウ左側のフォルダツリー。ノードのクリックはイベントで通知し、
/// タブ移動の実行は MainViewModel 側に委ねる（PaneView と同じイベント委譲方式）。</summary>
public sealed partial class FolderTreePanel : UserControl
{
    private const double ExpandedWidth = 240;
    private const double CollapsedWidth = 28;

    private IFileSystemService? _fs;
    private readonly ObservableCollection<FolderNodeViewModel> _roots = new();
    private bool _collapsed;

    /// <summary>ノード本体のクリック（Invoke）。引数は移動先フォルダのフルパス。</summary>
    public event Action<string>? FolderInvoked;

    public FolderTreePanel()
    {
        InitializeComponent();
        Tree.ItemsSource = _roots;
    }

    /// <summary>合成ルート（MainWindow）から呼ぶ。ルート（ホーム＋準備完了ドライブ）を非同期に構築する。</summary>
    public void Initialize(IFileSystemService fs)
    {
        _fs = fs;
        _ = LoadRootsAsync(fs.HomePath);
    }

    private async Task LoadRootsAsync(string homePath)
    {
        // IsReady はドライブへ実アクセスするため UI スレッドで回さない
        var drives = await Task.Run(() =>
        {
            try
            {
                return DriveInfo.GetDrives()
                    .Where(d => d.IsReady)
                    .Select(d => d.Name)
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        });

        _roots.Clear();
        _roots.Add(new FolderNodeViewModel("ホーム", homePath, isHiddenOrSystem: false, glyph: "\uE80F"));
        foreach (var drive in drives)
        {
            _roots.Add(new FolderNodeViewModel(drive, drive, isHiddenOrSystem: false, glyph: "\uEDA2"));
        }
    }

    private async void OnExpanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (_fs is null ||
            args.Item is not FolderNodeViewModel node ||
            !node.HasUnrealizedChildren ||
            node.IsLoadingChildren)
        {
            return;
        }
        node.IsLoadingChildren = true;
        try
        {
            var dirs = await _fs.ListDirectoriesAsync(node.Path);
            node.Children.Clear();
            foreach (var dir in dirs)
            {
                node.Children.Add(new FolderNodeViewModel(
                    dir.Name, System.IO.Path.Combine(node.Path, dir.Name), dir.IsHiddenOrSystem));
            }
            // 空でも false にする(シェブロンが消えて「子なし」を表す)
            node.HasUnrealizedChildren = false;
        }
        finally
        {
            node.IsLoadingChildren = false;
        }
    }

    private void OnItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is FolderNodeViewModel node)
        {
            FolderInvoked?.Invoke(node.Path);
        }
    }

    private void OnToggle(object sender, RoutedEventArgs e)
    {
        _collapsed = !_collapsed;
        Root.Width = _collapsed ? CollapsedWidth : ExpandedWidth;
        Tree.Visibility = _collapsed ? Visibility.Collapsed : Visibility.Visible;
        HeaderText.Visibility = _collapsed ? Visibility.Collapsed : Visibility.Visible;
        ToggleButton.HorizontalAlignment = _collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Right;
        // ChevronLeft / ChevronRight
        ToggleIcon.Glyph = _collapsed ? "\uE76C" : "\uE76B";
    }
}
