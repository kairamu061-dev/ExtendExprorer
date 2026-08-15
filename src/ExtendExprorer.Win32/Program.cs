using ExtendExprorer.Models.Session;
using ExtendExprorer.Services;
using ExtendExprorer.UI;
using ExtendExprorer.ViewModels;

namespace ExtendExprorer;

/// <summary>エントリポイント。WinUI 版の <c>App.OnLaunched</c> にあたる合成ルート
/// （DI コンテナは使わず、ここで手で組み立てる方針も引き継ぐ）。</summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        MainWindow.InitCommonControls();

        var fileSystem = new FileSystemService();
        var session = new SessionService();

        var file = session.Load();
        var fileList = new FileListViewModel(fileSystem);
        var window = new MainWindow(fileList);

        // ツリー幅の復元はウィンドウを作る前に（最初のレイアウト計算に間に合わせる）
        RestoreLayout(window, file);
        window.Create("ExtendExprorer");

        // 第 1 段では一覧を 1 つだけ開く。タブ・ペインは第 2 段、ツリーは第 3 段
        fileList.Navigate(StartPath(args) ?? FirstTabPath(file?.Layout) ?? fileSystem.HomePath);

        window.Show();
        return MainWindow.RunMessageLoop();
    }

    /// <summary>session からツリーの幅・折りたたみだけ先に復元する。
    /// レイアウト木（ペイン・タブ）の復元は第 2 段で入れる。</summary>
    private static void RestoreLayout(MainWindow window, SessionFile? file)
    {
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

    /// <summary>コマンドラインで指定されたフォルダ（<c>ExtendExprorer.exe &lt;folder&gt;</c>）。
    /// session より優先する。アドレスバーが入る第 3 段まで、開くフォルダを指定できる唯一の手段。</summary>
    private static string? StartPath(string[] args)
    {
        foreach (var arg in args)
        {
            var path = arg.Trim().Trim('"');
            if (path.Length > 0 && Directory.Exists(path))
            {
                return path;
            }
        }
        return null;
    }

    /// <summary>最初のペインの最初のタブのパス。第 1 段は一覧が 1 つしか無いので、
    /// 「前回の続きから」の代わりにここだけ使う（保存はまだ行わない）。</summary>
    private static string? FirstTabPath(LayoutSnapshot? node)
    {
        if (node is null)
        {
            return null;
        }
        if (node.Tabs is { Count: > 0 } tabs)
        {
            var path = tabs[Math.Clamp(node.ActiveTabIndex, 0, tabs.Count - 1)].Path;
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                return path;
            }
        }
        return FirstTabPath(node.First) ?? FirstTabPath(node.Second);
    }
}
