namespace ExtendExprorer.UI;

/// <summary>握りつぶした例外の記録先。
///
/// <para><b>なぜ必要か</b>: <c>[UnmanagedCallersOnly]</c> のウィンドウプロシージャからは
/// マネージド例外を外へ出せない。出るとランタイムが fail-fast してプロセスが即死し、
/// ダイアログもスタックも残らない（BUG-013 の症状と見分けがつかない）。
/// そのため境界で必ず捕まえるのだが、握りつぶしただけでは実機での原因が分からなくなる。
/// ここに書き出しておけば、確認をお願いした側でファイルを見てもらえる。</para></summary>
internal static class Diagnostics
{
    private static readonly object Gate = new();
    private static string? _logPath;
    private static bool _failed;

    internal static string LogPath => _logPath ??= System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ExtendExprorer", "error.log");

    internal static void Report(string context, Exception ex)
    {
        if (_failed)
        {
            return;
        }
        try
        {
            lock (Gate)
            {
                var path = LogPath;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                File.AppendAllText(path,
                    $"[{DateTime.Now:yyyy/MM/dd HH:mm:ss}] {context}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch
        {
            // 記録にすら失敗する状況（ディスク満杯・権限なし）では、以後あきらめて動作を続ける
            _failed = true;
        }
    }
}
