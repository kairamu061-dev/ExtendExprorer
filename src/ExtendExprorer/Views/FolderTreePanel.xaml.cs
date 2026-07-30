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
    private const double DefaultExpandedWidth = 240;
    private const double CollapsedWidth = 28;

    /// <summary>ツリーの幅の下限・上限（PanelSplitter のドラッグもこの範囲に収める）。</summary>
    public const double MinExpandedWidth = 120;
    public const double MaxExpandedWidth = 600;

    private IFileSystemService? _fs;
    private readonly ObservableCollection<FolderNodeViewModel> _roots = new();
    private bool _collapsed;
    private double _expandedWidth = DefaultExpandedWidth;

    /// <summary>ノード本体のクリック（Invoke）。引数は移動先フォルダのフルパス。</summary>
    public event Action<string>? FolderInvoked;

    /// <summary>幅・折りたたみ状態が変わったときに発火（セッション保存とスプリッターの表示切替に使う）。</summary>
    public event Action? LayoutStateChanged;

    /// <summary>展開時の幅。折りたたみ中も「戻したときの幅」として保持する。</summary>
    public double ExpandedWidth => _expandedWidth;

    public bool IsCollapsed => _collapsed;

    /// <summary>ツリーの幅を変える（スプリッターのドラッグ）。範囲外はクランプする。
    /// ドラッグ中は <paramref name="notify"/>=false で呼び、確定時にまとめて通知する。</summary>
    public void SetExpandedWidth(double width, bool notify = true)
    {
        // 整数 px に丸める（session.json に小数が並ばず、復元の往復でも値がぶれない）
        var clamped = Math.Round(Math.Clamp(double.IsFinite(width) ? width : DefaultExpandedWidth,
            MinExpandedWidth, MaxExpandedWidth));
        if (Math.Abs(clamped - _expandedWidth) < 0.5)
        {
            return;
        }
        _expandedWidth = clamped;
        if (!_collapsed)
        {
            Root.Width = clamped;
        }
        if (notify)
        {
            LayoutStateChanged?.Invoke();
        }
    }

    /// <summary>セッションからの復元（通知しない＝復元自体を保存契機にしない）。</summary>
    public void RestoreLayout(double width, bool collapsed)
    {
        if (width > 0)
        {
            _expandedWidth = Math.Round(Math.Clamp(width, MinExpandedWidth, MaxExpandedWidth));
        }
        if (collapsed != _collapsed)
        {
            ApplyCollapsed(collapsed);
        }
        else
        {
            Root.Width = _collapsed ? CollapsedWidth : _expandedWidth;
        }
    }

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
        // ルート(ホーム・ドライブ)はシェルの固有アイコンを使う（解決までは従来グリフ）
        _roots.Add(new FolderNodeViewModel("ホーム", homePath, isHiddenOrSystem: false,
            glyph: "\uE80F", distinctIcon: true));
        foreach (var drive in drives)
        {
            _roots.Add(new FolderNodeViewModel(drive, drive, isHiddenOrSystem: false,
                glyph: "\uEDA2", distinctIcon: true));
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
        ApplyCollapsed(!_collapsed);
        LayoutStateChanged?.Invoke();
    }

    private void ApplyCollapsed(bool collapsed)
    {
        _collapsed = collapsed;
        // 展開時はドラッグで決めた幅に戻す
        Root.Width = _collapsed ? CollapsedWidth : _expandedWidth;
        Tree.Visibility = _collapsed ? Visibility.Collapsed : Visibility.Visible;
        HeaderText.Visibility = _collapsed ? Visibility.Collapsed : Visibility.Visible;
        ToggleButton.HorizontalAlignment = _collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Right;
        // ChevronLeft / ChevronRight
        ToggleIcon.Glyph = _collapsed ? "\uE76C" : "\uE76B";
    }
}
