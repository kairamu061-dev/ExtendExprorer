using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static ExtendExprorer.Interop.Win32;

namespace ExtendExprorer.UI;

/// <summary>ウィンドウ最上段の細い帯。いまはフォルダツリーの開閉ボタンだけを載せる。
///
/// <para><b>なぜ帯を作るか。</b>「開いているときも閉じているときも同じ場所にボタンがある」
/// ことが要件で、ツリー自身のヘッダに置くと閉じたときにボタンごと消える
/// （旧版で 28px の名残が残っていたのはこれを避けるためだった）。
/// 本物のタイトルバー（キャプション）に載せるには、DWM の描く枠を自前に置き換えて
/// 最小化・最大化・閉じるとタイトル文字まで自分で描くことになるため、
/// クライアント領域の最上段に独立した帯を設けた。</para></summary>
internal sealed unsafe class ChromeBar
{
    private const string ClassName = "ExtendExprorer.ChromeBar";

    /// <summary>帯の高さ（96dpi 基準）。</summary>
    internal const int Height = 26;

    /// <summary>ボタンの一辺（96dpi 基準）。帯の高さから上下 2px ずつ空ける。</summary>
    private const int ButtonSize = 22;

    private const int ButtonMargin = 2;

    private static readonly Dictionary<nint, ChromeBar> Bars = [];
    private static bool _classRegistered;

    private nint _hwnd;
    private uint _dpi = 96;
    private bool _hot;
    private bool _tracking;

    /// <summary>ツリーが閉じているか。矢印の向きに使う。</summary>
    internal bool Collapsed { get; set; }

    /// <summary>開閉ボタンが押された。</summary>
    internal event Action? ToggleRequested;

    internal void Create(nint parent, nint instance, RECT bounds, uint dpi)
    {
        _dpi = dpi;
        RegisterClass(instance);
        _hwnd = CreateWindowExW(0, ClassName, null, WS_CHILD | WS_VISIBLE,
            bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            parent, 0, instance, 0);
        if (_hwnd == 0)
        {
            throw new InvalidOperationException($"CreateWindowEx({ClassName}) failed: {Marshal.GetLastPInvokeError()}");
        }
        Bars[_hwnd] = this;
    }

    internal void SetBounds(RECT bounds)
    {
        if (_hwnd != 0)
        {
            MoveWindow(_hwnd, bounds.Left, bounds.Top, bounds.Width, bounds.Height, repaint: true);
        }
    }

    internal void SetDpi(uint dpi)
    {
        _dpi = dpi;
        Invalidate();
    }

    internal void Invalidate()
    {
        if (_hwnd != 0)
        {
            InvalidateRect(_hwnd, 0, erase: true);
        }
    }

