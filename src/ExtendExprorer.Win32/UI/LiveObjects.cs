namespace ExtendExprorer.UI;

/// <summary>「捨てたはずのものが、まだ生きているか」を数える道具（<c>--diag</c> 専用）。
///
/// <para><b>なぜ要るか。</b>外から測れるのはメモリの量だけで、その増え方は
/// <b>2 つの原因で見分けが付かない</b>——(a) 捨てたものへの参照が残っている、
/// (b) ただのゴミが溜まっているだけで、まだ回収が走っていない。
/// どちらも「1 回ごとに同じだけ増える」「放置しても減らない」ように見える
/// （<c>.NET</c> の回収は時間ではなく確保量で起きるので、放置しても走らない）。</para>
///
/// <para><b>数えれば決まる。</b>作ったものを弱い参照で控えておき、
/// <b>強制的に回収してから</b>生き残りを数える。閉じたぶんが消えていれば (b)、
/// 残っていれば (a) で、しかも<b>どの種類が残っているか</b>まで分かる。</para>
///
/// <para><c>--diag</c> のときだけ控える。通常起動では 1 バイトも使わない。</para></summary>
internal static class LiveObjects
{
    private static readonly Dictionary<string, List<WeakReference>> Tracked = [];
    private static readonly Dictionary<string, int> Created = [];
    private static readonly object Gate = new();

    /// <summary>作ったものを控える。捨てる側では何もしない
    /// （<b>捨て忘れを探す道具なので、捨てる側の申告は当てにしない</b>）。</summary>
    internal static void Track(object instance, string kind)
    {
        if (!Diagnostics.Enabled)
        {
            return;
        }
        lock (Gate)
        {
            if (!Tracked.TryGetValue(kind, out var list))
            {
                list = [];
                Tracked[kind] = list;
            }
            list.Add(new WeakReference(instance));
            Created[kind] = Created.GetValueOrDefault(kind) + 1;
        }
    }

    /// <summary><b>強制的に回収してから</b>、種類ごとの生き残りを数えて記録する。
    ///
    /// <para>回収は 2 回走らせる。1 回目で到達不能になったものの終了処理が動き、
    /// そこで初めて手放される参照があるため。</para></summary>
    internal static void Report(string reason)
    {
        if (!Diagnostics.Enabled)
        {
            return;
        }
        var before = GC.GetTotalMemory(false);

        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

        var after = GC.GetTotalMemory(false);

        var parts = new List<string>();
        lock (Gate)
        {
            foreach (var (kind, list) in Tracked)
            {
                list.RemoveAll(reference => !reference.IsAlive);
                parts.Add($"{kind}={list.Count}/{Created.GetValueOrDefault(kind)}");
            }
        }
        Diagnostics.Write(
            $"[gc] {reason} マネージド {before / 1024} → {after / 1024} KB / 生存数（生きている/作った合計） "
            + string.Join(" ", parts));
    }
}
