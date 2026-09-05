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

    // --- ドラッグ（並べ替え・別ペインへの移動。第 4d 段） ---
    //
    // 掴んでいる帯と、いま指している帯は別でありうる（別ペインへ移すため）ので、
    // **どちらも静的に 1 組だけ**持つ。ドラッグは同時に 1 つしか起きない。

    /// <summary>掴んでいる帯。ドラッグ中だけ非 null。</summary>
    private static TabStripView? _dragFrom;

    /// <summary>掴んだタブの番号（<see cref="_dragFrom"/> の中での）。</summary>
    private static int _dragIndex = -1;

    /// <summary>いま指している帯と、そこへの挿入位置。線を描く先。</summary>
    private static TabStripView? _dragOver;
    private static int _dragInsert = -1;

    /// <summary>押した位置。ここから動いて初めてドラッグとみなす
    /// （クリックしただけで並びが変わらないように）。</summary>
    private POINT _pressPoint;
    private int _pressIndex = -1;

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
        // ドラッグの最中にペインごと閉じられることがある。控えを残さない
        if (ReferenceEquals(_dragFrom, this))
        {
            _dragFrom = null;
            _dragIndex = -1;
        }
        if (ReferenceEquals(_dragOver, this))
        {
            _dragOver = null;
            _dragInsert = -1;
        }
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
                    // 掴んだ位置を控えるだけ。動き出すまではドラッグにしない
                    _pressIndex = index;
                    _pressPoint = PointOf(lParam);
                    SetCapture(hwnd);
                }
                return 0;

            case WM_MOUSEMOVE:
                OnMouseMove(PointOf(lParam));
                return 0;

            case WM_LBUTTONUP:
                // ★ 先に確定させる。ReleaseCapture は WM_CAPTURECHANGED を
                //   その場で呼び返すので、先に放すと「取り消し」に化ける
                EndDrag(commit: true);
                if (GetCapture() == hwnd)
                {
                    ReleaseCapture();
                }
                return 0;

            case WM_CAPTURECHANGED:
                // 取り上げられたら取り消し（別の窓がキャプチャを取った・Alt+Tab 等）。
                // 確定済みなら何も残っていないので、そのまま何もしない
                EndDrag(commit: false);
                return 0;

            case WM_RBUTTONUP when _dragFrom is not null:
                EndDrag(commit: false); // ドラッグ中の右クリックは取り消し
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

    private void Invalidate()
    {
        if (_hwnd != 0)
        {
            InvalidateRect(_hwnd, 0, erase: false);
        }
    }

    // --- ドラッグ（並べ替え・別ペインへの移動） ---

    /// <summary>ここまで動いて初めてドラッグとみなす（クリックで並びが変わらないように）。</summary>
    private const int DragThreshold = 4;

    private void OnMouseMove(POINT point)
    {
        if (_pressIndex < 0)
        {
            return;
        }
        if (_dragFrom is null)
        {
            if (Math.Abs(point.X - _pressPoint.X) < DragThreshold
                && Math.Abs(point.Y - _pressPoint.Y) < DragThreshold)
            {
                return; // まだ動いていない
            }
            _dragFrom = this;
            _dragIndex = _pressIndex;
        }

        var screen = point;
        ClientToScreen(_hwnd, ref screen);
        var target = StripAt(screen);
        var insert = target?.InsertIndexAt(screen) ?? -1;
        if (ReferenceEquals(_dragOver, target) && _dragInsert == insert)
        {
            return;
        }
        var previous = _dragOver;
        _dragOver = target;
        _dragInsert = insert;
        previous?.Invalidate();
        target?.Invalidate();
    }

    /// <summary>その画面座標にあるタブ帯（別ペインのものでもよい）。無ければ null。</summary>
    private static TabStripView? StripAt(POINT screen)
    {
        var hwnd = WindowFromPoint(screen);
        return hwnd != 0 && Strips.TryGetValue(hwnd, out var strip) ? strip : null;
    }

    /// <summary>この帯のどこへ差し込むか。タブの左半分なら手前、右半分なら後ろ。
    /// どのタブの上でもなければ末尾。</summary>
    private int InsertIndexAt(POINT screen)
    {
        var point = screen;
        ScreenToClient(_hwnd, ref point);
        for (var i = 0; i < _rects.Count; i++)
        {
            var r = _rects[i];
            if (point.X >= r.Left && point.X < r.Right && point.Y >= r.Top && point.Y < r.Bottom)
            {
                return point.X < (r.Left + r.Right) / 2 ? i : i + 1;
            }
        }
        return _rects.Count;
    }

    /// <summary>ドラッグを終える。<paramref name="commit"/> が false なら何もせず取り消す。</summary>
    private void EndDrag(bool commit)
    {
        var from = _dragFrom;
        var index = _dragIndex;
        var over = _dragOver;
        var insert = _dragInsert;

        _dragFrom = null;
        _dragIndex = -1;
        _dragOver = null;
        _dragInsert = -1;
        _pressIndex = -1;
        over?.Invalidate();

        if (!commit || from is null || over is null || index < 0 || insert < 0)
        {
            return;
        }

        if (ReferenceEquals(over, from))
        {
            // 同じ帯の中。抜いたぶん後ろの番号が 1 つ詰まる
            var to = insert > index ? insert - 1 : insert;
            from._pane.MoveTab(index, to);
            // ★ 枚数も一緒に出す。番号が枚数を超えていれば当たり判定が壊れている合図
            Diagnostics.Write($"[tab] 並べ替え {index} → {to}（{from._pane.Tabs.Count} 枚）");
            return;
        }

        // 別のペインへ。最後の 1 枚は渡さない（渡すとペインが空になる）
        var count = from._pane.Tabs.Count;
        if (index >= count)
        {
            // 起きてはいけない。起きたら当たり判定の側が壊れている（BUG-031）
            Diagnostics.Write($"[tab] 番号が範囲外 {index}／{count} 枚");
            return;
        }
        var tab = from._pane.DetachTab(index);
        if (tab is null)
        {
            Diagnostics.Write($"[tab] 最後の 1 枚は移せない（{count} 枚）");
            return;
        }
        over._pane.AttachTab(tab, insert);
        Diagnostics.Write($"[tab] 別のペインへ {index} → 挿入 {insert}");
        over.Clicked?.Invoke(); // 移した先を手前のペインにする
    }

    /// <summary>差し込む位置の縦線。</summary>
    private void DrawInsertMark(nint hdc, RECT client)
    {
        if (_rects.Count == 0)
        {
            return;
        }
        var last = _dragInsert >= _rects.Count;
        var anchor = _rects[last ? ^1 : _dragInsert];
        var x = last ? anchor.Right : anchor.Left;
        var mark = new RECT
        {
            Left = x - 1,
            Top = anchor.Top,
            Right = x + 1,
            Bottom = anchor.Bottom,
        };
        var brush = CreateSolidBrush(GetSysColor(COLOR_HIGHLIGHT));
        FillRect(hdc, in mark, brush);
        DeleteObject(brush);
    }

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
        // 行は下端をそろえる。アクティブなタブだけ上へ伸ばす。
        //
        // ★ いったん手元に組んでから入れ替える。行数を知らせると親が並べ直し、
        //   その中で**ここへ戻ってくる**（再入する）。組みかけの状態を
        //   置いたままにすると、戻ってきた側が入れた分の上にこちらの分が積まれ、
        //   **矩形が枚数の 2 倍**になる（BUG-031）
        var rects = new List<RECT>(tabs.Count);
        for (var i = 0; i < tabs.Count; i++)
        {
            var bottom = (rowOf[i] + 1) * rowHeight;
            var height = i == _pane.ActiveIndex ? rowHeight : tabHeight;
            rects.Add(new RECT
            {
                Left = xOf[i],
                Top = bottom - height,
                Right = xOf[i] + widths[i],
                Bottom = bottom,
            });
        }
        _rects.Clear();
        _rects.AddRange(rects);

        // 知らせるのは最後。ここから再入しても、上の入れ替えは済んでいる
        SetRows(row + 1);
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

            if (ReferenceEquals(_dragOver, this) && _dragInsert >= 0)
            {
                DrawInsertMark(hdc, client);
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