    private static void RegisterClass(nint instance)
    {
        if (_classRegistered)
        {
            return;
        }
        fixed (char* className = ClassName)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                // 幅が変わったら全体を描き直す。右端に寄せて描くものがあるので、
                // 広がった分だけの無効化では古い絵が残る（BUG-023）
                style = CS_HREDRAW | CS_VREDRAW,
                lpfnWndProc = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&WndProc,
                hInstance = instance,
                hCursor = LoadCursorW(0, IDC_ARROW),
                hbrBackground = 0, // WM_ERASEBKGND で自分で塗る
                lpszClassName = (nint)className,
            };
            if (RegisterClassExW(ref wc) == 0)
            {
                throw new InvalidOperationException($"RegisterClassEx({ClassName}) failed: {Marshal.GetLastPInvokeError()}");
            }
        }
        _classRegistered = true;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            if (!Bars.TryGetValue(hwnd, out var bar))
            {
                return DefWindowProcW(hwnd, msg, wParam, lParam);
            }
            return bar.HandleMessage(hwnd, msg, wParam, lParam);
        }
        catch (Exception ex)
        {
            Diagnostics.Report($"ChromeBar.WndProc(0x{msg:X4})", ex);
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }
    }

    private nint HandleMessage(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_ERASEBKGND:
                return 1; // WM_PAINT で全面を塗るので、ここで塗るとちらつく

            case WM_PAINT:
                Paint();
                return 0;

            case WM_MOUSEMOVE:
                OnMouseMove(hwnd, PointOf(lParam));
                return 0;

            case WM_MOUSELEAVE:
                _tracking = false;
                SetHot(false);
                return 0;

            case WM_LBUTTONDOWN:
                if (ButtonBounds().Contains(PointOf(lParam)))
                {
                    ToggleRequested?.Invoke();
                }
                return 0;

            case WM_DESTROY:
                Bars.Remove(hwnd);
                return 0;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void OnMouseMove(nint hwnd, POINT point)
    {
        if (!_tracking)
        {
            // これを予約しないと「出ていった」通知が来ず、強調が出たままになる
            var track = new TRACKMOUSEEVENT
            {
                cbSize = (uint)sizeof(TRACKMOUSEEVENT),
                dwFlags = TME_LEAVE,
                hwndTrack = hwnd,
            };
            _tracking = TrackMouseEvent(ref track);
        }
        SetHot(ButtonBounds().Contains(point));
    }

    private void SetHot(bool hot)
    {
        if (_hot != hot)
        {
            _hot = hot;
            Invalidate();
        }
    }

    private RECT ButtonBounds()
    {
        var margin = Scale(ButtonMargin, _dpi);
        var size = Scale(ButtonSize, _dpi);
        return new RECT { Left = margin, Top = margin, Right = margin + size, Bottom = margin + size };
    }

    private void Paint()
    {
        var hdc = BeginPaint(_hwnd, out var ps);
        try
        {
            if (!GetClientRect(_hwnd, out var client))
            {
                return;
            }
            // 帯とボタン。配色はタブ帯・ツールバーと同じ（サンプルツール準拠の #F0F0F0）
            Fill(hdc, client, BandColor);
            var border = client with { Top = client.Bottom - 1 };
            Fill(hdc, border, BorderColor);

            var button = ButtonBounds();
            if (_hot)
            {
                Fill(hdc, button, HotColor);
            }
            DrawChevron(hdc, button);
        }
        finally
        {
            EndPaint(_hwnd, in ps);
        }
    }

    /// <summary>開いているときは「◀」（閉じる向き）、閉じているときは「▶」。
    /// グリフ用のフォントを持たずに済むよう、三角は座標で描く。</summary>
    private void DrawChevron(nint hdc, RECT button)
    {
        var centerX = (button.Left + button.Right) / 2;
        var centerY = (button.Top + button.Bottom) / 2;
        var half = Math.Max(2, Scale(4, _dpi));
        var reach = Math.Max(1, Scale(3, _dpi));
        var tip = Collapsed ? centerX + reach : centerX - reach;
        var back = Collapsed ? centerX - reach : centerX + reach;

        var points = stackalloc POINT[3];
        points[0] = new POINT { X = tip, Y = centerY };
        points[1] = new POINT { X = back, Y = centerY - half };
        points[2] = new POINT { X = back, Y = centerY + half };

        var brush = CreateSolidBrush(GetSysColor(COLOR_WINDOWTEXT));
        var pen = GetStockObject(NULL_PEN);
        var oldBrush = SelectObject(hdc, brush);
        var oldPen = SelectObject(hdc, pen);
        Polygon(hdc, points, 3);
        SelectObject(hdc, oldPen);
        SelectObject(hdc, oldBrush);
        DeleteObject(brush);
    }

    private static void Fill(nint hdc, RECT rect, uint color)
    {
        var brush = CreateSolidBrush(color);
        FillRect(hdc, in rect, brush);
        DeleteObject(brush);
    }

    private const uint BandColor = 0x00F0F0F0;
    private const uint BorderColor = 0x00D0D0D0;
    private const uint HotColor = 0x00FBF1E5; // #E5F1FB を COLORREF（BGR）で
}
