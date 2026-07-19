using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace ExtendExprorer.Interop;

// Native AOT 互換のため、COM は .NET 8 のソース生成([GeneratedComInterface])のみを使う。
// 組み込みの COM 相互運用([ComImport]や Marshal.GetObjectForIUnknown)は AOT 非対応(BUG-001 と同方針)。
// vtable 順は shobjidl_core.h に厳密一致させること。呼ばないメソッドも省略してはならない。

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X;
    public int Y;
}

/// <summary>SHFILEINFOW (shellapi.h)。shell-icons のアイコン取得用。</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct SHFILEINFOW
{
    public nint hIcon;
    public int iIcon;
    public uint dwAttributes;
    public fixed char szDisplayName[260];
    public fixed char szTypeName[80];
}

[StructLayout(LayoutKind.Sequential)]
internal struct ICONINFO
{
    public int fIcon;
    public int xHotspot;
    public int yHotspot;
    public nint hbmMask;
    public nint hbmColor;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAP
{
    public int bmType;
    public int bmWidth;
    public int bmHeight;
    public int bmWidthBytes;
    public ushort bmPlanes;
    public ushort bmBitsPixel;
    public nint bmBits;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFOHEADER
{
    public int biSize;
    public int biWidth;
    public int biHeight;
    public ushort biPlanes;
    public ushort biBitCount;
    public uint biCompression;
    public uint biSizeImage;
    public int biXPelsPerMeter;
    public int biYPelsPerMeter;
    public uint biClrUsed;
    public uint biClrImportant;
}

/// <summary>SHELLEXECUTEINFOW (shellapi.h)。BUG-004: PIDL ベース起動用。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ShellExecuteInfoW
{
    public int cbSize;
    public uint fMask;
    public nint hwnd;
    public nint lpVerb;
    public nint lpFile;
    public nint lpParameters;
    public nint lpDirectory;
    public int nShow;
    public nint hInstApp;
    public nint lpIDList;
    public nint lpClass;
    public nint hkeyClass;
    public uint dwHotKey;
    public nint hIconOrMonitor;
    public nint hProcess;
}

/// <summary>CMINVOKECOMMANDINFOEX (shobjidl_core.h)。全フィールド blittable。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct InvokeCommandInfoEx
{
    public int cbSize;
    public int fMask;
    public nint hwnd;
    public nint lpVerb;
    public nint lpParameters;
    public nint lpDirectory;
    public int nShow;
    public int dwHotKey;
    public nint hIcon;
    public nint lpTitle;
    public nint lpVerbW;
    public nint lpParametersW;
    public nint lpDirectoryW;
    public nint lpTitleW;
    public POINT ptInvoke;
}

[GeneratedComInterface]
[Guid("000214E6-0000-0000-C000-000000000046")]
internal partial interface IShellFolder
{
    // 使わないメソッドも vtable スロット確保のため全宣言する（文字列等は nint で受ける）
    [PreserveSig] int ParseDisplayName(nint hwnd, nint pbc, nint pszDisplayName, nint pchEaten, nint ppidl, nint pdwAttributes);
    [PreserveSig] int EnumObjects(nint hwnd, uint grfFlags, out nint ppenumIDList);
    [PreserveSig] int BindToObject(nint pidl, nint pbc, in Guid riid, out nint ppv);
    [PreserveSig] int BindToStorage(nint pidl, nint pbc, in Guid riid, out nint ppv);
    [PreserveSig] int CompareIDs(nint lParam, nint pidl1, nint pidl2);
    [PreserveSig] int CreateViewObject(nint hwndOwner, in Guid riid, out nint ppv);
    // apidl は子 PIDL 配列の先頭ポインタ（複数選択のため raw pointer で受ける）
    [PreserveSig] int GetAttributesOf(uint cidl, nint apidl, ref uint rgfInOut);
    [PreserveSig] int GetUIObjectOf(nint hwndOwner, uint cidl, nint apidl, in Guid riid, nint rgfReserved, out nint ppv);
    [PreserveSig] int GetDisplayNameOf(nint pidl, uint uFlags, nint pName);
    [PreserveSig] int SetNameOf(nint hwnd, nint pidl, nint pszName, uint uFlags, out nint ppidlOut);
}

[GeneratedComInterface]
[Guid("000214E4-0000-0000-C000-000000000046")]
internal partial interface IContextMenu
{
    [PreserveSig] int QueryContextMenu(nint hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
    [PreserveSig] int InvokeCommand(nint pici);
    [PreserveSig] int GetCommandString(nuint idCmd, uint uType, nint pReserved, nint pszName, uint cchMax);
}

/// <summary>「送る」等の遅延生成サブメニューのメッセージ転送用。</summary>
[GeneratedComInterface]
[Guid("000214F4-0000-0000-C000-000000000046")]
internal partial interface IContextMenu2 : IContextMenu
{
    [PreserveSig] int HandleMenuMsg(uint uMsg, nint wParam, nint lParam);
}

[GeneratedComInterface]
[Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719")]
internal partial interface IContextMenu3 : IContextMenu2
{
    [PreserveSig] int HandleMenuMsg2(uint uMsg, nint wParam, nint lParam, out nint plResult);
}

/// <summary>ファイルのコピー/移動/削除（貼り付け・ショートカット・D&amp;D 共用。BUG-005）。
/// エクスプローラーと同じ進捗・衝突ダイアログ・ごみ箱・自動リネームをシェルに任せる。
/// IShellItem 等のポインタは呼び出すだけで中身に触れないため nint で受ける。</summary>
[GeneratedComInterface]
[Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8")]
internal partial interface IFileOperation
{
    // 使わないメソッドも vtable スロット確保のため全宣言する
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

internal static unsafe partial class NativeMethods
{
    internal const uint TPM_RETURNCMD = 0x0100;
    internal const uint TPM_RIGHTBUTTON = 0x0002;
    internal const int CMIC_MASK_PTINVOKE = 0x20000000;
    internal const uint WM_INITMENUPOPUP = 0x0117;
    internal const uint WM_DRAWITEM = 0x002B;
    internal const uint WM_MEASUREITEM = 0x002C;
    internal const uint WM_MENUCHAR = 0x0120;
    internal const int SW_SHOWNORMAL = 1;
    internal const uint CF_HDROP = 15;
    internal const uint MF_STRING = 0x0000;
    internal const uint MF_GRAYED = 0x0001;
    internal const uint MF_BYPOSITION = 0x0400;
    internal const uint MF_SEPARATOR = 0x0800;
    internal const uint DROPEFFECT_COPY = 1;

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int SHParseDisplayName(string pszName, nint pbc, out nint ppidl, uint sfgaoIn, out uint psfgaoOut);

    [LibraryImport("shell32.dll")]
    internal static partial int SHBindToParent(nint pidl, in Guid riid, out nint ppv, out nint ppidlLast);

    [LibraryImport("user32.dll")]
    internal static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyMenu(nint hMenu);

    [LibraryImport("user32.dll")]
    internal static partial int TrackPopupMenuEx(nint hMenu, uint uFlags, int x, int y, nint hwnd, nint lptpm);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(out POINT lpPoint);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowSubclass(
        nint hWnd,
        delegate* unmanaged<nint, uint, nint, nint, nuint, nuint, nint> pfnSubclass,
        nuint uIdSubclass,
        nuint dwRefData);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RemoveWindowSubclass(
        nint hWnd,
        delegate* unmanaged<nint, uint, nint, nint, nuint, nuint, nint> pfnSubclass,
        nuint uIdSubclass);

    [LibraryImport("comctl32.dll")]
    internal static partial nint DefSubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool InsertMenuW(nint hMenu, uint uPosition, uint uFlags, nuint uIDNewItem, string? lpNewItem);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsClipboardFormatAvailable(uint format);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint RegisterClipboardFormatW(string lpszFormat);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool OpenClipboard(nint hWndNewOwner);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseClipboard();

    [LibraryImport("user32.dll")]
    internal static partial nint GetClipboardData(uint uFormat);

    [LibraryImport("kernel32.dll")]
    internal static partial nint GlobalLock(nint hMem);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GlobalUnlock(nint hMem);

    [LibraryImport("ole32.dll")]
    internal static partial int OleGetClipboard(out nint ppDataObj);

    // ---- ファイル操作基盤（IFileOperation / クリップボード書き込み） ----

    internal const uint FOF_RENAMEONCOLLISION = 0x0008;
    internal const uint FOF_ALLOWUNDO = 0x0040;
    internal const uint CLSCTX_ALL = 0x17;
    internal const uint GMEM_MOVEABLE = 0x0002;
    internal const uint DROPEFFECT_MOVE = 2;
    internal const uint DROPEFFECT_LINK = 4;

    internal static readonly Guid CLSID_FileOperation = new("3AD05575-8857-4850-9277-11B85BDB8E09");
    internal static readonly Guid IID_IFileOperation = new("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8");
    internal static readonly Guid IID_IShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    [LibraryImport("ole32.dll")]
    internal static partial int CoCreateInstance(in Guid rclsid, nint pUnkOuter, uint dwClsContext, in Guid riid, out nint ppv);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int SHCreateItemFromParsingName(string pszPath, nint pbc, in Guid riid, out nint ppv);

    /// <summary>iFile=0xFFFFFFFF でファイル数を返す。lpszFile はワイド文字バッファ。</summary>
    [LibraryImport("shell32.dll")]
    internal static partial uint DragQueryFileW(nint hDrop, uint iFile, nint lpszFile, uint cch);

    [LibraryImport("kernel32.dll")]
    internal static partial nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [LibraryImport("kernel32.dll")]
    internal static partial nint GlobalFree(nint hMem);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EmptyClipboard();

    [LibraryImport("user32.dll")]
    internal static partial nint SetClipboardData(uint uFormat, nint hMem);

    /// <summary>OS 設定のダブルクリック間隔(ms)。リネーム開始の待ち時間に使う。</summary>
    [LibraryImport("user32.dll")]
    internal static partial uint GetDoubleClickTime();

    /// <summary>ファイルを既定アプリで開く（file-list のダブルクリック用）。
    /// パス文字列版 ShellExecuteW は既定 verb を解決できず openas 化した(BUG-004)ため、
    /// PIDL＋SEE_MASK_INVOKEIDLIST（エクスプローラーのダブルクリックと同じ経路）を使う。</summary>
    internal const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;

    [LibraryImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShellExecuteExW(ref ShellExecuteInfoW pExecInfo);

    // ---- shell-icons ----

    internal const uint SHGFI_ICON = 0x100;
    internal const uint SHGFI_SMALLICON = 0x1;
    internal const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    internal const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    internal const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    internal const uint DIB_RGB_COLORS = 0;

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint SHGetFileInfoW(string pszPath, uint dwFileAttributes, ref SHFILEINFOW psfi, uint cbFileInfo, uint uFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyIcon(nint hIcon);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetIconInfo(nint hIcon, out ICONINFO piconinfo);

    [LibraryImport("user32.dll")]
    internal static partial nint GetDC(nint hWnd);

    [LibraryImport("user32.dll")]
    internal static partial int ReleaseDC(nint hWnd, nint hDC);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteObject(nint ho);

    [LibraryImport("gdi32.dll")]
    internal static partial int GetObjectW(nint h, int c, nint pv);

    [LibraryImport("gdi32.dll")]
    internal static partial int GetDIBits(nint hdc, nint hbm, uint start, uint cLines, nint lpvBits, nint lpbmi, uint usage);
}
