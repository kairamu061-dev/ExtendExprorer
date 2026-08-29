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
/// <para>UI（STA）スレッド専用。失敗は握りつぶしてアプリを落とさない
/// （ダイアログはシェル側が出す）。</para></summary>
internal static class ShellFileOperations
{
    private static readonly StrategyBasedComWrappers ComWrappers = new();

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
            UI.Diagnostics.Report("ShellFileOperations.Transfer", ex);
        }
    }

    /// <summary>名前の変更（インライン編集の確定）。衝突・不正な名前のダイアログはシェル任せ。
    ///
    /// <para><b>同じフォルダへの「移動」として実行する。</b><c>RenameItem</c> だと、
    /// 既にある名前へ変えたときに「置換またはスキップ」ではなく
    /// <c>0x80070057</c>（パラメーターが間違っています）で中断する実機があった
    /// （2026-08-30 に毎回再現・BUG-025）。<c>MoveItem</c> は貼り付けの衝突で
    /// 正しいダイアログが出ている経路そのもので、名前を変えながらの移動＝改名になる。</para>
    ///
    /// <para>戻り値は捨てずに <c>--diag</c> に残す。実機でしか出ない失敗を、
    /// 次に推測ではなく数字で切り分けられるようにしておく。</para></summary>
    internal static void Rename(nint hwnd, string path, string newName)
    {
        try
        {
            var folder = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path));
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }
            if (NativeMethods.SHCreateItemFromParsingName(folder, 0,
                    in NativeMethods.IID_IShellItem, out var destPtr) < 0 || destPtr == 0)
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
                    var move = operation.MoveItem(itemPtrs[0], destPtr, namePtr, 0);
                    var perform = operation.PerformOperations();
                    UI.Diagnostics.Write($"[rename] MoveItem=0x{move:X8} PerformOperations=0x{perform:X8}");
                }
                finally
                {
                    Marshal.FreeCoTaskMem(namePtr);
                    ReleaseAll(itemPtrs);
                }
            }
            finally
            {
                Marshal.Release(destPtr);
            }
        }
        catch (Exception ex)
        {
            UI.Diagnostics.Report($"ShellFileOperations.Rename({newName})", ex);
        }
    }

    /// <summary>ごみ箱へ削除（シェルの確認・進捗ダイアログ付き）。</summary>
    internal static void Delete(nint hwnd, IReadOnlyList<string> paths)
    {
        try
        {
            var (operation, itemPtrs) = CreateOperation(hwnd, NativeMethods.FOF_ALLOWUNDO, paths);
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
            UI.Diagnostics.Report("ShellFileOperations.Delete", ex);
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
            NativeMethods.EmptyClipboard();

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

            // SetClipboardData が成功したら、メモリの持ち主はシステム側へ移る
            if (NativeMethods.SetClipboardData(NativeMethods.CF_HDROP, hDrop) == 0)
            {
                NativeMethods.GlobalFree(hDrop);
            }
            var format = NativeMethods.RegisterClipboardFormatW("Preferred DropEffect");
            if (format != 0 && hEffect != 0 && NativeMethods.SetClipboardData(format, hEffect) == 0)
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
