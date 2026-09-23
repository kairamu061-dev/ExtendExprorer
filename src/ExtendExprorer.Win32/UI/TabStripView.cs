using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ExtendExprorer.Services;
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
/// <para>末尾に<b>「＋」</b>を置く。<b>折り返しの計算にはタブと同じ流れで乗せる</b>ので、
/// 最終行に入らなければ次の行の頭へ回る。枚数の上限は設けていないので、消えることはない。</para>
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

    /// <summary>「＋」の幅。<b>折り返しの計算にも入れる</b>ので、タブと同じ流れに並べる。</summary>
    private const int PlusWidth = 26;

    /// <summary>カーソルが乗っているものを表す番号。実在のタブと衝突しない値にしてある。</summary>
    private const int PlusIndex = int.MaxValue;

    /// <summary>ホバーの色（<c>#CCE8FF</c>）。<c>COLORREF</c> は BGR。
    /// ペインの帯のボタン・パンくずと同じ色にそろえる。
    ///
    /// <para><b>2026-09-18 に濃くした。</b>それまでは <c>#E5F1FB</c> で、
    /// 「乗っているのが分かりにくい」というご指摘。</para></summary>
    private const uint HotColor = 0x00FFE8CC;

    /// <summary>ホバーの枠（<c>#99D1FF</c>）。色を濃くするだけより、
    /// <b>枠を足す方が「押せる」ことがはっきりする</b>。</summary>
    private const uint HotBorderColor = 0x00FFD199;

    /// <summary>見出しの左に置くアイコンと、その右の余白。</summary>
    private const int IconSize = 16;
    private const int IconGap = 4;

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

    /// <summary>「＋」の矩形。<see cref="_rects"/> と同じときに組む。</summary>
    private RECT _plus;

    /// <summary>各タブのアイコン番号。<see cref="_rects"/> と同じ並び・同じときに組む。
    ///
    /// <para><b>引くのは並べるときの 1 回だけ。</b>描くたびに引くと、
    /// 無効化のたびにシェルを呼ぶことになる。</para></summary>
    private readonly List<int> _icons = [];

    /// <summary>カーソルが乗っているタブ（<see cref="PlusIndex"/> なら「＋」）。
    /// 乗っていなければ -1。</summary>
    private int _hot = -1;

    /// <summary><c>WM_MOUSELEAVE</c> を頼んであるか。</summary>
    private bool _tracking;

    /// <summary>一度でもタブが乗ったか。<b>空のまま作られる瞬間</b>
    /// （分割直後・復元の途中——窓を作ってからタブを足すので必ず通る）と、
    /// <b>中身が空になってしまった状態</b>を区別するために持つ。</summary>
    private bool _everHadTabs;

    /// <summary>0 枚の帯を並べたことを、もう書いたか。書かないと毎回の並べ直しで積もる。</summary>
    private bool _reportedEmpty;

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

    /// <summary>最後の 1 枚を別のペインへ渡すので、<b>このペインごと畳んでほしい</b>
    /// （2026-09-23 のご要望）。
    ///
    /// <para>受け手は<b>本当に閉じること</b>。閉じられないまま戻ってくると、
    /// 同じタブが 2 つのペインにぶら下がる。呼ぶ側は戻ったあとに
    /// <b>窓が壊れたか</b>で閉じられたかを確かめている。</para></summary>
    internal event Action? FoldRequested;

    /// <summary>このペインの一覧にフォーカスを移してほしい。
    ///
    /// <para>畳んだペインに居たフォーカスは、<c>PaneHost.Close</c> が
    /// <b>繰り上がった側</b>へ移す。それが移した先のペインとは限らない
    /// （3 つ以上あるとき）ので、移したあとに自分で取り直す。</para></summary>
    internal event Action? FocusRequested;

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
        DragGhost.End();
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
        MoveWindowNoCopy(_hwnd, bounds);
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
                if (_plus.Contains(PointOf(lParam)))
                {
                    AddTab();
                    return 0;
                }
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

            case WM_MOUSELEAVE:
                _tracking = false;
                SetHot(-1);
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
        TrackHover(point);
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
            ShowGhost(_pressIndex, _pressPoint);
        }

        var screen = point;
        ClientToScreen(_hwnd, ref screen);
        DragGhost.Move(screen);
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

    /// <summary>カーソルが乗っているものを覚える。
    ///
    /// <para><b>ドラッグ中は消す。</b>掴んだまま動かしている間は挿入位置の線が主役で、
    /// そこへ背景の色まで付くと、どちらが操作の結果なのか分からなくなる。</para>
    ///
    /// <para><c>WM_MOUSELEAVE</c> を頼んでおかないと、帯の外へ出たときに
    /// <b>色が付いたまま残る</b>（カーソルが乗っていないのに乗って見える）。</para></summary>
    private void TrackHover(POINT point)
    {
        if (!_tracking && _hwnd != 0)
        {
            var track = new TRACKMOUSEEVENT
            {
                cbSize = (uint)sizeof(TRACKMOUSEEVENT),
                dwFlags = TME_LEAVE,
                hwndTrack = _hwnd,
            };
            _tracking = TrackMouseEvent(ref track);
        }
        if (_dragFrom is not null)
        {
            SetHot(-1);
            return;
        }
        SetHot(_plus.Contains(point) ? PlusIndex : HitTest(point));
    }

    /// <summary>そのタブのアイコン番号。
    ///
    /// <para>ふつうのフォルダは<b>実パスで引く</b>ので、ダウンロードのように
    /// 固有の絵を持つフォルダはその絵になる。「PC」はパスが無いので別に引く。</para></summary>
    private static int IconOf(string path)
    {
        if (path.Length == 0)
        {
            return ShellImageList.Folder;
        }
        return ViewModels.FileListViewModel.IsDrivesPath(path)
            ? ShellImageList.DrivesRoot
            : ShellImageList.IndexOfPath(path);
    }

    /// <summary>掴んだタブの絵を、半透明の影として出す。
    ///
    /// <para><b>見た目はタブそのまま</b>（アイコン＋名前＋枠・実寸）。
    /// 掴んだ場所が同じ位置でカーソルに付いてくるよう、その相対座標を渡す。</para></summary>
    private void ShowGhost(int index, POINT grab)
    {
        if ((uint)index >= (uint)_rects.Count || index >= _pane.Tabs.Count)
        {
            return;
        }
        var rect = _rects[index];
        DragGhost.Begin(GetAncestor(_hwnd, GA_ROOT), rect.Width, rect.Height,
            grab.X - rect.Left, grab.Y - rect.Top,
            hdc =>
            {
                var band = CreateSolidBrush(GetSysColor(COLOR_BTNFACE));
                var active = CreateSolidBrush(GetSysColor(COLOR_WINDOW));
                var border = CreateSolidBrush(GetSysColor(COLOR_BTNSHADOW));
                var previousFont = _font != 0 ? SelectObject(hdc, _font) : 0;
                SetBkMode(hdc, TRANSPARENT);
                try
                {
                    // 影は「掴んでいるもの」なので、ホバーは付けず、下辺も残す
                    RenderTab(hdc, index, new RECT { Right = rect.Width, Bottom = rect.Height },
                        isHot: false, seam: false,
                        new TabBrushes(band, active, border, band, border));
                }
                finally
                {
                    if (previousFont != 0)
                    {
                        SelectObject(hdc, previousFont);
                    }
                    DeleteObject(band);
                    DeleteObject(active);
                    DeleteObject(border);
                }
            });
    }

    /// <summary>1 枚を描くのに使う筆。<b>帯は 1 回作って使い回す</b>
    /// （タブの枚数だけ作り直すのは無駄）。影は 1 枚ぶんなので、その場で作って捨てる。</summary>
    private readonly record struct TabBrushes(nint Band, nint Active, nint Border, nint Hot, nint HotBorder);

    /// <summary>タブ 1 枚を描く。<b>帯の中でも影の中でも、ここを通る</b>
    /// ——2 か所に書くと、いつか片方だけ直して見た目がずれる。</summary>
    ///
    /// <param name="seam">下辺を消して一覧と地続きに見せるか。
    /// 帯の中では要るが、<b>影では要らない</b>（下に一覧が無いので、線が欠けて見える）。</param>
    private void RenderTab(nint hdc, int index, RECT rect, bool isHot, bool seam, TabBrushes brushes)
    {
        var tabs = _pane.Tabs;
        if ((uint)index >= (uint)tabs.Count)
        {
            return;
        }
        var isActive = index == _pane.ActiveIndex;

        FillRect(hdc, in rect, isActive ? brushes.Active : (isHot ? brushes.Hot : brushes.Band));
        FrameRect(hdc, in rect, isHot ? brushes.HotBorder : brushes.Border);
        if (isActive && seam)
        {
            var line = new RECT
            {
                Left = rect.Left + 1,
                Top = rect.Bottom - 1,
                Right = rect.Right - 1,
                Bottom = rect.Bottom,
            };
            FillRect(hdc, in line, brushes.Active);
        }

        var padding = Scale(TabPaddingX, _dpi);
        var iconSize = Scale(IconSize, _dpi);
        var iconGap = Scale(IconGap, _dpi);
        var imageList = ShellImageList.Handle;
        if (imageList != 0 && index < _icons.Count && _icons[index] >= 0)
        {
            ImageList_Draw(imageList, _icons[index], hdc,
                rect.Left + padding, rect.Top + ((rect.Height - iconSize) / 2), ILD_TRANSPARENT);
        }
        SetTextColor(hdc, GetSysColor(isActive ? COLOR_WINDOWTEXT : COLOR_GRAYTEXT));
        var text = new RECT
        {
            Left = rect.Left + padding + iconSize + iconGap,
            Top = rect.Top,
            Right = rect.Right - padding,
            Bottom = rect.Bottom,
        };
        var title = tabs[index].Title;
        // アイコンのぶん左に寄るので、文字は中央ではなく左詰めにする
        DrawTextW(hdc, title, title.Length, ref text,
            DT_SINGLELINE | DT_VCENTER | DT_LEFT | DT_END_ELLIPSIS | DT_NOPREFIX);
    }

    private void SetHot(int hot)
    {
        if (_hot == hot)
        {
            return;
        }
        _hot = hot;
        Invalidate();
    }

    /// <summary>「＋」で 1 枚増やす。<b>手前のタブと同じフォルダ</b>を開く
    /// （旧 WinUI 3 版の「＋」と同じ。履歴は引き継がない）。</summary>
    private void AddTab()
    {
        if (_pane.ActiveTab is { } tab)
        {
            _pane.AddTab(tab.Path);
        }
    }

    /// <summary>その画面座標にあるタブ帯（別ペインのものでもよい）。無ければ null。</summary>
    /// <summary>その画面座標にあるタブ帯。
    ///
    /// <para><b>当たり判定ではなく矩形で引く。</b><c>WindowFromPoint</c> だと、
    /// カーソルの真下に居る<b>ドラッグ中の影</b>を拾ってしまい、
    /// 落とし先の帯が見つからなくなる（影の側にも
    /// <see cref="WS_EX_TRANSPARENT"/> を付けてあるが、
    /// <b>重なり順に頼らない</b>方が確実）。</para>
    ///
    /// <para>タブ帯どうしは重ならないので、矩形で引いても曖昧にならない。</para></summary>
    private static TabStripView? StripAt(POINT screen)
    {
        foreach (var strip in Strips.Values)
        {
            if (strip._hwnd != 0 && IsWindowVisible(strip._hwnd)
                && GetWindowRect(strip._hwnd, out var rect) && rect.Contains(screen))
            {
                return strip;
            }
        }
        return null;
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
        // ★ 影は真っ先に消す。ここから下は途中で戻る道が何本もあるので、
        //   後ろに置くとどれかで消し忘れる（BUG-035 と同じ形）
        DragGhost.End();
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

        // 別のペインへ。
        var count = from._pane.Tabs.Count;
        if (index >= count)
        {
            // 起きてはいけない。起きたら当たり判定の側が壊れている（BUG-031）
            Diagnostics.Write($"[tab] 番号が範囲外 {index}／{count} 枚");
            return;
        }

        if (count == 1)
        {
            MoveLastTab(from, over, insert);
            return;
        }

        var tab = from._pane.DetachTab(index);
        if (tab is null)
        {
            // 起きてはいけない。枚数と番号は上で見ているので、ここは通らない
            Diagnostics.Write($"[tab] 外せなかった {index}／{count} 枚");
            return;
        }
        over._pane.AttachTab(tab, insert);
        Diagnostics.Write($"[tab] 別のペインへ {index} → 挿入 {insert}（移した先 {over._pane.Tabs.Count} 枚）");
        over.Clicked?.Invoke(); // 移した先を手前のペインにする
    }

    /// <summary>最後の 1 枚を別のペインへ移す。<b>元のペインは畳む</b>
    /// （2026-09-23 のご要望。それまでは移動そのものを取り消していた）。
    ///
    /// <para><b>順番が肝心。</b>「渡してから畳む」ではなく<b>「畳んでから渡す」</b>にしてある。
    /// 先に外すと <b>0 枚のペイン</b>が一瞬できて、そこを描く道ができてしまう。
    /// タブはただのデータなので、ペインが消えたあとでも渡せる。</para>
    ///
    /// <para>畳めたかどうかは<b>帯の窓が壊れたか</b>で見る。畳めないのに渡すと、
    /// 同じタブが 2 つのペインにぶら下がるため——<b>畳めなければ何もしない。</b>
    /// 落とし先が別のペインである以上ペインは 2 つ以上あるので、ここは通らないはずだが、
    /// 通ったときに状態を壊さない形にしておく。</para></summary>
    private static void MoveLastTab(TabStripView from, TabStripView over, int insert)
    {
        var tab = from._pane.HandOverSoleTab();
        if (tab is null)
        {
            Diagnostics.Write("[tab] 最後の 1 枚を取り出せなかった");
            return;
        }

        from.FoldRequested?.Invoke(); // ここで元のペインが閉じる（この帯の窓も壊れる）
        if (from._hwnd != 0)
        {
            Diagnostics.Write("[tab] 畳めなかったので移さない");
            return;
        }

        over._pane.AttachTab(tab, insert);
        Diagnostics.Write($"[tab] ペインごと畳んで移した → 挿入 {insert}（移した先 {over._pane.Tabs.Count} 枚）");
        over.Clicked?.Invoke(); // 移した先を手前のペインにする
        over.FocusRequested?.Invoke(); // 畳んだ側にフォーカスが残らないように
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
        // ★ 覚えている番号を捨てる。枚数や並びが変わったあとも持ち越すと、
        //   **カーソルが乗っていないタブに色が付く**（次にマウスが動くまで直らない）
        _hot = -1;
        LayoutTabs(client.Width);
        InvalidateRect(_hwnd, 0, erase: false);
    }

    // --- 配置（折り返し） ---

    /// <summary>タブの矩形を決める。左から詰めて、入らなくなったら次の行へ。
    /// 行数に上限は設けない（2026-08-13 のご要望）。</summary>
    private void LayoutTabs(int width)
    {
        _rects.Clear();
        _plus = default;
        var tabs = _pane.Tabs;
        if (tabs.Count == 0 || width <= 0)
        {
            // ★ 0 枚の帯は、作った直後（分割・session の復元の途中）だけの姿。
            //   **一度タブが乗ったあとに 0 枚へ戻ったら**、それは
            //   「帯も一覧も空で、何も操作できないペイン」が画面に出たということ。
            //   最後の 1 枚を移すときに**畳んでから渡す**順にしてあるので起きないはずだが、
            //   順番を戻す変更が入れば**ここが鳴る**（起きない証拠は撮れないので、仕掛けで見張る）
            if (tabs.Count == 0 && _everHadTabs && !_reportedEmpty)
            {
                _reportedEmpty = true;
                Diagnostics.Write("[tab] 0 枚の帯を並べた（起きてはいけない）");
            }
            SetRows(1);
            return;
        }
        _everHadTabs = true;

        var rowHeight = Scale(RowHeight, _dpi);
        var tabHeight = Scale(TabHeight, _dpi);
        var minWidth = Scale(TabMinWidth, _dpi);
        var maxWidth = Scale(TabMaxWidth, _dpi);
        var padding = Scale(TabPaddingX, _dpi);

        var hdc = GetDC(_hwnd);
        var previousFont = _font != 0 ? SelectObject(hdc, _font) : 0;

        // ★ 最後の 1 つは「＋」。**タブと同じ流れに並べる**ので、
        //   折り返しの計算にそのまま乗る（最終行に入らなければ次の行の頭へ回る）
        var iconSize = Scale(IconSize, _dpi);
        var iconGap = Scale(IconGap, _dpi);
        var widths = new int[tabs.Count + 1];
        var icons = new List<int>(tabs.Count);
        for (var i = 0; i < tabs.Count; i++)
        {
            widths[i] = Math.Clamp(
                MeasureText(hdc, tabs[i].Title) + padding * 2 + iconSize + iconGap,
                minWidth, maxWidth);
            icons.Add(IconOf(tabs[i].Path));
        }
        widths[tabs.Count] = Scale(PlusWidth, _dpi);

        if (previousFont != 0)
        {
            SelectObject(hdc, previousFont);
        }
        ReleaseDC(_hwnd, hdc);

        // 行を割り当てる（末尾の 1 つは「＋」）
        var rowOf = new int[widths.Length];
        var xOf = new int[widths.Length];
        var row = 0;
        var x = 0;
        for (var i = 0; i < widths.Length; i++)
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
        var plusBottom = (rowOf[tabs.Count] + 1) * rowHeight;
        var plus = new RECT
        {
            Left = xOf[tabs.Count],
            Top = plusBottom - tabHeight,
            Right = xOf[tabs.Count] + widths[tabs.Count],
            Bottom = plusBottom,
        };
        _rects.Clear();
        _rects.AddRange(rects);
        _icons.Clear();
        _icons.AddRange(icons);
        _plus = plus;

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
            var hotBrush = CreateSolidBrush(HotColor);
            var hotBorder = CreateSolidBrush(HotBorderColor);
            var previousFont = _font != 0 ? SelectObject(hdc, _font) : 0;
            SetBkMode(hdc, TRANSPARENT);

            FillRect(hdc, in client, band);

            var brushes = new TabBrushes(band, active, border, hotBrush, hotBorder);
            var tabs = _pane.Tabs;
            for (var i = 0; i < _rects.Count && i < tabs.Count; i++)
            {
                // 手前のタブにはホバーを出さない（すでに白く、変化が読み取れない）
                var isHot = i != _pane.ActiveIndex && i == _hot;
                RenderTab(hdc, i, _rects[i], isHot, seam: true, brushes);
            }

            DrawPlus(hdc, hotBrush, band);

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
            DeleteObject(hotBrush);
            DeleteObject(hotBorder);
        }
        finally
        {
            EndPaint(_hwnd, in ps);
        }
    }

    /// <summary>タブを増やす「＋」。フォントのグリフを使わず、縦横 2 本の矩形で描く
    /// （帯のボタンと同じ考え方。フォントによって大きさが変わらない）。</summary>
    private void DrawPlus(nint hdc, nint hotBrush, nint bandBrush)
    {
        if (_plus.Width <= 0)
        {
            return;
        }
        FillRect(hdc, in _plus, _hot == PlusIndex ? hotBrush : bandBrush);

        var cx = (_plus.Left + _plus.Right) / 2;
        var cy = (_plus.Top + _plus.Bottom) / 2;
        var reach = Math.Max(3, Scale(4, _dpi));
        var thick = Math.Max(1, Scale(1, _dpi));
        var ink = CreateSolidBrush(GetSysColor(COLOR_WINDOWTEXT));
        var horizontal = new RECT
        {
            Left = cx - reach,
            Top = cy - thick / 2,
            Right = cx + reach + 1,
            Bottom = cy - thick / 2 + thick,
        };
        var vertical = new RECT
        {
            Left = cx - thick / 2,
            Top = cy - reach,
            Right = cx - thick / 2 + thick,
            Bottom = cy + reach + 1,
        };
        FillRect(hdc, in horizontal, ink);
        FillRect(hdc, in vertical, ink);
        DeleteObject(ink);
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
