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

    /// <summary>調査用の書き出しが有効か。<c>--diag</c> を付けて起動したときだけ true。
    ///
    /// <para>実機でしか再現しない不具合を、推測ではなく<b>数字で</b>切り分けるための仕組み。
    /// 通常起動では一切動かない（判定は起動時の 1 回だけ）。</para></summary>
    internal static bool Enabled { get; set; }

    internal static string DiagPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ExtendExprorer", "diag.log");

    /// <summary>調査用の 1 行。<see cref="Enabled"/> のときだけ書く。</summary>
    internal static void Write(string line)
    {
        if (!Enabled)
        {
            return;
        }
        try
        {
            lock (Gate)
            {
                var path = DiagPath;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch
        {
            Enabled = false;
        }
    }

    /// <summary>例外ではないが<b>あとで効いてくる出来事</b>を、<c>--diag</c> 無しでも残す。
    ///
    /// <para>「設定が消えた」と言われたときに、<b>壊れていたのか初回起動だったのか</b>を
    /// 後から分けられるようにするためのもの。<see cref="Write"/> は <c>--diag</c> を
    /// 付けた回にしか出ないので、普段使いで起きた事故には届かない。</para>
    ///
    /// <para><b>普通に起きることは書かないこと。</b>起動のたびに行が増えると、
    /// 本当の記録が埋もれる（BUG-021 で 1 度やっている）。</para></summary>
    internal static void Note(string message)
    {
        Write(message);
        Report(message, null);
    }

    internal static void Report(string context, Exception? ex)
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
                var detail = ex is null ? string.Empty : $"{ex}{Environment.NewLine}";
                File.AppendAllText(path,
                    $"[{DateTime.Now:yyyy/MM/dd HH:mm:ss}] {context}{Environment.NewLine}{detail}{Environment.NewLine}");
            }
        }
        catch
        {
            // 記録にすら失敗する状況（ディスク満杯・権限なし）では、以後あきらめて動作を続ける
            _failed = true;
        }
    }
}
