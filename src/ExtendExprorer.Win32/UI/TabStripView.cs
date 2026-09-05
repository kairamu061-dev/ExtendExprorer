using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ExtendExprorer.ViewModels;
using static ExtendExprorer.Interop.Win32;

namespace ExtendExprorer.UI;

/// <summary>タブ帯。<b>自前描画</b>にしてある。
///
/// <para>OS のタブコントロールを使わないのは、<b>折り返し</b>が要るため。
/// タブが増えて 1 行に収まらなくなったら 2 行目・3 行目と積む（行数に上限は設けない）。
/// タブの幅はフォルダ名に合わせ、閉じる × は置かず、中クリックか右クリックのメニューで閉じる。
/// アクティブなタブだけ上に少し伸ばして目立たせる（2026-08-13 のご要望）。</para>
///
/// <para><b>行は下端をそろえて積む。</b>アクティブなタブだけ背が高いので、上端をそろえると
/// 下辺がずれて、帯と一覧の境目が崩れる（WinUI 版の <c>TabWrapPanel</c> と同じ考え方）。</para></summary>
internal sealed unsafe class TabStripView
{
    private const string ClassName = "ExtendExprorer.TabStrip";

    /// <summary>1 行の高さ。非アクティブなタブの高さ＋アクティブが伸びるぶん。</summary>
    private const int RowHeight = 23;

    /// <summary>非アクティブなタブの高さ。</summary>
    private const int TabHeight = 20;

    private const int TabMinWidth = 60;
    private const int TabMaxWidth = 240;

    /// <summary>見出しの左右の余白。</summary>
    private const int TabPaddingX = 10;

    private static readonly Dictionary<nint, TabStripView> Strips = [];
    private static bool _classRegistered;

    private readonly PaneModel _pane;
    private nint _hwnd;
    private nint _font;
    private uint _dpi = 96;
    private int _rows = 1;

    /// <summary>各タブの矩形（クライアント座標）。当たり判定と描画で同じ値を使う
    /// （測り直すと結果がぶれる）。</summary>
    private readonly List<RECT> _rects = [];

    /// <summary>右クリックメニューの対象。メニューを出している間だけ使う。</summary>
    private int _menuTarget = -1;

    private const int CmdClose = 1;
    private const int CmdCloseOthers = 2;
    private const int CmdCloseRight = 3;

    /// <summary>帯の高さが変わった（＝折り返しの行数が変わった）。
    /// 親はこれを受けて一覧の位置を取り直す。</summary>
    internal event Action? HeightChanged;

    /// <summary>帯が操作された（分割時に、どのペインが手前かを切り替える合図）。</summary>
    internal event Action? Clicked;

    internal nint Handle => _hwnd;

    /// <summary>いま必要な帯の高さ（DPI 適用済み）。</summary>
    internal int PreferredHeight => Scale(RowHeight, _dpi) * Math.Max(1, _rows);

