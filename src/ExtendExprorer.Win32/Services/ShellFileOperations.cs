using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using ExtendExprorer.Interop;

namespace ExtendExprorer.Services;

/// <summary>ファイルのコピー／移動／削除／名前の変更と、クリップボード連携。
/// 現行 WinUI 版からそのまま移した（Ctrl+C/X/V・Delete・リネーム・第 4b 段の D&amp;D で共用）。
///
/// <para><b>実処理はシェル（<c>IFileOperation</c>）に任せる。</b>進捗と衝突のダイアログ、
/// ごみ箱、「〜 - コピー」の自動採番がエクスプローラーと同じになる。
/// 自前で書くと必ずどこかが食い違う。</para>
///
/// <para>失敗は握りつぶしてアプリを落とさない（ダイアログはシェル側が出す）。</para>
///
/// <para>★ <b><c>PerformOperations</c> は UI スレッドで呼ばない</b>（2026-10-20 のご要望）。
/// 巨大なフォルダの削除・移動は分単位で返ってこないので、UI スレッドで待つと
/// その間こちらのメッセージループが回らない。BUG-043・044・047 と同じ家族——
/// <b>「時間のかかるものを UI スレッドから外す」の 4 件目</b>。
/// <see cref="Run"/> を通して、1 件につき 1 本の STA スレッドで走らせる。</para></summary>
internal static class ShellFileOperations
{
    private static readonly StrategyBasedComWrappers ComWrappers = new();

    /// <summary>いま走っている件数。<c>--diag</c> に出すためだけのもの。</summary>
    private static int _running;

    /// <summary>シェルのファイル操作を<b>専用の STA スレッド</b>で走らせる。
    ///
    /// <para><b>上限を設けていない。</b><see cref="ShellOpen"/> は 8 件で打ち切って捨てるが、
    /// あちらが捨てるのは「開く」で、捨てても害が無い。<b>削除・移動を捨てるのは
    /// 固まるより悪い</b>ので、ここは本数を数えて記録するだけにしてある。</para>
    ///
    /// <para><b>行列にしない</b>（BUG-045）。1 本に並べると、
    /// 返ってこない 1 件が後続を全部止める。</para>
    ///
    /// <para><b><c>CoCreateInstance</c> はこのスレッドの中で呼ぶ。</b>
    /// 外で作って渡すとアパートメントをまたぐことになる——
    /// だから <paramref name="body"/> には「作る」ところから入れる。</para>
    ///
    /// <para><c>CoInitializeEx</c> ではなく <see cref="Thread.SetApartmentState"/> を使う。
    /// ランタイムがアパートメントを承知していて、メッセージも回してくれる
    /// （BUG-047 で同じ判断をした）。</para></summary>
    private static void Run(string what, Action body)
    {
        var count = Interlocked.Increment(ref _running);
        var thread = new Thread(() =>
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            UI.Diagnostics.Write($"[fileop] 開始 {what} 同時={count}");
            try
            {
                body();
            }
            catch (Exception ex)
            {
                UI.Diagnostics.Report($"ShellFileOperations.{what}", ex);
            }
            finally
            {
                var ms = (System.Diagnostics.Stopwatch.GetTimestamp() - started)
                    * 1000 / System.Diagnostics.Stopwatch.Frequency;
                Interlocked.Decrement(ref _running);
                // ★ 経過を必ず出す。「固まった」の報告が来たときに、
                //   操作が長かったのか UI が止まったのかを分けるのはこの数字
                UI.Diagnostics.Write($"[fileop] 終わった {what} 経過={ms}ms");
            }
        })
        {
            IsBackground = true,
            Name = "ShellFileOp",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    /// <summary>クリップボード（<c>CF_HDROP</c>）の内容を貼り付ける。
    /// コピーか移動かは Preferred DropEffect に従う（無ければコピー）。</summary>
    internal static void PasteFromClipboard(nint hwnd, string destinationFolder)
    {
        var sources = ReadClipboardFileList();
        if (sources.Count == 0)
        {
            return;
        }
        var move = (GetPreferredDropEffect() & NativeMethods.DROPEFFECT_MOVE) != 0;
        Transfer(hwnd, sources, destinationFolder, move);
    }

    /// <summary>コピーまたは移動。<b>コピー元の親＝コピー先</b>の項目だけは
    /// <c>FOF_RENAMEONCOLLISION</c> を付けた別のオペレーションで実行し、
    /// 「〜 - コピー」を自動生成させる（旧版 BUG-005）。
    /// 同じフォルダへの移動は何もしない（エクスプローラーと同じ）。</summary>
    internal static void Transfer(nint hwnd, IReadOnlyList<string> sources, string destinationFolder, bool move)
    {
        // 判定（どちらが「同じ場所」か）は安いので呼び出し元のスレッドで済ませてよいが、
        // 分けると読みにくくなるだけなので、まとめて向こうへ渡す
        Run($"{(move ? "移動" : "コピー")} {sources.Count} 件 → {destinationFolder}",
            () => TransferCore(hwnd, sources, destinationFolder, move));
    }

    private static void TransferCore(nint hwnd, IReadOnlyList<string> sources, string destinationFolder, bool move)
    {
        try
        {
            var dest = Path.TrimEndingDirectorySeparator(destinationFolder);
            var samePlace = new List<string>();
            var otherPlace = new List<string>();
            foreach (var source in sources)
            {
                var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(source));
                if (string.Equals(parent, dest, StringComparison.OrdinalIgnoreCase))
                {
                    samePlace.Add(source);
                }
                else
                {
                    otherPlace.Add(source);
                }
            }
            if (otherPlace.Count > 0)
            {
                Execute(hwnd, otherPlace, destinationFolder, move, NativeMethods.FOF_ALLOWUNDO);
            }
            if (samePlace.Count > 0 && !move)
            {
                Execute(hwnd, samePlace, destinationFolder, move: false,
                    NativeMethods.FOF_ALLOWUNDO | NativeMethods.FOF_RENAMEONCOLLISION);
            }
        }
        catch (Exception ex)
        {
            UI.Diagnostics.Report("ShellFileOperations.TransferCore", ex);
        }
    }

