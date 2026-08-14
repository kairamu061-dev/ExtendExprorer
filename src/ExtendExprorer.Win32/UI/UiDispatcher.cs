using System.Collections.Concurrent;
using static ExtendExprorer.Interop.Win32;

namespace ExtendExprorer.UI;

/// <summary>ワーカースレッドから UI スレッドへ処理を戻す仕組み。WinUI 版の
/// <c>DispatcherQueue.TryEnqueue</c> にあたる。
///
/// <para><b>素の Win32 には同期コンテキストが無い</b>ので、<c>await</c> の続きはスレッドプールで
/// 走ってしまう。ウィンドウやコントロールを触ってよいのは作成したスレッドだけなので、
/// フォルダ列挙の結果もフォルダ監視の通知も、必ずここを通して UI スレッドに載せ替える。</para>
///
/// <para>投入は常にキューへ行い、ウィンドウがまだ無い間は起こす通知だけを省く。
/// <see cref="Attach"/> の直後に一度掃き出すので、ウィンドウ作成前に終わった初回読込を
/// 取りこぼさない。</para></summary>
internal static class UiDispatcher
{
    internal const uint WM_DISPATCH = WM_APP + 1;

    private static readonly ConcurrentQueue<Action> Queue = new();
    private static nint _hwnd;

    /// <summary>宛先のウィンドウを登録し、溜まっていた分を掃き出す。UI スレッドから呼ぶ。</summary>
    internal static void Attach(nint hwnd)
    {
        _hwnd = hwnd;
        Drain();
    }

    internal static void Post(Action action)
    {
        Queue.Enqueue(action);
        var hwnd = _hwnd;
        if (hwnd != 0)
        {
            PostMessageW(hwnd, WM_DISPATCH, 0, 0);
        }
    }

    /// <summary>溜まっている処理を実行する。UI スレッド専用。
    /// 1 件の失敗で残りを落とさないよう、例外はここで止めて記録する。</summary>
    internal static void Drain()
    {
        while (Queue.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Diagnostics.Report("UiDispatcher", ex);
            }
        }
    }
}
