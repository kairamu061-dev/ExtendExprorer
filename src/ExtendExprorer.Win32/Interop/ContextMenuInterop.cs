using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace ExtendExprorer.Interop;

/// <summary>シェルのコンテキストメニュー（<c>IContextMenu</c>）まわり。
/// 現行 WinUI 版からそのまま移した。
///
/// <para><b>使わないメソッドも vtable の位置を合わせるために全部宣言する。</b>
/// 1 つでも抜けると、それ以降の呼び出しが別のメソッドに落ちる。</para></summary>
[GeneratedComInterface]
[Guid("000214E6-0000-0000-C000-000000000046")]
internal partial interface IShellFolder
{
    [PreserveSig] int ParseDisplayName(nint hwnd, nint pbc, nint pszDisplayName, nint pchEaten, nint ppidl, nint pdwAttributes);
    [PreserveSig] int EnumObjects(nint hwnd, uint grfFlags, out nint ppenumIDList);
    [PreserveSig] int BindToObject(nint pidl, nint pbc, in Guid riid, out nint ppv);
    [PreserveSig] int BindToStorage(nint pidl, nint pbc, in Guid riid, out nint ppv);
    [PreserveSig] int CompareIDs(nint lParam, nint pidl1, nint pidl2);
    [PreserveSig] int CreateViewObject(nint hwndOwner, in Guid riid, out nint ppv);
    // apidl は子 PIDL 配列の先頭。複数選択を渡すので生ポインタで受ける
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

/// <summary>「送る」等、開いたときに中身を作るサブメニューへのメッセージ転送用。</summary>
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

/// <summary>SHELLEXECUTEINFOW。</summary>
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

/// <summary>CMINVOKECOMMANDINFOEX。全フィールド blittable。</summary>
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
    public Win32.POINT ptInvoke;
}

internal static unsafe partial class NativeMethods
{
    internal const uint MF_BYPOSITION = 0x0400;

    /// <summary><c>QueryContextMenu</c> の印。これを渡さないと、シェルは
    /// 「名前の変更」をメニューに足さない（自前で編集を始めるアプリ向けの項目のため）。</summary>
    internal const uint CMF_NORMAL = 0x00000000;
    internal const uint CMF_CANRENAME = 0x00000010;

    /// <summary><c>GetCommandString</c> で「動詞」（<c>rename</c> 等）を聞く。</summary>
    internal const uint GCS_VERBW = 0x00000004;
    internal const int CMIC_MASK_PTINVOKE = 0x20000000;

    internal const uint WM_INITMENUPOPUP = 0x0117;
    internal const uint WM_DRAWITEM = 0x002B;
    internal const uint WM_MEASUREITEM = 0x002C;
    internal const uint WM_MENUCHAR = 0x0120;

    /// <summary>PIDL ＋ この印で「エクスプローラーのダブルクリックと同じ経路」になる。
    /// パス文字列で <c>ShellExecute</c> すると、既定のアプリがあっても
    /// 「開く方法」を聞かれることがある（旧版 BUG-004）。</summary>
    internal const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int SHParseDisplayName(string pszName, nint pbc, out nint ppidl,
        uint sfgaoIn, out uint psfgaoOut);

    [LibraryImport("shell32.dll")]
    internal static partial int SHBindToParent(nint pidl, in Guid riid, out nint ppv, out nint ppidlLast);

    [LibraryImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShellExecuteExW(ref ShellExecuteInfoW pExecInfo);

    // --- OLE の初期化 ---
    //
    // シェルの「コピー」「切り取り」は OleSetClipboard でデータを載せるので、
    // OleInitialize されていないスレッドからは黙って失敗する（例外も出ない）。
    // [STAThread] による CoInitialize だけでは足りない。D&D（第 4c 段）にも要る。

    [LibraryImport("ole32.dll", EntryPoint = "OleInitialize")]
    internal static partial int OleInitialize(nint reserved);

    [LibraryImport("ole32.dll", EntryPoint = "OleUninitialize")]
    internal static partial void OleUninitialize();

    [LibraryImport("user32.dll", EntryPoint = "TrackPopupMenuEx")]
    internal static partial int TrackPopupMenuEx(nint hMenu, uint uFlags, int x, int y, nint hwnd, nint lptpm);

    [LibraryImport("user32.dll", EntryPoint = "InsertMenuW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool InsertMenuW(nint hMenu, uint uPosition, uint uFlags,
        nuint uIDNewItem, string? lpNewItem);

    [LibraryImport("user32.dll", EntryPoint = "IsClipboardFormatAvailable")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsClipboardFormatAvailable(uint format);

    // --- メニューのサブクラス化（サブメニューの中身を作らせるためのメッセージ転送） ---

    [LibraryImport("comctl32.dll", EntryPoint = "SetWindowSubclass")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowSubclass(nint hWnd,
        delegate* unmanaged<nint, uint, nint, nint, nuint, nuint, nint> pfnSubclass,
        nuint uIdSubclass, nuint dwRefData);

    [LibraryImport("comctl32.dll", EntryPoint = "RemoveWindowSubclass")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RemoveWindowSubclass(nint hWnd,
        delegate* unmanaged<nint, uint, nint, nint, nuint, nuint, nint> pfnSubclass,
        nuint uIdSubclass);

    [LibraryImport("comctl32.dll", EntryPoint = "DefSubclassProc")]
    internal static partial nint DefSubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam);
}
