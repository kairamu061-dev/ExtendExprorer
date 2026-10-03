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

    /// <summary>調査用の 1 行（<b>補間文字列はこちらに来る</b>）。
    ///
    /// <para>★ <b><see cref="Enabled"/> が false なら、文字列を組み立てもしない</b>
    /// （2026-10-03・コードレビュー指摘 12）。
    /// <c>Write(string)</c> だけだったときは、中で捨てる前に<b>引数が評価済み</b>で、
    /// <b>普通の起動でもフォルダを開くたびに補間文字列を 30 本作っていた</b>
    /// （<c>FileSystemService</c>。<c>FileAttributes.ToString()</c> 込み）。</para>
    ///
    /// <para><b>呼び出し側は 1 文字も変わらない。</b><c>$"..."</c> はこの overload に
    /// 束縛され、ハンドラの構築子が <c>shouldAppend=false</c> を返すと
    /// <b>コンパイラが Append の呼び出しを丸ごと飛ばす</b>。
    /// <c>LogDraw</c> / <c>ReportMessageGeometry</c> が先頭で <c>Enabled</c> を見ていたのと
    /// 同じことを、全部の呼び出しに効かせる形。</para></summary>
    internal static void Write(ref DiagLine line)
    {
        if (!Enabled)
        {
            return;
        }
        Write(line.ToStringAndClear());
    }

    /// <summary>調査用の 1 行。<see cref="Enabled"/> のときだけ書く。
    /// <b>文字列を先に作ってしまう形</b>なので、
    /// 組み立てが高く付く所では <c>$"..."</c> のまま渡すこと（上の overload に行く）。</summary>
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
    /// 本当の記録が埋もれる（BUG-021 で 1 度やっている）。</para>
    ///
    /// <para><b>あきらめの札は <see cref="Report"/> と分けてある。</b>ここでの書き損じで
    /// 例外の記録まで止まると、<b>落ちたときのスタックが残らなくなる</b>——
    /// ついでの記録が、いちばん要る記録を巻き添えにしてはいけない。</para></summary>
    internal static void Note(string message)
    {
        Write(message);
        if (_noteFailed)
        {
            return;
        }
        _noteFailed = !Append($"[{DateTime.Now:yyyy/MM/dd HH:mm:ss}] {message}{Environment.NewLine}{Environment.NewLine}");
    }

    private static bool _noteFailed;

    internal static void Report(string context, Exception ex)
    {
        if (_failed)
        {
            return;
        }
        // 記録にすら失敗する状況（ディスク満杯・権限なし）では、以後あきらめて動作を続ける
        _failed = !Append(
            $"[{DateTime.Now:yyyy/MM/dd HH:mm:ss}] {context}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
    }

    /// <summary><see cref="LogPath"/> へ追記する。書けたら true。</summary>
    private static bool Append(string text)
    {
        try
        {
            lock (Gate)
            {
                var path = LogPath;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                File.AppendAllText(path, text);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary><c>Diagnostics.Write($"...")</c> のための補間文字列ハンドラ。
///
/// <para><b>構築子が <c>out bool shouldAppend</c> を取る</b>のが要点——
/// false を返すと、コンパイラが <c>AppendLiteral</c> / <c>AppendFormatted</c> の
/// 呼び出しを<b>まるごと生成しない</b>。だから <c>--diag</c> 無しのときは
/// <b>文字列も、埋め込む値の <c>ToString()</c> も走らない</b>。</para>
///
/// <para>Native AOT でも問題なく効く（<c>DefaultInterpolatedStringHandler</c> を
/// そのまま包んでいるだけで、リフレクションは使わない）。</para></summary>
[System.Runtime.CompilerServices.InterpolatedStringHandler]
internal ref struct DiagLine
{
    private System.Runtime.CompilerServices.DefaultInterpolatedStringHandler _inner;
    private readonly bool _on;

    public DiagLine(int literalLength, int formattedCount, out bool shouldAppend)
    {
        _on = Diagnostics.Enabled;
        shouldAppend = _on;
        _inner = _on
            ? new System.Runtime.CompilerServices.DefaultInterpolatedStringHandler(literalLength, formattedCount)
            : default;
    }

    public void AppendLiteral(string value) => _inner.AppendLiteral(value);

    public void AppendFormatted<T>(T value) => _inner.AppendFormatted(value);

    public void AppendFormatted<T>(T value, string? format) => _inner.AppendFormatted(value, format);

    public void AppendFormatted<T>(T value, int alignment) => _inner.AppendFormatted(value, alignment);

    public void AppendFormatted<T>(T value, int alignment, string? format) =>
        _inner.AppendFormatted(value, alignment, format);

    public void AppendFormatted(string? value) => _inner.AppendFormatted(value);

    public void AppendFormatted(ReadOnlySpan<char> value) => _inner.AppendFormatted(value);

    internal string ToStringAndClear() => _on ? _inner.ToStringAndClear() : "";
}