    internal TabStripView(PaneModel pane)
    {
        LiveObjects.Track(this, "TabStripView");
        _pane = pane;
        _pane.TabsChanged += OnTabsChanged;
    }

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
        Strips[_hwnd] = this;
        LayoutTabs(bounds.Width);
    }

    /// <summary>窓を壊す。<b>ハンドルの控えとモデルの購読も必ず外す。</b>
    /// どちらか外し忘れると、このタブ帯 1 つぶんがプロセスの終わりまで残る
    /// （ペインを閉じるたびに増える）。</summary>
    internal void Destroy()
    {
        _pane.TabsChanged -= OnTabsChanged;
        if (_hwnd != 0)
        {
            Strips.Remove(_hwnd);
            DestroyWindow(_hwnd);
            _hwnd = 0;
        }
    }

    internal void SetFont(nint font, uint dpi)
    {
        _font = font;
        _dpi = dpi;
        Relayout();
    }

    internal void SetBounds(RECT bounds)
    {
        if (_hwnd == 0)
        {
            return;
        }
        MoveWindow(_hwnd, bounds.Left, bounds.Top, bounds.Width, bounds.Height, repaint: true);
        LayoutTabs(bounds.Width);
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
                hbrBackground = 0, // 全面を自分で塗る（ちらつき防止）
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
            if (!Strips.TryGetValue(hwnd, out var strip))
            {
                return DefWindowProcW(hwnd, msg, wParam, lParam);
            }
            return strip.HandleMessage(hwnd, msg, wParam, lParam);
        }
        catch (Exception ex)
        {
            // 例外を外へ出すとランタイムが即死させる（MainWindow.WndProc と同じ理由）
            Diagnostics.Report($"TabStrip.WndProc(0x{msg:X4})", ex);
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }
    }

    private nint HandleMessage(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_ERASEBKGND:
                return 1; // 背景は WM_PAINT で全面塗る

            case WM_PAINT:
                Paint();
                return 0;

            case WM_LBUTTONDOWN:
                Clicked?.Invoke();
                if (HitTest(PointOf(lParam)) is >= 0 and var index)
                {
                    _pane.Activate(index);
                }
                return 0;

            case WM_MBUTTONUP:
                // 中クリックで閉じる（× ボタンは置かない）
                if (HitTest(PointOf(lParam)) is >= 0 and var closing)
                {
                    _pane.CloseTab(closing);
                }
                return 0;

            case WM_RBUTTONUP:
                ShowMenu(PointOf(lParam));
                return 0;

            case WM_COMMAND:
                RunMenuCommand((int)(wParam & 0xFFFF));
                return 0;

            case WM_DESTROY:
                Strips.Remove(hwnd);
                return 0;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void OnTabsChanged() => Relayout();

    private void Relayout()
    {
        if (_hwnd == 0 || !GetClientRect(_hwnd, out var client))
        {
            return;
        }
        LayoutTabs(client.Width);
        InvalidateRect(_hwnd, 0, erase: false);
    }

    // --- 配置（折り返し） ---

    /// <summary>タブの矩形を決める。左から詰めて、入らなくなったら次の行へ。
    /// 行数に上限は設けない（2026-08-13 のご要望）。</summary>
    private void LayoutTabs(int width)
    {
        _rects.Clear();
        var tabs = _pane.Tabs;
        if (tabs.Count == 0 || width <= 0)
        {
            SetRows(1);
            return;
        }

        var rowHeight = Scale(RowHeight, _dpi);
        var tabHeight = Scale(TabHeight, _dpi);
        var minWidth = Scale(TabMinWidth, _dpi);
        var maxWidth = Scale(TabMaxWidth, _dpi);
        var padding = Scale(TabPaddingX, _dpi);

        var hdc = GetDC(_hwnd);
        var previousFont = _font != 0 ? SelectObject(hdc, _font) : 0;

        var widths = new int[tabs.Count];
        for (var i = 0; i < tabs.Count; i++)
        {
            widths[i] = Math.Clamp(MeasureText(hdc, tabs[i].Title) + padding * 2, minWidth, maxWidth);
        }

        if (previousFont != 0)
        {
            SelectObject(hdc, previousFont);
        }
        ReleaseDC(_hwnd, hdc);

        // 行を割り当てる
        var rowOf = new int[tabs.Count];
        var xOf = new int[tabs.Count];
        var row = 0;
        var x = 0;
        for (var i = 0; i < tabs.Count; i++)
        {
            if (x > 0 && x + widths[i] > width)
            {
                row++;
                x = 0;
            }
            rowOf[i] = row;
            xOf[i] = x;
            x += widths[i];
        }
        SetRows(row + 1);

        // 行は下端をそろえる。アクティブなタブだけ上へ伸ばす
        for (var i = 0; i < tabs.Count; i++)
        {
            var bottom = (rowOf[i] + 1) * rowHeight;
            var height = i == _pane.ActiveIndex ? rowHeight : tabHeight;
            _rects.Add(new RECT
            {
                Left = xOf[i],
                Top = bottom - height,
                Right = xOf[i] + widths[i],
                Bottom = bottom,
            });
        }
    }

    private void SetRows(int rows)
    {
        if (_rows == rows)
        {
            return;
        }
        _rows = rows;
        HeightChanged?.Invoke();
    }

    private static int MeasureText(nint hdc, string text)
    {
        var rect = default(RECT);
        DrawTextW(hdc, text, text.Length, ref rect, DT_CALCRECT | DT_SINGLELINE | DT_NOPREFIX);
        return rect.Width;
    }

    private int HitTest(POINT point)
    {
        // 後ろから見る。アクティブなタブは背が高く、隣の行へはみ出して見えることがある
        for (var i = _rects.Count - 1; i >= 0; i--)
        {
            var r = _rects[i];
            if (point.X >= r.Left && point.X < r.Right && point.Y >= r.Top && point.Y < r.Bottom)
            {
                return i;
            }
        }
        return -1;
    }

    // --- 描画 ---

    private void Paint()
    {
        var hdc = BeginPaint(_hwnd, out var ps);
        try
        {
            if (!GetClientRect(_hwnd, out var client))
            {
                return;
            }
            var band = CreateSolidBrush(GetSysColor(COLOR_BTNFACE));
            var active = CreateSolidBrush(GetSysColor(COLOR_WINDOW));
            var border = CreateSolidBrush(GetSysColor(COLOR_BTNSHADOW));
            var previousFont = _font != 0 ? SelectObject(hdc, _font) : 0;
            SetBkMode(hdc, TRANSPARENT);

            FillRect(hdc, in client, band);

            var tabs = _pane.Tabs;
            for (var i = 0; i < _rects.Count && i < tabs.Count; i++)
            {
                var rect = _rects[i];
                var isActive = i == _pane.ActiveIndex;

                FillRect(hdc, in rect, isActive ? active : band);
                FrameRect(hdc, in rect, border);
                if (isActive)
                {
                    // 下辺を消して、一覧と地続きに見せる
                    var seam = new RECT
                    {
                        Left = rect.Left + 1,
                        Top = rect.Bottom - 1,
                        Right = rect.Right - 1,
                        Bottom = rect.Bottom,
                    };
                    FillRect(hdc, in seam, active);
                }

                SetTextColor(hdc, GetSysColor(isActive ? COLOR_WINDOWTEXT : COLOR_GRAYTEXT));
                var padding = Scale(TabPaddingX, _dpi);
                var text = new RECT
                {
                    Left = rect.Left + padding,
                    Top = rect.Top,
                    Right = rect.Right - padding,
                    Bottom = rect.Bottom,
                };
                var title = tabs[i].Title;
                DrawTextW(hdc, title, title.Length, ref text,
                    DT_SINGLELINE | DT_VCENTER | DT_CENTER | DT_END_ELLIPSIS | DT_NOPREFIX);
            }

            if (previousFont != 0)
            {
                SelectObject(hdc, previousFont);
            }
            DeleteObject(band);
            DeleteObject(active);
            DeleteObject(border);
        }
        finally
        {
            EndPaint(_hwnd, in ps);
        }
    }

    // --- 右クリックメニュー ---

    private void ShowMenu(POINT point)
    {
        var index = HitTest(point);
        if (index < 0)
        {
            return;
        }
        _menuTarget = index;
        var menu = CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }
        try
        {
            var single = _pane.Tabs.Count <= 1;
            var last = index == _pane.Tabs.Count - 1;
            AppendMenuW(menu, MF_STRING | (single ? MF_GRAYED : 0), CmdClose, "タブを閉じる");
            AppendMenuW(menu, MF_STRING | (single ? MF_GRAYED : 0), CmdCloseOthers, "他のタブを閉じる");
            AppendMenuW(menu, MF_STRING | (last ? MF_GRAYED : 0), CmdCloseRight, "右側のタブを閉じる");

            var screen = point;
            ClientToScreen(_hwnd, ref screen);
            var command = TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD,
                screen.X, screen.Y, 0, _hwnd, 0);
            RunMenuCommand(command);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private void RunMenuCommand(int command)
    {
        var index = _menuTarget;
        if (index < 0)
        {
            return;
        }
        switch (command)
        {
            case CmdClose:
                _pane.CloseTab(index);
                break;
            case CmdCloseOthers:
                _pane.CloseOthers(index);
                break;
            case CmdCloseRight:
                _pane.CloseToTheRight(index);
                break;
        }
        _menuTarget = -1;
    }
}
