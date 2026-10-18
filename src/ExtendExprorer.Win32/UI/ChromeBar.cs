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

    /// <summary>版数を描くための UI フォント。<b>借りているだけ</b>——
    /// 作ったのは <c>MainWindow</c> なので、ここで壊さないこと。
    ///
    /// <para>この帯は元々フォントを持っていなかった（矢印は座標で描くので要らなかった）。
    /// 版数の文字を出すために足した。</para></summary>
    private nint _font;

    /// <summary>ツリーが閉じているか。矢印の向きに使う。</summary>
    internal bool Collapsed { get; set; }

    /// <summary>開閉ボタンが押された。</summary>
    internal event Action? ToggleRequested;

    internal void Create(nint parent, nint instance, RECT bounds, nint font, uint dpi)
    {
        _font = font;
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
            MoveWindowNoCopy(_hwnd, bounds);
        }
    }

    internal void SetDpi(nint font, uint dpi)
    {
        // ★ フォントも受け直す。DPI が変わると MainWindow が作り直すので、
        //   古いハンドルを持ち続けると、壊れたフォントで描くことになる
        _font = font;
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
            DrawVersion(hdc, client);
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

    /// <summary>版数を帯の右端に出す（2026-10-18 のご要望）。
    ///
    /// <para><b>読めなければ何も描かない。</b>間違った版数は、無いより悪い
    /// （<see cref="Services.AppVersion"/>）。</para>
    ///
    /// <para><b>ボタンに重ならないよう、右端から詰めるだけ</b>にしてある。
    /// 帯が極端に狭いときは文字が切れるが、<b>ボタンの上には来ない</b>——
    /// 開閉ボタンは「開いていても閉じていても同じ場所にある」のが要件なので、
    /// そこを版数で隠してはいけない。</para></summary>
    private void DrawVersion(nint hdc, RECT client)
    {
        var text = Services.AppVersion.Short;
        if (text.Length == 0 || _font == 0)
        {
            return;
        }
        var margin = Scale(VersionMargin, _dpi);
        var left = ButtonBounds().Right + margin;
        var area = new RECT
        {
            Left = left,
            Top = client.Top,
            Right = client.Right - margin,
            Bottom = client.Bottom - 1, // 下辺の境界線を避ける
        };
        if (area.Right <= area.Left)
        {
            return;
        }
        var previous = SelectObject(hdc, _font);
        try
        {
            SetBkMode(hdc, TRANSPARENT);
            SetTextColor(hdc, VersionColor);
            DrawTextW(hdc, text, -1, ref area,
                DT_RIGHT | DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX | DT_END_ELLIPSIS);
        }
        finally
        {
            SelectObject(hdc, previous);
        }
    }

    /// <summary>版数の文字の左右の余白（96dpi 基準）。</summary>
    private const int VersionMargin = 6;

    /// <summary>版数の色（<c>#727272</c>）。<c>COLORREF</c> は BGR。
    /// <b>薄くする</b>——常に出ているものなので、目に入り続けると邪魔になる。</summary>
    private const uint VersionColor = 0x00727272;

    private const uint BandColor = 0x00F0F0F0;
    private const uint BorderColor = 0x00D0D0D0;
    private const uint HotColor = 0x00FBF1E5; // #E5F1FB を COLORREF（BGR）で
}
