using System.Runtime.InteropServices;

namespace ExtendExprorer.Interop;

/// <summary>ウィンドウとメッセージループまわりの Win32 宣言。
/// <para>宣言は <c>LibraryImport</c>（ソース生成マーシャラ）で書く。Native AOT ではリフレクションに
/// 頼る <c>DllImport</c> の一部機能が使えないため、構造体は blittable に保つこと
/// （<c>string</c> フィールドを持たせると <c>sizeof</c> がネイティブの大きさを返さなくなる）。</para></summary>
internal static partial class Win32
{
    internal const int WM_CREATE = 0x0001;
    internal const int WM_DESTROY = 0x0002;
    internal const int WM_SIZE = 0x0005;
    internal const int WM_SETFOCUS = 0x0007;
    internal const int WM_CLOSE = 0x0010;
    internal const int WM_SETFONT = 0x0030;
    internal const int WM_NOTIFY = 0x004E;
    internal const int WM_DPICHANGED = 0x02E0;
    internal const int WM_GETMINMAXINFO = 0x0024;
    internal const int WM_KEYDOWN = 0x0100;
    internal const int WM_SYSKEYDOWN = 0x0104;
    internal const int WM_APPCOMMAND = 0x0319;
    internal const int WM_APP = 0x8000;

    internal const int VK_BACK = 0x08;
    internal const int VK_LEFT = 0x25;
    internal const int VK_UP = 0x26;
    internal const int VK_RIGHT = 0x27;
    internal const int VK_MENU = 0x12;

    /// <summary>マウスの「戻る」「進む」ボタン。子（一覧）で処理されなかった
    /// <c>WM_APPCOMMAND</c> は <c>DefWindowProc</c> が親へ送り上げてくる。</summary>
    internal const int APPCOMMAND_BROWSER_BACKWARD = 1;
    internal const int APPCOMMAND_BROWSER_FORWARD = 2;

    internal const uint WS_CHILD = 0x40000000;
    internal const uint WS_VISIBLE = 0x10000000;
    internal const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
    internal const uint WS_CLIPCHILDREN = 0x02000000;
    internal const uint WS_TABSTOP = 0x00010000;
    internal const uint WS_BORDER = 0x00800000;
    internal const int CW_USEDEFAULT = unchecked((int)0x80000000);

    internal const int SW_SHOW = 5;
    internal const int SW_SHOWNORMAL = 1;

    internal const int COLOR_WINDOW = 5;
    internal const int IDC_ARROW = 32512;

    internal const int SW_HIDE = 0;
    internal const int WM_CTLCOLORSTATIC = 0x0138;

    internal const string WC_STATIC = "STATIC";
    internal const uint SS_CENTER = 0x00000001;
    /// <summary>1 行のテキストを上下中央に置く。</summary>
    internal const uint SS_CENTERIMAGE = 0x00000200;

    internal const uint ICC_LISTVIEW_CLASSES = 0x00000001;
    internal const uint ICC_TREEVIEW_CLASSES = 0x00000002;
    internal const uint ICC_BAR_CLASSES = 0x00000004;

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
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct INITCOMMONCONTROLSEX
    {
        public uint dwSize;
        public uint dwICC;
    }

    internal const int LF_FACESIZE = 32;

    /// <summary>LOGFONTW。<c>lfFaceName</c> は <c>fixed</c> の固定長にして blittable に保つ。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct LOGFONTW
    {
        public int lfHeight;
        public int lfWidth;
        public int lfEscapement;
        public int lfOrientation;
        public int lfWeight;
        public byte lfItalic;
        public byte lfUnderline;
        public byte lfStrikeOut;
        public byte lfCharSet;
        public byte lfOutPrecision;
        public byte lfClipPrecision;
        public byte lfQuality;
        public byte lfPitchAndFamily;
        public fixed char lfFaceName[LF_FACESIZE];
    }

