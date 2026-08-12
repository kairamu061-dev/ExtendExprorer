using System.Runtime.InteropServices;

namespace Win32Aot;

/// <summary>スパイク用の最小限の Win32 宣言。本体（ExtendExprorer）の Interop とは意図的に分けている
/// （こちらは計測が目的で、本体に取り込むかどうかは計測結果で決める）。</summary>
internal static unsafe partial class Native
{
    internal const int WM_DESTROY = 0x0002;
    internal const int WM_SIZE = 0x0005;
    internal const int WM_NOTIFY = 0x004E;

    internal const int WS_CHILD = 0x40000000;
    internal const int WS_VISIBLE = 0x10000000;
    internal const int WS_OVERLAPPEDWINDOW = 0x00CF0000;
    internal const int CW_USEDEFAULT = unchecked((int)0x80000000);

    // ListView
    internal const int LVM_FIRST = 0x1000;
    internal const int LVM_SETIMAGELIST = LVM_FIRST + 3;
    internal const int LVM_SETITEMCOUNT = LVM_FIRST + 47;
    internal const int LVM_INSERTCOLUMNW = LVM_FIRST + 97;
    internal const int LVM_SETEXTENDEDLISTVIEWSTYLE = LVM_FIRST + 54;

    internal const int LVS_REPORT = 0x0001;
    internal const int LVS_SHAREIMAGELISTS = 0x0040;
    internal const int LVS_OWNERDATA = 0x1000;
    internal const int LVS_EX_FULLROWSELECT = 0x0020;
    internal const int LVS_EX_DOUBLEBUFFER = 0x10000;
    internal const int LVSIL_SMALL = 1;

    internal const uint LVCF_FMT = 0x0001;
    internal const uint LVCF_WIDTH = 0x0002;
    internal const uint LVCF_TEXT = 0x0004;
    internal const uint LVCF_SUBITEM = 0x0008;
    internal const uint LVIF_TEXT = 0x0001;
    internal const uint LVIF_IMAGE = 0x0002;

    internal const int LVN_GETDISPINFOW = -177;

    // SHGetFileInfo
    internal const uint SHGFI_ICON = 0x000000100;
    internal const uint SHGFI_SMALLICON = 0x000000001;
    internal const uint SHGFI_SYSICONINDEX = 0x000004000;
    internal const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    internal const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
    internal const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    internal const uint ICC_LISTVIEW_CLASSES = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    internal struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public nint lpszMenuName;
        public nint lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct INITCOMMONCONTROLSEX
    {
        public uint dwSize;
        public uint dwICC;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LVCOLUMNW
    {
        public uint mask;
        public int fmt;
        public int cx;
        public nint pszText;
        public int cchTextMax;
        public int iSubItem;
        public int iImage;
        public int iOrder;
        public int cxMin;
        public int cxDefault;
        public int cxIdeal;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NMHDR
    {
        public nint hwndFrom;
        public nint idFrom;
        public int code;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LVITEMW
    {
        public uint mask;
        public int iItem;
        public int iSubItem;
        public uint state;
        public uint stateMask;
        public nint pszText;
        public int cchTextMax;
        public int iImage;
        public nint lParam;
        public int iIndent;
        public int iGroupId;
        public uint cColumns;
        public nint puColumns;
        public nint piColFmt;
        public int iGroup;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NMLVDISPINFOW
    {
        public NMHDR hdr;
        public LVITEMW item;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct SHFILEINFOW
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW")]
    internal static partial ushort RegisterClassExW(ref WNDCLASSEXW wc);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateWindowExW(
        uint exStyle, string className, string? windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    internal static partial nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "ShowWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint hwnd, int cmdShow);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    internal static partial int GetMessageW(out MSG msg, nint hwnd, uint filterMin, uint filterMax);

    [LibraryImport("user32.dll", EntryPoint = "TranslateMessage")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TranslateMessage(in MSG msg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    internal static partial nint DispatchMessageW(in MSG msg);

    [LibraryImport("user32.dll", EntryPoint = "PostQuitMessage")]
    internal static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    internal static partial nint SendMessageW(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "MoveWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool MoveWindow(nint hwnd, int x, int y, int w, int h, [MarshalAs(UnmanagedType.Bool)] bool repaint);

    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")]
    internal static partial nint LoadCursorW(nint instance, nint cursorName);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
    internal static partial nint GetModuleHandleW(nint moduleName);

    [LibraryImport("comctl32.dll", EntryPoint = "InitCommonControlsEx")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool InitCommonControlsEx(ref INITCOMMONCONTROLSEX icc);

    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode)]
    internal static extern nint SHGetFileInfoW(string path, uint attributes, ref SHFILEINFOW info, uint size, uint flags);
}
