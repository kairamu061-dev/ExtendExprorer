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
    internal const int VK_CONTROL = 0x11;
    internal const int VK_TAB = 0x09;
    internal const int VK_SHIFT = 0x10;
    internal const int VK_T = 0x54;
    internal const int VK_W = 0x57;
    internal const int VK_G = 0x47;
    internal const int VK_H = 0x48;
    internal const int VK_V = 0x56;
    internal const int VK_C = 0x43;
    internal const int VK_X = 0x58;
    internal const int VK_DELETE = 0x2E;
    internal const int VK_F2 = 0x71;
    internal const int VK_F5 = 0x74;

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

    /// <summary>大きさが変わったら<b>窓の全体</b>を描き直す。右端に寄せて描くもの
    /// （分割ボタン等）は、広げた分だけしか無効化されないと古い絵が残る（BUG-023）。</summary>
    internal const uint CS_VREDRAW = 0x0001;
    internal const uint CS_HREDRAW = 0x0002;

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

        public readonly bool Contains(POINT point) =>
            point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;
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

    /// <summary>コントロールのウィンドウプロシージャを差し替える（サブクラス化）。
    /// 元のプロシージャは自分で覚えておき、<see cref="CallWindowProcW"/> で呼び戻す。</summary>
    internal const int GWLP_WNDPROC = -4;

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    internal static partial nint SetWindowLongPtrW(nint hwnd, int index, nint value);

    [LibraryImport("user32.dll", EntryPoint = "CallWindowProcW")]
    internal static partial nint CallWindowProcW(nint prevProc, nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetFocus")]
    internal static partial nint GetFocus();

    [LibraryImport("user32.dll", EntryPoint = "GetParent")]
    internal static partial nint GetParent(nint hwnd);

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

    // --- 自前描画（タブ帯） ---

    internal const int WM_PAINT = 0x000F;
    internal const int WM_ERASEBKGND = 0x0014;
    internal const int WM_LBUTTONDOWN = 0x0201;
    internal const int WM_MBUTTONUP = 0x0208;
    internal const int WM_RBUTTONUP = 0x0205;
    internal const int WM_COMMAND = 0x0111;
    internal const int WM_MOUSEMOVE = 0x0200;
    internal const int WM_LBUTTONUP = 0x0202;
    internal const int WM_SETCURSOR = 0x0020;
    internal const int WM_CAPTURECHANGED = 0x0215;

    internal const int IDC_SIZENS = 32645;
    internal const int IDC_SIZEWE = 32644;

    [LibraryImport("user32.dll", EntryPoint = "SetCursor")]
    internal static partial nint SetCursor(nint cursor);

    [LibraryImport("user32.dll", EntryPoint = "SetCapture")]
    internal static partial nint SetCapture(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "ReleaseCapture")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ReleaseCapture();

    [LibraryImport("user32.dll", EntryPoint = "ScreenToClient")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ScreenToClient(nint hwnd, ref POINT point);

    internal const int COLOR_BTNFACE = 15;
    internal const int COLOR_BTNSHADOW = 16;
    internal const int COLOR_GRAYTEXT = 17;

    internal const int TRANSPARENT = 1;

    internal const uint DT_SINGLELINE = 0x00000020;
    internal const uint DT_CENTER = 0x00000001;
    internal const uint DT_VCENTER = 0x00000004;
    internal const uint DT_END_ELLIPSIS = 0x00008000;
    internal const uint DT_CALCRECT = 0x00000400;
    internal const uint DT_NOPREFIX = 0x00000800;

    [StructLayout(LayoutKind.Sequential)]
    internal struct PAINTSTRUCT
    {
        public nint hdc;
        public int fErase;
        public RECT rcPaint;
        public int fRestore;
        public int fIncUpdate;
        public long rgbReserved1;
        public long rgbReserved2;
        public long rgbReserved3;
        public long rgbReserved4;
    }

    [LibraryImport("user32.dll", EntryPoint = "BeginPaint")]
    internal static partial nint BeginPaint(nint hwnd, out PAINTSTRUCT ps);

    [LibraryImport("user32.dll", EntryPoint = "EndPaint")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EndPaint(nint hwnd, in PAINTSTRUCT ps);

    [LibraryImport("user32.dll", EntryPoint = "GetDC")]
    internal static partial nint GetDC(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "ReleaseDC")]
    internal static partial int ReleaseDC(nint hwnd, nint hdc);

    [LibraryImport("user32.dll", EntryPoint = "FillRect")]
    internal static partial int FillRect(nint hdc, in RECT rect, nint brush);

    [LibraryImport("user32.dll", EntryPoint = "FrameRect")]
    internal static partial int FrameRect(nint hdc, in RECT rect, nint brush);

    [LibraryImport("user32.dll", EntryPoint = "DrawTextW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int DrawTextW(nint hdc, string text, int length, ref RECT rect, uint format);

    [LibraryImport("gdi32.dll", EntryPoint = "SelectObject")]
    internal static partial nint SelectObject(nint hdc, nint obj);

    [LibraryImport("gdi32.dll", EntryPoint = "SetBkMode")]
    internal static partial int SetBkMode(nint hdc, int mode);

    /// <summary>枠線を描かせないためのペン（<see cref="Polygon"/> の輪郭を消す）。</summary>
    internal const int NULL_PEN = 8;

    [LibraryImport("gdi32.dll", EntryPoint = "GetStockObject")]
    internal static partial nint GetStockObject(int index);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateSolidBrush")]
    internal static partial nint CreateSolidBrush(uint color);

    /// <summary>塗りつぶした多角形。シェブロン（開閉ボタンの三角）はこれで描く。
    /// グリフ用のフォントを別に作らずに済み、DPI に合わせて座標を計算するだけでよい。</summary>
    [LibraryImport("gdi32.dll", EntryPoint = "Polygon")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool Polygon(nint hdc, POINT* points, int count);

    // --- ホバー（マウスが離れたことを知る） ---

    internal const int WM_MOUSELEAVE = 0x02A3;
    internal const uint TME_LEAVE = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    internal struct TRACKMOUSEEVENT
    {
        public uint cbSize;
        public uint dwFlags;
        public nint hwndTrack;
        public uint dwHoverTime;
    }

    /// <summary>「マウスが出ていった」通知（<c>WM_MOUSELEAVE</c>）を 1 回だけ予約する。
    /// これを呼ばないとホバーの解除が来ず、強調が出たままになる。</summary>
    [LibraryImport("user32.dll", EntryPoint = "TrackMouseEvent")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TrackMouseEvent(ref TRACKMOUSEEVENT track);

    // --- パス入力（アドレスバーの編集モード） ---

    internal const string WC_EDIT = "EDIT";
    internal const uint ES_AUTOHSCROLL = 0x0080;
    internal const uint EM_SETSEL = 0x00B1;
    internal const int EN_KILLFOCUS = 0x0200;
    internal const int VK_RETURN = 0x0D;
    internal const int VK_ESCAPE = 0x1B;

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    internal static unsafe partial int GetWindowTextW(nint hwnd, char* text, int max);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    internal static partial int GetWindowTextLengthW(nint hwnd);

    // --- タイマー（「パスが見つかりません」を数秒で消す） ---

    internal const int WM_TIMER = 0x0113;

    [LibraryImport("user32.dll", EntryPoint = "SetTimer")]
    internal static partial nint SetTimer(nint hwnd, nint id, uint elapseMs, nint callback);

    [LibraryImport("user32.dll", EntryPoint = "KillTimer")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool KillTimer(nint hwnd, nint id);

    // --- 右クリックメニュー ---

    internal const uint MF_STRING = 0x0000;
    internal const uint MF_SEPARATOR = 0x0800;
    internal const uint MF_GRAYED = 0x0001;
    internal const uint TPM_RIGHTBUTTON = 0x0002;
    internal const uint TPM_RETURNCMD = 0x0100;

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll", EntryPoint = "CreatePopupMenu")]
    internal static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", EntryPoint = "AppendMenuW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AppendMenuW(nint menu, uint flags, nint id, string? item);

    [LibraryImport("user32.dll", EntryPoint = "TrackPopupMenu")]
    internal static partial int TrackPopupMenu(nint menu, uint flags, int x, int y,
        int reserved, nint hwnd, nint rect);

    [LibraryImport("user32.dll", EntryPoint = "DestroyMenu")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyMenu(nint menu);

    [LibraryImport("user32.dll", EntryPoint = "GetCursorPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(out POINT point);

    [LibraryImport("user32.dll", EntryPoint = "ClientToScreen")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ClientToScreen(nint hwnd, ref POINT point);

    /// <summary>lParam に載っているマウス座標（クライアント座標）。</summary>
    internal static POINT PointOf(nint lParam) => new()
    {
        X = (short)(lParam & 0xFFFF),
        Y = (short)((lParam >> 16) & 0xFFFF),
    };

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

    internal const int WM_GETFONT = 0x0031;

    /// <summary>イメージリストの絵の大きさ。借りているのが小アイコン（16x16）の
    /// 一覧かどうかを、実機のログで確かめるために使う。</summary>
    [LibraryImport("comctl32.dll", EntryPoint = "ImageList_GetIconSize")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ImageList_GetIconSize(nint imageList, out int cx, out int cy);

    [LibraryImport("comctl32.dll", EntryPoint = "InitCommonControlsEx")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool InitCommonControlsEx(ref INITCOMMONCONTROLSEX icc);

    /// <summary>96dpi 基準の値を、そのウィンドウの DPI に合わせる。</summary>
    internal static int Scale(int value, uint dpi) => (int)Math.Round(value * dpi / 96.0);
}
