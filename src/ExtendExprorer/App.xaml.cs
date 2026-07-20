using ExtendExprorer.Services;
using ExtendExprorer.ViewModels;
using Microsoft.UI.Xaml;

namespace ExtendExprorer;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 合成ルート: サービスはここで生成しコンストラクタ注入する
        var fileSystem = new FileSystemService();
        var session = new SessionService();
        var viewModel = new MainViewModel(fileSystem);

        // 起動時の復元 or 既定初期化・保存は MainWindow が担う（ウィンドウ位置/サイズも扱うため）
        _window = new MainWindow(viewModel, fileSystem, session);
        _window.Activate();
    }
}
