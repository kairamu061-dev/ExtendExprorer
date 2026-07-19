using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using ExtendExprorer.Interop;

namespace ExtendExprorer.Services;

/// <summary>ファイルのコピー/移動/削除とクリップボード連携（貼り付け・Ctrl+C/X/V/Delete・D&amp;D 共用）。
/// 実際のファイル操作・進捗/衝突ダイアログ・ごみ箱・自動リネームはシェル(IFileOperation)に任せる。
/// UI(STA) スレッド専用。失敗は握りつぶしてアプリを落とさない。</summary>
internal static class ShellFileOperations
{
    private static readonly StrategyBasedComWrappers ComWrappers = new();

    /// <summary>クリップボード(CF_HDROP)の内容を destinationFolder へ貼り付ける。
    /// コピー/移動は Preferred DropEffect に従う（無ければコピー）。</summary>
    public static void PasteFromClipboard(nint hwnd, string destinationFolder)
    {
        var sources = ReadClipboardFileList();
        if (sources.Count == 0)
        {
            return;
        }
        var move = (GetPreferredDropEffect() & NativeMethods.DROPEFFECT_MOVE) != 0;
        Transfer(hwnd, sources, destinationFolder, move);
    }

    /// <summary>コピーまたは移動。コピー元の親フォルダ＝コピー先のアイテムは
    /// FOF_RENAMEONCOLLISION を付けた別オペレーションで実行し「〜 - コピー」を自動生成する(BUG-005)。
    /// 同フォルダへの移動は何もしない（エクスプローラーと同じ）。</summary>
    public static void Transfer(nint hwnd, IReadOnlyList<string> sources, string destinationFolder, bool move)
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
        catch
        {
        }
    }

    /// <summary>名前の変更（インライン編集のコミット。衝突・不正名のダイアログはシェル任せ）。</summary>
    public static void Rename(nint hwnd, string path, string newName)
    {
        try
        {
            var (operation, itemPtrs) = CreateOperation(hwnd, NativeMethods.FOF_ALLOWUNDO, new[] { path });
            if (operation is null || itemPtrs.Count == 0)
            {
                return;
            }
            var namePtr = Marshal.StringToCoTaskMemUni(newName);
            try
            {
                operation.RenameItem(itemPtrs[0], namePtr, 0);
                operation.PerformOperations();
            }
            finally
            {
                Marshal.FreeCoTaskMem(namePtr);
                ReleaseAll(itemPtrs);
            }
        }
        catch
        {
        }
    }

    /// <summary>ごみ箱へ削除（シェルの確認・進捗ダイアログ付き）。</summary>
    public static void Delete(nint hwnd, IReadOnlyList<string> paths)
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
        catch
        {
        }
    }

    /// <summary>ファイル一覧をクリップボードへ（cut=true で切り取り）。CF_HDROP＋Preferred DropEffect。</summary>
    public static unsafe void CopyToClipboard(IReadOnlyList<string> paths, bool cut)
    {
        if (paths.Count == 0 || !NativeMethods.OpenClipboard(0))
        {
            return;
        }
        try
        {
            NativeMethods.EmptyClipboard();

            // DROPFILES(20 バイト) + 各パスの NUL 終端ワイド文字列 + 終端の空文字列
            var chars = paths.Sum(p => p.Length + 1) + 1;
            var size = (nuint)(20 + chars * 2);
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
                *(int*)p = 20;           // pFiles: 文字列リストへのオフセット
                *(long*)(p + 4) = 0;     // pt
                *(int*)(p + 12) = 0;     // fNC
                *(int*)(p + 16) = 1;     // fWide = TRUE
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

            // エクスプローラー準拠: コピー = COPY|LINK(5), 切り取り = MOVE(2)
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

            // SetClipboardData 成功後のメモリ所有権はシステム側に移る
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

    /// <summary>クリップボードの CF_HDROP からフルパス一覧を読む。無ければ空。</summary>
    public static unsafe List<string> ReadClipboardFileList()
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
                var len = NativeMethods.DragQueryFileW(hDrop, i, (nint)buffer, 520);
                if (len > 0)
                {
                    result.Add(new string(buffer, 0, (int)len));
                }
            }
            return result;
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    /// <summary>クリップボードの "Preferred DropEffect"（コピー=1/移動=2）。無ければ 0。</summary>
    public static unsafe uint GetPreferredDropEffect()
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

    private static void Execute(nint hwnd, IReadOnlyList<string> sources, string destinationFolder, bool move, uint flags)
    {
        if (NativeMethods.SHCreateItemFromParsingName(destinationFolder, 0, in NativeMethods.IID_IShellItem, out var destPtr) < 0 ||
            destPtr == 0)
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

    private static (IFileOperation? Operation, List<nint> ItemPtrs) CreateOperation(nint hwnd, uint flags, IReadOnlyList<string> sources)
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
            if (NativeMethods.SHCreateItemFromParsingName(source, 0, in NativeMethods.IID_IShellItem, out var itemPtr) >= 0 &&
                itemPtr != 0)
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
