using ExtendExprorer.UI;

namespace ExtendExprorer.Services;

/// <summary>ファイルを既定のアプリで開く仕事を、<b>UI スレッドの外</b>で走らせる（BUG-047）。
///
/// <para><b>なぜ要るか。</b><c>ShellExecuteEx</c> は<b>長く返ってこないことがある</b>——
/// 実機で 2 回、<b>戻らないまま</b>になっている（BUG-046。未署名＋Mark of the Web の exe で、
/// SmartScreen の問い合わせを待つ形）。それを UI スレッドで呼んでいたので、
/// <b>印の付いた exe をこの一覧からダブルクリックすると、窓ごと固まって戻らなかった。</b></para>
///
/// <para>BUG-038 / BUG-041（アイコン）・BUG-043（起動）・BUG-044（ツリー）と<b>同じ家族</b>。
/// <b>避けられない仕事は UI スレッドから外す。</b></para></summary>
internal static class ShellOpen
{
    /// <summary>★ <b>1 回ごとに 1 本</b>作って、終わったら捨てる。
    ///
    /// <para><b>列に積まない</b>のは、<b>BUG-045 を繰り返さない</b>ため——
    /// あちらはアイコンを引く列が 1 本しかなく、<b>先頭が詰まると後ろが全部止まった</b>。
    /// ここで同じ作りにすると、<b>戻ってこない 1 件が、次に開くものを全部止める</b>。</para>
    ///
    /// <para>開くのは<b>人が押したときだけ</b>なので、本数は自然に少ない。
    /// それでも連打に備えて上限を置く。</para></summary>
    private const int MaxConcurrent = 8;

    private static int _running;

    /// <summary>既定のアプリで開く。<b>すぐ戻る。</b>
    ///
    /// <para><paramref name="owner"/> はシェルが出すダイアログ
    /// （「このファイルを実行しますか？」など）の持ち主。
    /// <b>別スレッドから渡すことになる</b>が、持ち主を付けないと
    /// ダイアログがどの窓のものか分からなくなる。</para></summary>
    internal static void Default(nint owner, string path)
    {
        if (path.Length == 0)
        {
            return;
        }
        if (Interlocked.Increment(ref _running) > MaxConcurrent)
        {
            Interlocked.Decrement(ref _running);
            // 連打の上限。**黙って捨てる**——もう一度押せばよいし、
            // ここで待たせると UI スレッドを止めることになる
            Diagnostics.Write($"[open] 同時に開きすぎ（{MaxConcurrent} 件）。捨てた: {path}");
            return;
        }

        var thread = new Thread(() => Run(owner, path))
        {
            IsBackground = true,
            Name = "ShellOpen",
        };
        // ★ STA にする。ShellExecuteEx は中でシェル拡張（データソース・コンテキスト
        //   メニューのハンドラ・verb の実装）を COM で呼ぶことがあり、**STA を要求する
        //   ものがある**（MTA の ShellNamespace / ShellIcons とは事情が違う）。
        //
        //   ★ CoInitializeEx を自分で呼ばず SetApartmentState を使うのが要点。
        //     こうするとランタイムがその区画を知っているので、**待っている間に
        //     メッセージを回してくれる**——ShellNamespace の doc にある
        //     「ポンプの無い STA はデッドロックの温床で、ポンプを書くとそれ自体が
        //     検証の対象になる」を、自前のループを書かずに避けられる
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static void Run(nint owner, string path)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            ShellContextMenuService.OpenWithDefault(owner, path);
        }
        catch (Exception ex)
        {
            // 開けないことより、スレッドが外へ例外を出す方が困る
            Diagnostics.Report($"ShellOpen.Run({path})", ex);
        }
        finally
        {
            Interlocked.Decrement(ref _running);
        }
        var ms = (System.Diagnostics.Stopwatch.GetTimestamp() - started)
            * 1000 / System.Diagnostics.Stopwatch.Frequency;
        // ★ この行が「UI は止まっていない」の裏取りになる。
        //   経過が長くても、一覧はその間ずっと触れているはず
        Diagnostics.Write($"[open] 終わった 経過={ms}ms {path}");
    }
}
