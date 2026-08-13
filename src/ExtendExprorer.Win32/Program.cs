using ExtendExprorer.Services;
using ExtendExprorer.UI;

namespace ExtendExprorer;

/// <summary>エントリポイント。WinUI 版の <c>App.OnLaunched</c> にあたる合成ルート
/// （DI コンテナは使わず、ここで手で組み立てる方針も引き継ぐ）。</summary>
internal static class Program
{
    [STAThread]
    private static int Main()
    {
        MainWindow.InitCommonControls();

        var fileSystem = new FileSystemService();
        var session = new SessionService();

        var window = new MainWindow();
        window.Create("ExtendExprorer");

        // 第 0 段では、移植したサービスが Native AOT 上で動くことの確認までを行う。
        // 一覧・タブ・ツリーは第 1 段以降で載せる
        RestoreLayout(window, session);
        window.Show();

        return MainWindow.RunMessageLoop();
    }

    /// <summary>session からツリーの幅・折りたたみだけ先に復元する。
    /// レイアウト木（ペイン・タブ）の復元は第 2 段で入れる。</summary>
    private static void RestoreLayout(MainWindow window, ISessionService session)
    {
        var file = session.Load();
        if (file is null)
        {
            return;
        }
        if (file.TreeWidth > 0)
        {
            window.TreeWidth = (int)Math.Round(file.TreeWidth);
        }
        window.TreeCollapsed = file.TreeCollapsed;
    }
}
