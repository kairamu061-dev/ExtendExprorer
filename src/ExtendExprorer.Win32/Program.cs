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
        // --diag: 実機でしか再現しない不具合を切り分けるための書き出し。
        // 通常起動では一切動かない（docs/win32-migration/dev-notes.md）
        Diagnostics.Enabled = args.Any(a => string.Equals(a, "--diag", StringComparison.OrdinalIgnoreCase));
        Diagnostics.Write($"=== ExtendExprorer 診断 {DateTime.Now:yyyy/MM/dd HH:mm:ss} ===");

        MainWindow.InitCommonControls();

        var fileSystem = new FileSystemService();
        var session = new SessionService();

        var file = session.Load();
        var pane = new PaneModel(fileSystem);
        var window = new MainWindow(pane);

        // ツリー幅の復元はウィンドウを作る前に（最初のレイアウト計算に間に合わせる）
        RestoreLayout(window, file);
        window.Create("ExtendExprorer");

        // 第 2 段ではペインが 1 つ。分割は次の段で入れる
        OpenInitialTabs(pane, args, file, fileSystem);

        window.Show();
        return MainWindow.RunMessageLoop();
    }

    /// <summary>最初に開くタブ。
    /// <list type="number">
    /// <item>起動引数のフォルダ（<b>複数指定できる</b>。指定した数だけタブが開く）</item>
    /// <item>session の最初のペインのタブ</item>
    /// <item>ホーム</item>
    /// </list>
    /// 引数を複数取れるようにしてあるのは、タブを何十枚も開いた状態を
    /// 手作業なしで作れるようにするため（メモリ実測の再現性）。</summary>
    private static void OpenInitialTabs(PaneModel pane, string[] args, SessionFile? file, IFileSystemService fs)
    {
        var opened = false;
        foreach (var path in StartPaths(args))
        {
            // 最初の 1 枚だけアクティブにする。残りは裏で開くだけなので列挙は走らない
            pane.AddTab(path, activate: !opened);
            opened = true;
        }
        if (opened)
        {
            return;
        }
        pane.AddTab(FirstTabPath(file?.Layout) ?? fs.HomePath);
    }

    /// <summary>コマンドラインで指定されたフォルダ（<c>ExtendExprorer.exe &lt;folder&gt;...</c>）。
    /// session より優先する。アドレスバーが入る第 3 段まで、開くフォルダを指定できる唯一の手段。</summary>
    private static IEnumerable<string> StartPaths(string[] args)
    {
        foreach (var arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }
            var path = arg.Trim().Trim('"');
            if (path.Length > 0 && Directory.Exists(path))
            {
                yield return path;
            }
        }
    }

    /// <summary>session からツリーの幅・折りたたみだけ先に復元する。
    /// レイアウト木（ペイン・タブ）の復元は第 5 段で入れる。</summary>
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

    /// <summary>最初のペインの最初のタブのパス。</summary>
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
