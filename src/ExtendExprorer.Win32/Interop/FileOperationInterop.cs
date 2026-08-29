using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace ExtendExprorer.Interop;

/// <summary>ファイルのコピー／移動／削除／名前の変更（<c>IFileOperation</c>）。
///
/// <para>現行 WinUI 版からそのまま移した。<b>実際のファイル操作・進捗と衝突のダイアログ・
/// ごみ箱・「〜 - コピー」の自動採番はシェルに任せる</b>のが方針で、
/// 自前で書くとエクスプローラーと挙動が食い違う。</para>
///
/// <para>COM は .NET 8 のソース生成（<c>[GeneratedComInterface]</c>）だけを使う。
/// Native AOT では実行時にリフレクションで vtable を組む従来の COM 相互運用が使えない
/// （BUG-013 の原因もこの周辺だった）。<b>使わないメソッドも vtable の位置を合わせるために
/// 全部宣言する。</b>1 つでも抜けると、それ以降の呼び出しが別のメソッドに落ちる。</para></summary>
[GeneratedComInterface]
[Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8")]
internal partial interface IFileOperation
{
    [PreserveSig] int Advise(nint pfops, out uint pdwCookie);
    [PreserveSig] int Unadvise(uint dwCookie);
    [PreserveSig] int SetOperationFlags(uint dwOperationFlags);
    [PreserveSig] int SetProgressMessage(nint pszMessage);
    [PreserveSig] int SetProgressDialog(nint popd);
    [PreserveSig] int SetProperties(nint pproparray);
    [PreserveSig] int SetOwnerWindow(nint hwndOwner);
    [PreserveSig] int ApplyPropertiesToItem(nint psiItem);
    [PreserveSig] int ApplyPropertiesToItems(nint punkItems);
    [PreserveSig] int RenameItem(nint psiItem, nint pszNewName, nint pfopsItem);
    [PreserveSig] int RenameItems(nint pUnkItems, nint pszNewName);
    [PreserveSig] int MoveItem(nint psiItem, nint psiDestinationFolder, nint pszNewName, nint pfopsItem);
    [PreserveSig] int MoveItems(nint punkItems, nint psiDestinationFolder);
    [PreserveSig] int CopyItem(nint psiItem, nint psiDestinationFolder, nint pszCopyName, nint pfopsItem);
    [PreserveSig] int CopyItems(nint punkItems, nint psiDestinationFolder);
    [PreserveSig] int DeleteItem(nint psiItem, nint pfopsItem);
    [PreserveSig] int DeleteItems(nint punkItems);
    [PreserveSig] int NewItem(nint psiDestinationFolder, uint dwFileAttributes, nint pszName, nint pszTemplateName, nint pfopsItem);
    [PreserveSig] int PerformOperations();
    [PreserveSig] int GetAnyOperationsAborted(nint pfAnyOperationsAborted);
}

internal static partial class NativeMethods
{
    // --- IFileOperation ---

    internal const uint FOF_RENAMEONCOLLISION = 0x0008;
    internal const uint FOF_ALLOWUNDO = 0x0040;
    internal const uint CLSCTX_ALL = 0x17;

    internal static readonly Guid CLSID_FileOperation = new("3AD05575-8857-4850-9277-11B85BDB8E09");
    internal static readonly Guid IID_IFileOperation = new("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8");
    internal static readonly Guid IID_IShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    [LibraryImport("ole32.dll")]
    internal static partial int CoCreateInstance(in Guid rclsid, nint pUnkOuter, uint dwClsContext,
        in Guid riid, out nint ppv);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int SHCreateItemFromParsingName(string pszPath, nint pbc, in Guid riid, out nint ppv);

    // --- クリップボード（CF_HDROP） ---

    internal const uint CF_HDROP = 15;
    internal const uint GMEM_MOVEABLE = 0x0002;

    /// <summary>エクスプローラー準拠。コピーは <c>COPY | LINK</c>、切り取りは <c>MOVE</c>。</summary>
    internal const uint DROPEFFECT_COPY = 1;
    internal const uint DROPEFFECT_MOVE = 2;
    internal const uint DROPEFFECT_LINK = 4;

    [LibraryImport("user32.dll", EntryPoint = "OpenClipboard")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool OpenClipboard(nint hWndNewOwner);

    [LibraryImport("user32.dll", EntryPoint = "CloseClipboard")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseClipboard();

    [LibraryImport("user32.dll", EntryPoint = "EmptyClipboard")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EmptyClipboard();

    [LibraryImport("user32.dll", EntryPoint = "SetClipboardData")]
    internal static partial nint SetClipboardData(uint format, nint handle);

    [LibraryImport("user32.dll", EntryPoint = "GetClipboardData")]
    internal static partial nint GetClipboardData(uint format);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClipboardFormatW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint RegisterClipboardFormatW(string format);

    [LibraryImport("shell32.dll", EntryPoint = "DragQueryFileW")]
    internal static partial uint DragQueryFileW(nint hDrop, uint iFile, nint lpszFile, uint cch);

    [LibraryImport("kernel32.dll", EntryPoint = "GlobalAlloc")]
    internal static partial nint GlobalAlloc(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll", EntryPoint = "GlobalLock")]
    internal static partial nint GlobalLock(nint handle);

    [LibraryImport("kernel32.dll", EntryPoint = "GlobalUnlock")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GlobalUnlock(nint handle);

    [LibraryImport("kernel32.dll", EntryPoint = "GlobalFree")]
    internal static partial nint GlobalFree(nint handle);
}