    /// <summary>名前の変更（インライン編集の確定）。衝突・不正な名前のダイアログはシェル任せ。
    ///
    /// <para><b>ファイルとフォルダで経路を分ける。</b>どちらも改名だが、
    /// 既にある名前へ変えたときの振る舞いが実機で食い違った。</para>
    ///
    /// <list type="bullet">
    /// <item><b>ファイル</b>は<b>同じフォルダへの移動</b>として実行する。
    /// <c>RenameItem</c> は「置換またはスキップ」ではなく <c>0x80070057</c> で
    /// 中断することがあった（BUG-025）。<c>MoveItem</c> は貼り付けの衝突で
    /// 正しいダイアログが出ている経路そのもの</item>
    /// <item><b>フォルダ</b>は <c>RenameItem</c> のまま。移動として実行すると
    /// 「フォルダーの上書きの確認」を出さずに<b>黙って統合</b>してしまう
    /// （移動としては正しい動きだが、改名としては驚きが大きい・BUG-028）。
    /// フォルダ側は <c>RenameItem</c> で正しく確認が出ている</item>
    /// </list>
    ///
    /// <para>戻り値は捨てずに <c>--diag</c> に残す。実機でしか出ない失敗を、
    /// 次に推測ではなく数字で切り分けられるようにしておく。</para>
    ///
    /// <para>★ <b>ここは UI スレッドのまま</b>（<see cref="Run"/> を通さない）。
    /// 改名は<b>同じフォルダの中での付け替え</b>なので、中身の量に関係なく即座に終わる
    /// ——長さの問題が無い。対して、衝突の確認ダイアログが出る経路は実機の往復で
    /// 何度も確かめてある（BUG-025・BUG-028）。<b>測った利点が無いのに、
    /// 確かめ済みの振る舞いを動かす方が危ない。</b>
    /// 長くなる証拠が出たら、そのときに移す。</para></summary>
    internal static void Rename(nint hwnd, string path, string newName)
    {
        try
        {
            var isDirectory = Directory.Exists(path);
            var folder = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path));
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            nint destPtr = 0;
            if (!isDirectory && (NativeMethods.SHCreateItemFromParsingName(folder, 0,
                    in NativeMethods.IID_IShellItem, out destPtr) < 0 || destPtr == 0))
            {
                UI.Diagnostics.Write($"[rename] 親フォルダを解決できない: {folder}");
                return;
            }
            try
            {
                var (operation, itemPtrs) = CreateOperation(hwnd, NativeMethods.FOF_ALLOWUNDO, [path]);
                if (operation is null || itemPtrs.Count == 0)
                {
                    UI.Diagnostics.Write("[rename] IFileOperation を作れない");
                    return;
                }
                var namePtr = Marshal.StringToCoTaskMemUni(newName);
                try
                {
                    var hr = isDirectory
                        ? operation.RenameItem(itemPtrs[0], namePtr, 0)
                        : operation.MoveItem(itemPtrs[0], destPtr, namePtr, 0);
                    var perform = operation.PerformOperations();
                    UI.Diagnostics.Write(
                        $"[rename] {(isDirectory ? "RenameItem" : "MoveItem")}=0x{hr:X8} " +
                        $"PerformOperations=0x{perform:X8}");
                }
                finally
                {
                    Marshal.FreeCoTaskMem(namePtr);
                    ReleaseAll(itemPtrs);
                }
            }
            finally
            {
                if (destPtr != 0)
                {
                    Marshal.Release(destPtr);
                }
            }
        }
        catch (Exception ex)
        {
            UI.Diagnostics.Report($"ShellFileOperations.Rename({newName})", ex);
        }
    }

    /// <summary>ごみ箱へ削除（シェルの確認・進捗ダイアログ付き）。</summary>
    /// <param name="permanent"><c>true</c> なら<b>ごみ箱を経由しない</b>（<c>Shift+Delete</c> 相当）。
    /// <c>FOF_ALLOWUNDO</c> を外すだけで、<b>「完全に削除しますか」の確認は
    /// <c>IFileOperation</c> が出す</b>——こちらで確認を組まない。
    ///
    /// <para>★ <b>既定は <c>false</c>（ごみ箱）。</b>取り返せない側を既定にしない。</para></param>
    internal static void Delete(nint hwnd, IReadOnlyList<string> paths, bool permanent = false)
    {
        Run($"削除 {paths.Count} 件{(permanent ? "（完全）" : "")}",
            () => DeleteCore(hwnd, paths, permanent));
    }

    private static void DeleteCore(nint hwnd, IReadOnlyList<string> paths, bool permanent)
    {
        try
        {
            // ごみ箱へ入れるかどうかは FOF_ALLOWUNDO の有無だけで決まる
            var flags = permanent ? 0u : NativeMethods.FOF_ALLOWUNDO;
            var (operation, itemPtrs) = CreateOperation(hwnd, flags, paths);
            if (operation is null)
            {
                return;
            }
            try
            {
                foreach (var item in itemPtrs)
                {
                    operation.DeleteItem(item, 0);
                }
                operation.PerformOperations();
            }
            finally
            {
                ReleaseAll(itemPtrs);
            }
        }
        catch (Exception ex)
        {
            UI.Diagnostics.Report("ShellFileOperations.DeleteCore", ex);
        }
    }

    /// <summary>ファイル一覧をクリップボードへ（<paramref name="cut"/> で切り取り）。</summary>
    internal static unsafe void CopyToClipboard(IReadOnlyList<string> paths, bool cut)
    {
        if (paths.Count == 0 || !NativeMethods.OpenClipboard(0))
        {
            return;
        }
        try
        {
            // ★ EmptyClipboard はここで呼ばない（2026-10-03・コードレビュー指摘 9）。
            //   先に空にしてから確保に失敗すると、**利用者のクリップボードを
            //   空にしただけで終わる**。**置くものが全部できてから空にする。**

            // DROPFILES（20 バイト）＋ 各パスの NUL 終端ワイド文字列 ＋ 終端の空文字列
            var chars = paths.Sum(p => p.Length + 1) + 1;
            var size = (nuint)(20 + (chars * 2));
            var hDrop = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, size);
            if (hDrop == 0)
            {
                return;
            }
            var p = NativeMethods.GlobalLock(hDrop);
            if (p == 0)
            {
                NativeMethods.GlobalFree(hDrop);
                return;
            }
            try
            {
                *(int*)p = 20;          // pFiles: 文字列リストへのオフセット
                *(long*)(p + 4) = 0;    // pt
                *(int*)(p + 12) = 0;    // fNC
                *(int*)(p + 16) = 1;    // fWide = TRUE
                var cursor = (char*)(p + 20);
                foreach (var path in paths)
                {
                    foreach (var c in path)
                    {
                        *cursor++ = c;
                    }
                    *cursor++ = '\0';
                }
                *cursor = '\0';
            }
            finally
            {
                NativeMethods.GlobalUnlock(hDrop);
            }

            var effect = cut
                ? NativeMethods.DROPEFFECT_MOVE
                : NativeMethods.DROPEFFECT_COPY | NativeMethods.DROPEFFECT_LINK;
            var hEffect = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, sizeof(uint));
            if (hEffect != 0)
            {
                var pe = NativeMethods.GlobalLock(hEffect);
                if (pe != 0)
                {
                    *(uint*)pe = effect;
                    NativeMethods.GlobalUnlock(hEffect);
                }
            }
            // 「切り取りか」の印は**おまけ**。登録できなければ付けずに進む（貼り付けはコピー扱いになる）
            var format = NativeMethods.RegisterClipboardFormatW("Preferred DropEffect");

            // ★ 置くものが揃ったので、ここで空にする
            NativeMethods.EmptyClipboard();

            // SetClipboardData が成功したら、メモリの持ち主はシステム側へ移る
            if (NativeMethods.SetClipboardData(NativeMethods.CF_HDROP, hDrop) == 0)
            {
                NativeMethods.GlobalFree(hDrop);
            }
            // ★ 渡せなかったときは必ず解放する（2026-10-03・コードレビュー指摘 9）。
            //   前は `format != 0 && hEffect != 0 && Set…== 0` の 1 本だったので、
            //   **format が 0 のときに hEffect がどこからも解放されなかった**
            if (hEffect != 0
                && (format == 0 || NativeMethods.SetClipboardData(format, hEffect) == 0))
            {
                NativeMethods.GlobalFree(hEffect);
            }
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    /// <summary>クリップボードの <c>CF_HDROP</c> からフルパス一覧を読む。無ければ空。</summary>
    internal static unsafe List<string> ReadClipboardFileList()
    {
        var result = new List<string>();
        if (!NativeMethods.OpenClipboard(0))
        {
            return result;
        }
        try
        {
            var hDrop = NativeMethods.GetClipboardData(NativeMethods.CF_HDROP);
            if (hDrop == 0)
            {
                return result;
            }
            var count = NativeMethods.DragQueryFileW(hDrop, 0xFFFFFFFF, 0, 0);
            var buffer = stackalloc char[520];
            for (uint i = 0; i < count; i++)
            {
                var length = NativeMethods.DragQueryFileW(hDrop, i, (nint)buffer, 520);
                if (length > 0)
                {
                    result.Add(new string(buffer, 0, (int)length));
                }
            }
            return result;
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    /// <summary>クリップボードの "Preferred DropEffect"。無ければ 0。</summary>
    internal static unsafe uint GetPreferredDropEffect()
    {
        var format = NativeMethods.RegisterClipboardFormatW("Preferred DropEffect");
        if (format == 0 || !NativeMethods.OpenClipboard(0))
        {
            return 0;
        }
        try
        {
            var handle = NativeMethods.GetClipboardData(format);
            if (handle == 0)
            {
                return 0;
            }
            var ptr = NativeMethods.GlobalLock(handle);
            if (ptr == 0)
            {
                return 0;
            }
            try
            {
                return *(uint*)ptr;
            }
            finally
            {
                NativeMethods.GlobalUnlock(handle);
            }
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    private static void Execute(nint hwnd, IReadOnlyList<string> sources, string destinationFolder,
        bool move, uint flags)
    {
        if (NativeMethods.SHCreateItemFromParsingName(destinationFolder, 0,
                in NativeMethods.IID_IShellItem, out var destPtr) < 0 || destPtr == 0)
        {
            return;
        }
        try
        {
            var (operation, itemPtrs) = CreateOperation(hwnd, flags, sources);
            if (operation is null)
            {
                return;
            }
            try
            {
                foreach (var item in itemPtrs)
                {
                    if (move)
                    {
                        operation.MoveItem(item, destPtr, 0, 0);
                    }
                    else
                    {
                        operation.CopyItem(item, destPtr, 0, 0);
                    }
                }
                operation.PerformOperations();
            }
            finally
            {
                ReleaseAll(itemPtrs);
            }
        }
        finally
        {
            Marshal.Release(destPtr);
        }
    }

    private static (IFileOperation? Operation, List<nint> ItemPtrs) CreateOperation(
        nint hwnd, uint flags, IReadOnlyList<string> sources)
    {
        var itemPtrs = new List<nint>();
        if (NativeMethods.CoCreateInstance(in NativeMethods.CLSID_FileOperation, 0, NativeMethods.CLSCTX_ALL,
                in NativeMethods.IID_IFileOperation, out var opPtr) < 0 || opPtr == 0)
        {
            return (null, itemPtrs);
        }
        IFileOperation operation;
        try
        {
            operation = (IFileOperation)ComWrappers.GetOrCreateObjectForComInstance(opPtr, CreateObjectFlags.None);
        }
        finally
        {
            Marshal.Release(opPtr);
        }
        operation.SetOperationFlags(flags);
        operation.SetOwnerWindow(hwnd);
        foreach (var source in sources)
        {
            if (NativeMethods.SHCreateItemFromParsingName(source, 0,
                    in NativeMethods.IID_IShellItem, out var itemPtr) >= 0 && itemPtr != 0)
            {
                itemPtrs.Add(itemPtr);
            }
        }
        return (operation, itemPtrs);
    }

    private static void ReleaseAll(List<nint> ptrs)
    {
        foreach (var ptr in ptrs)
        {
            Marshal.Release(ptr);
        }
    }
}
