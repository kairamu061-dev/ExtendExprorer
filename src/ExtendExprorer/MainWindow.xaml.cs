using ExtendExprorer.Models.Session;
using ExtendExprorer.Services;
using ExtendExprorer.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace ExtendExprorer;

public sealed partial class MainWindow : Window
{
    private readonly ISessionService _session;
    private readonly DispatcherTimer _saveTimer;
    private bool _sessionReady;

    public MainViewModel ViewModel { get; }

    public MainWindow(MainViewModel viewModel, IFileSystemService fileSystem, ISessionService session)
    {
        ViewModel = viewModel;
        _session = session;
        InitializeComponent();
        Title = "ExtendExprorer";
        SetWindowIcon();
        Host.ViewModel = ViewModel;
        TreePanel.Initialize(fileSystem);
        TreePanel.FolderInvoked += ViewModel.NavigateActiveTab;
        // ツリーと一覧の境界（幅はドラッグで変えられ、セッションに保存する）
        TreeSplitter.Attach(TreePanel, MainArea);
        TreeSplitter.WidthCommitted += OnSessionDirty;
        TreePanel.LayoutStateChanged += OnSessionDirty;

        // 状態変更は 1 秒デバウンスで保存
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _saveTimer.Tick += OnSaveTimerTick;
        ViewModel.SessionDirty += OnSessionDirty;
        AppWindow.Changed += OnAppWindowChanged;
        Closed += OnClosed;

        _ = InitializeSessionAsync();
    }

    /// <summary>ウィンドウ左上・タスクバーのアイコン。exe のアイコン（ApplicationIcon）とは別に、
    /// 実行中のウィンドウへも同じ .ico を明示指定する。</summary>
    private void SetWindowIcon()
    {
        try
        {
            // unpackaged 配布なので exe と同じフォルダの Assets を絶対パスで指す
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (System.IO.File.Exists(path))
            {
                AppWindow.SetIcon(path);
            }
        }
        catch
        {
            // アイコンが無い・読めない場合は既定のアイコンのまま起動する
        }
    }

    /// <summary>起動時: session.json があれば復元、無ければ既定状態で初期化。</summary>
    private async Task InitializeSessionAsync()
    {
        var file = await _session.LoadAsync();
        if (file is not null)
        {
            TreePanel.RestoreLayout(file.TreeWidth, file.TreeCollapsed);
        }
        if (file?.Layout is not null)
        {
            ApplyBounds(file.Bounds);
            if (await ViewModel.RestoreAsync(file))
            {
                ShowRestoreNotice();
            }
        }
        else
        {
            await ViewModel.InitializeAsync();
        }
        // 以降の変更・ウィンドウ移動から保存を有効化（起動中の初期変化は保存しない）
        _sessionReady = true;
    }

    private void ApplyBounds(WindowBounds? bounds)
    {
        if (bounds is null || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }
        try
        {
            AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        }
        catch
        {
            // 画面外座標など復元不能時は既定位置のまま
        }
    }

    private void OnSessionDirty() => RestartSaveTimer();

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (_sessionReady && (args.DidPositionChange || args.DidSizeChange))
        {
            RestartSaveTimer();
        }
    }

    private void RestartSaveTimer()
    {
        if (!_sessionReady)
        {
            return;
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void OnSaveTimerTick(object? sender, object e)
    {
        _saveTimer.Stop();
        _ = _session.SaveAsync(CaptureSession());
    }

    /// <summary>保存用のスナップショット。ツリーの幅・折りたたみは View 側だけが知る状態なので、
    /// MainViewModel の分に MainWindow が付け足す（Bounds と同じ扱い）。</summary>
    private SessionFile CaptureSession()
    {
        var file = ViewModel.CaptureSession(CurrentBounds());
        file.TreeWidth = TreePanel.ExpandedWidth;
        file.TreeCollapsed = TreePanel.IsCollapsed;
        return file;
    }

    private WindowBounds? CurrentBounds()
    {
        try
        {
            var pos = AppWindow.Position;
            var size = AppWindow.Size;
            return new WindowBounds { X = pos.X, Y = pos.Y, Width = size.Width, Height = size.Height };
        }
        catch
        {
            return null;
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        ViewModel.SessionDirty -= OnSessionDirty;
        TreeSplitter.WidthCommitted -= OnSessionDirty;
        TreePanel.LayoutStateChanged -= OnSessionDirty;
        AppWindow.Changed -= OnAppWindowChanged;
        _saveTimer.Stop();
        if (_sessionReady)
        {
            // 終了時は最終状態を同期保存してから閉じる
            _session.SaveSync(CaptureSession());
        }
    }

    private void ShowRestoreNotice()
    {
        RestoreInfo.IsOpen = true;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            RestoreInfo.IsOpen = false;
        };
        timer.Start();
    }
}
