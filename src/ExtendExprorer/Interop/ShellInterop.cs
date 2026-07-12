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
    [PreserveSig] int GetAttributesOf(uint cidl, in nint apidl, ref uint rgfInOut);
    [PreserveSig] int GetUIObjectOf(nint hwndOwner, uint cidl, in nint apidl, in Guid riid, nint rgfReserved, out nint ppv);
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
}