    /// <summary>NONCLIENTMETRICSW。<c>lfMessageFont</c> が「UI の既定フォント」で、
    /// これを一覧やツリーに設定しないと comctl32 の既定（古いビットマップフォント）のままになる。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NONCLIENTMETRICSW
    {
        public uint cbSize;
        public int iBorderWidth;
        public int iScrollWidth;
        public int iScrollHeight;
        public int iCaptionWidth;
        public int iCaptionHeight;
        public LOGFONTW lfCaptionFont;
        public int iSmCaptionWidth;
        public int iSmCaptionHeight;
        public LOGFONTW lfSmCaptionFont;
        public int iMenuWidth;
        public int iMenuHeight;
        public LOGFONTW lfMenuFont;
        public LOGFONTW lfStatusFont;
        public LOGFONTW lfMessageFont;
        public int iPaddedBorderWidth;
    }

    internal const uint SPI_GETNONCLIENTMETRICS = 0x0029;

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW")]
    internal static partial ushort RegisterClassExW(ref WNDCLASSEXW wc);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateWindowExW(
        uint exStyle, string className, string? windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    internal static partial nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "DestroyWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "ShowWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint hwnd, int cmdShow);

    [LibraryImport("user32.dll", EntryPoint = "UpdateWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UpdateWindow(nint hwnd);

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
    internal static partial bool MoveWindow(nint hwnd, int x, int y, int w, int h,
        [MarshalAs(UnmanagedType.Bool)] bool repaint);

    [LibraryImport("user32.dll", EntryPoint = "GetClientRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetClientRect(nint hwnd, out RECT rect);

    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")]
    internal static partial nint LoadCursorW(nint instance, nint cursorName);

    [LibraryImport("user32.dll", EntryPoint = "GetDpiForWindow")]
    internal static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowTextW(nint hwnd, string text);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostMessageW(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "InvalidateRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool InvalidateRect(nint hwnd, nint rect, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [LibraryImport("user32.dll", EntryPoint = "SetFocus")]
    internal static partial nint SetFocus(nint hwnd);

    internal const uint GA_ROOT = 2;

    [LibraryImport("user32.dll", EntryPoint = "GetAncestor")]
    internal static partial nint GetAncestor(nint hwnd, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetKeyState")]
    internal static partial short GetKeyState(int virtualKey);

    internal const int COLOR_WINDOWTEXT = 8;

    [LibraryImport("user32.dll", EntryPoint = "GetSysColor")]
    internal static partial uint GetSysColor(int index);

    [LibraryImport("user32.dll", EntryPoint = "GetSysColorBrush")]
    internal static partial nint GetSysColorBrush(int index);

    [LibraryImport("gdi32.dll", EntryPoint = "SetTextColor")]
    internal static partial uint SetTextColor(nint hdc, uint color);

    [LibraryImport("gdi32.dll", EntryPoint = "SetBkColor")]
    internal static partial uint SetBkColor(nint hdc, uint color);

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SystemParametersInfoW(uint action, uint param, ref NONCLIENTMETRICSW data, uint winIni);

    /// <summary>DPI ごとのメトリクスを取る（PerMonitorV2 では画面ごとに違う）。
    /// Windows 10 1607 以降。取れないときは <see cref="SystemParametersInfoW"/> に落とす。</summary>
    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoForDpi")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SystemParametersInfoForDpi(uint action, uint param, ref NONCLIENTMETRICSW data, uint winIni, uint dpi);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateFontIndirectW")]
    internal static partial nint CreateFontIndirectW(ref LOGFONTW logFont);

    [LibraryImport("gdi32.dll", EntryPoint = "DeleteObject")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteObject(nint handle);

    /// <summary>コントロールに「Explorer」テーマを当てる。これを呼ばないと選択の塗りや
    /// ヘッダのホバーが旧来の見た目になり、エクスプローラーと並べたときに違って見える。</summary>
    [LibraryImport("uxtheme.dll", EntryPoint = "SetWindowTheme", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int SetWindowTheme(nint hwnd, string? subAppName, string? subIdList);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
    internal static partial nint GetModuleHandleW(nint moduleName);

    [LibraryImport("comctl32.dll", EntryPoint = "InitCommonControlsEx")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool InitCommonControlsEx(ref INITCOMMONCONTROLSEX icc);

    /// <summary>96dpi 基準の値を、そのウィンドウの DPI に合わせる。</summary>
    internal static int Scale(int value, uint dpi) => (int)Math.Round(value * dpi / 96.0);
}
