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
        var viewModel = new MainViewModel(fileSystem);

        _window = new MainWindow(viewModel);
        _window.Activate();

        _ = viewModel.InitializeAsync();
    }
}
