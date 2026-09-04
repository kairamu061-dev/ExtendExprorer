using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static ExtendExprorer.Interop.Win32;

namespace ExtendExprorer.UI;

/// <summary>ペインごとの帯。<c>[戻る・進む・上へ][パンくず][縦分割・横分割]</c> を横に並べる。
///
/// <para>中身は全部自前描画にした。ボタンを 5 つ・パンくずのセグメントを可変個ぶん
/// 子ウィンドウにすると、移動のたびに作り直すことになる（そのたびにハンドルが増減する）。
/// 1 枚の窓に描いて当たり判定を自分で持つ方が、ペインが 4 つに増えても軽い。</para>
///
/// <para>例外は <see cref="PaneBandView.WndProc"/> で止める。
/// <c>[UnmanagedCallersOnly]</c> の外へ出すとプロセスが即死する。</para></summary>
internal sealed unsafe class PaneBandView
{
    private const string ClassName = "ExtendExprorer.PaneBand";

    /// <summary>帯の高さ（96dpi 基準）。</summary>
    internal const int Height = 26;

    private const int ButtonWidth = 26;
    private const int ButtonHeight = 20;
    private const int PaddingX = 6;
    private const int GroupGap = 6;
    private const int SegmentPaddingX = 5;
    private const int SeparatorWidth = 11;

    /// <summary>「パスが見つかりません」を出しておく時間（address-bar 仕様）。</summary>
    private const uint ErrorMs = 3000;

    private const nint ErrorTimerId = 1;

    private static readonly Dictionary<nint, PaneBandView> Bands = [];
    private static bool _classRegistered;

    /// <summary>帯のボタン。並び順は左から。
    /// <b>右のグループはこの並びのまま右端へ向かって置く</b>ので、順序を変えないこと。</summary>
    private enum Button { None, Back, Forward, Up, SplitVertical, SplitHorizontal, Close }

    /// <summary>パンくずの 1 区切り。</summary>
    private struct Segment
    {
        internal string Text;
        internal string Path;
        internal RECT Bounds;
    }

    private readonly List<Segment> _segments = [];

    private static readonly Button[] AllButtons =
        [Button.Back, Button.Forward, Button.Up,
         Button.SplitVertical, Button.SplitHorizontal, Button.Close];

    private nint _hwnd;
    private nint _instance;
    private nint _font;
    private uint _dpi = 96;

    private string _path = "";
    private Button _hot = Button.None;
    private int _hotSegment = -1;
    private bool _tracking;
    private bool _showingError;

    /// <summary>編集モードのパス入力。使うときだけ作り、抜けたら隠す
    /// （毎回作り直すとフォーカスの取り回しが増えるだけなので、使い回す）。</summary>
    private nint _editor;
    private bool _editing;

    internal bool CanGoBack { get; set; }
    internal bool CanGoForward { get; set; }
    internal bool CanGoUp { get; set; }

    /// <summary>このペインを閉じられるか（＝ペインが 2 つ以上ある）。
    /// <b>閉じられないときも場所は空けたまま、灰色で出す。</b>
    /// 分割のたびにボタンの位置が動くと、押し間違いのもとになる。</summary>
    internal bool CanClose
    {
        get => _canClose;
        set
        {
            if (_canClose == value)
            {
                return;
            }
            _canClose = value;
            Invalidate(); // 灰色と有効の切り替わりを描き直す
        }
    }

    private bool _canClose;

    /// <summary>編集モードのパス入力のハンドル。キーの横取りの判定に使う。</summary>
    internal nint EditorHandle => _editing ? _editor : 0;

    internal event Action? BackRequested;
    internal event Action? ForwardRequested;
    internal event Action? UpRequested;
    internal event Action<SplitDirection>? SplitRequested;

    /// <summary>閉じるボタン。閉じるのはこの帯を持っているペイン。</summary>
    internal event Action? CloseRequested;

    /// <summary>パンくずのクリック。引数は移動先のフルパス。</summary>
    internal event Action<string>? NavigateRequested;

    /// <summary>編集モードで Enter が押された。引数は入力された文字列。
    ///
    /// <para>移動できるかの判定はディスクを見るので非同期になる。ここでは投げるだけにして、
    /// 成功なら移動 → <see cref="SetPath"/> が呼ばれて編集が閉じる、
    /// 失敗なら <see cref="ShowPathNotFound"/> を呼んでもらう、という往復にした。</para></summary>
    internal event Action<string>? PathEntered;

    /// <summary>帯が操作された（このペインを手前にする合図）。</summary>
    internal event Action? Clicked;

    internal void Create(nint parent, nint instance, RECT bounds, nint font, uint dpi)
    {
        _instance = instance;
        _font = font;
        _dpi = dpi;
        RegisterClass(instance);
        _hwnd = CreateWindowExW(0, ClassName, null, WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN,
            bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            parent, 0, instance, 0);
        if (_hwnd == 0)
        {
            throw new InvalidOperationException($"CreateWindowEx({ClassName}) failed: {Marshal.GetLastPInvokeError()}");
        }
        Bands[_hwnd] = this;
        BuildSegments();
    }

    internal void SetBounds(RECT bounds)
    {
        if (_hwnd == 0)
        {
            return;
        }
        MoveWindow(_hwnd, bounds.Left, bounds.Top, bounds.Width, bounds.Height, repaint: true);
        BuildSegments();
        LayoutEditor();
    }

    internal void SetFont(nint font, uint dpi)
    {
        _font = font;
        _dpi = dpi;
        if (_editor != 0)
        {
            SendMessageW(_editor, WM_SETFONT, font, 1);
        }
        BuildSegments();
        Invalidate();
    }

    /// <summary>いま見ているフォルダ。移動・タブ切替のたびに呼ばれる。</summary>
    internal void SetPath(string path)
    {
        _path = path ?? "";
        // 移動が起きたら編集モードは解除する（履歴移動・タブ切替でも同じ）
        if (_editing)
        {
            ExitEdit();
        }
        BuildSegments();
        Invalidate();
    }

    internal void Invalidate()
    {
        if (_hwnd != 0)
        {
            InvalidateRect(_hwnd, 0, erase: false);
        }
    }

    /// <summary>窓を壊す。<b>ハンドルの控えも必ず外す。</b>外し忘れると、
    /// この帯 1 つぶんがプロセスの終わりまで残る（ペインを閉じるたびに増える）。</summary>
    internal void Destroy()
    {
        if (_hwnd != 0)
        {
            Bands.Remove(_hwnd);
            DestroyWindow(_hwnd);
            _hwnd = 0;
        }
    }

    // --- 配置 ---

    private RECT ClientBounds()
    {
        GetClientRect(_hwnd, out var client);
        return client;
    }

    /// <summary>ボタン 1 つぶんの矩形。<paramref name="index"/> は左のグループなら 0 起点、
    /// 右のグループなら右端からの並び。</summary>
    private RECT ButtonBounds(Button button)
    {
        var client = ClientBounds();
        var width = Scale(ButtonWidth, _dpi);
        var height = Scale(ButtonHeight, _dpi);
        var top = client.Top + (client.Height - height) / 2;
        var padding = Scale(PaddingX, _dpi);

        var index = button switch
        {
            Button.Back => 0,
            Button.Forward => 1,
            Button.Up => 2,
            _ => -1,
        };
        if (index >= 0)
        {
            var left = client.Left + padding + (index * width);
            return new RECT { Left = left, Top = top, Right = left + width, Bottom = top + height };
        }
        // 右のグループ。右端から Close → 横分割 → 縦分割 の順で置く
        var rightIndex = button switch
        {
            Button.SplitVertical => 2,
            Button.SplitHorizontal => 1,
            _ => 0, // Close
        };
        var right = client.Right - padding - (rightIndex * width);
        return new RECT { Left = right - width, Top = top, Right = right, Bottom = top + height };
    }

    /// <summary>左右のボタン群にはさまれた、パンくず（または編集）の領域。</summary>
    private RECT AddressBounds()
    {
        var client = ClientBounds();
        var gap = Scale(GroupGap, _dpi);
        var height = Scale(ButtonHeight, _dpi);
        var top = client.Top + (client.Height - height) / 2;
        var left = ButtonBounds(Button.Up).Right + gap;
        var right = ButtonBounds(Button.SplitVertical).Left - gap;
        return new RECT
        {
            Left = left,
            Top = top,
            Right = Math.Max(left, right),
            Bottom = top + height,
        };
    }

    /// <summary>パンくずを組み直す。<b>入りきらないときは頭から捨てる。</b>
    /// いま見ているフォルダ（末尾）が見えなくなる方が困るため。</summary>
    private void BuildSegments()
    {
        _segments.Clear();
        _hotSegment = -1;
        if (_hwnd == 0 || _path.Length == 0)
        {
            return;
        }

        var parts = SplitPath(_path);
        var area = AddressBounds();
        var hdc = GetDC(_hwnd);
        if (hdc == 0)
        {
            return;
        }
        try
        {
            var old = _font != 0 ? SelectObject(hdc, _font) : 0;
            var padding = Scale(SegmentPaddingX, _dpi) * 2;
            var separator = Scale(SeparatorWidth, _dpi);

            var widths = new int[parts.Count];
            var total = 0;
            for (var i = 0; i < parts.Count; i++)
            {
                widths[i] = TextWidth(hdc, parts[i].Text) + padding;
                total += widths[i] + (i > 0 ? separator : 0);
            }

            // 入りきらないぶんは頭から落とす
            var first = 0;
            while (first < parts.Count - 1 && total > area.Width)
            {
                total -= widths[first] + separator;
                first++;
            }

            var x = area.Left;
            for (var i = first; i < parts.Count; i++)
            {
                if (i > first)
                {
                    x += separator;
                }
                _segments.Add(new Segment
                {
                    Text = parts[i].Text,
                    Path = parts[i].Path,
                    Bounds = new RECT
                    {
                        Left = x,
                        Top = area.Top,
                        Right = Math.Min(area.Right, x + widths[i]),
                        Bottom = area.Bottom,
                    },
                });
                x += widths[i];
            }
            if (old != 0)
            {
                SelectObject(hdc, old);
            }
        }
        finally
        {
            ReleaseDC(_hwnd, hdc);
        }
    }

    /// <summary>フルパスを「表示名とそこまでのパス」の並びにする。</summary>
    private static List<(string Text, string Path)> SplitPath(string path)
    {
        var result = new List<(string, string)>();
        var trimmed = path.TrimEnd('\\', '/');
        // ルートは「末尾を削る前」から取る。削ったあとの "C:" を渡すと GetPathRoot は
        // そのまま "C:" を返すが、これはドライブ相対（そのドライブのカレント）で
        // "C:\" とは別物になる。ドライブ直下を開いているとき、唯一のセグメントを
        // 押すと別の場所へ飛んでしまう
        var root = System.IO.Path.GetPathRoot(path) ?? "";
        if (root.Length == 0)
        {
            // ルートが取れない特殊なパスは 1 つにまとめて出す（編集はできる）
            result.Add((trimmed, trimmed));
            return result;
        }
        var rootName = root.TrimEnd('\\', '/');
        result.Add((rootName.Length == 0 ? root : rootName, root));

        var rest = trimmed.Length > root.Length ? trimmed[root.Length..] : "";
        var cumulative = root;
        foreach (var part in rest.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
        {
            cumulative = System.IO.Path.Combine(cumulative, part);
            result.Add((part, cumulative));
        }
        return result;
    }

    private int TextWidth(nint hdc, string text)
    {
        var rect = default(RECT);
        DrawTextW(hdc, text, -1, ref rect, DT_CALCRECT | DT_SINGLELINE | DT_NOPREFIX);
        return rect.Width;
    }

    // --- 描画 ---

    private void Paint()
    {
        var hdc = BeginPaint(_hwnd, out var ps);
        try
        {
            var client = ClientBounds();
            Fill(hdc, client, BandColor);
            Fill(hdc, client with { Top = client.Bottom - 1 }, BorderColor);

            var old = _font != 0 ? SelectObject(hdc, _font) : 0;
            SetBkMode(hdc, TRANSPARENT);

            DrawGroup(hdc, Button.Back, Button.Up);
            DrawGroup(hdc, Button.SplitVertical, Button.Close);

            if (!_editing && !_showingError)
            {
                DrawSegments(hdc);
            }
            if (_showingError)
            {
                DrawError(hdc);
            }

            if (old != 0)
            {
                SelectObject(hdc, old);
            }
        }
        finally
        {
            EndPaint(_hwnd, in ps);
        }
    }

    /// <summary>ボタンを 1 枠にまとめて描く（区切り線つき・現行版と同じ見た目）。</summary>
    private void DrawGroup(nint hdc, Button from, Button to)
    {
        var left = ButtonBounds(from);
        var right = ButtonBounds(to);
        var frame = new RECT
        {
            Left = Math.Min(left.Left, right.Left),
            Top = left.Top,
            Right = Math.Max(left.Right, right.Right),
            Bottom = left.Bottom,
        };
        Fill(hdc, frame, GroupFillColor);

        for (var button = from; button <= to; button++)
        {
            var bounds = ButtonBounds(button);
            if (_hot == button && IsEnabled(button))
            {
                Fill(hdc, bounds, HotColor);
            }
            DrawIcon(hdc, button, bounds);
            if (button != to)
            {
                // 区切りは上下を少し空けて、枠いっぱいには引かない
                var inset = Scale(3, _dpi);
                Fill(hdc, new RECT
                {
                    Left = bounds.Right - 1,
                    Top = bounds.Top + inset,
                    Right = bounds.Right,
                    Bottom = bounds.Bottom - inset,
                }, SeparatorColor);
            }
        }
        FrameRectWith(hdc, frame, GroupBorderColor);
    }

    private bool IsEnabled(Button button) => button switch
    {
        Button.Back => CanGoBack,
        Button.Forward => CanGoForward,
        Button.Up => CanGoUp,
        Button.Close => CanClose,
        _ => true,
    };

    private void DrawIcon(nint hdc, Button button, RECT bounds)
    {
        var color = IsEnabled(button) ? IconColor : GetSysColor(COLOR_GRAYTEXT);
        switch (button)
        {
            case Button.Back:
                DrawArrow(hdc, bounds, -1, 0, color);
                break;
            case Button.Forward:
                DrawArrow(hdc, bounds, 1, 0, color);
                break;
            case Button.Up:
                DrawArrow(hdc, bounds, 0, -1, color);
                break;
            case Button.SplitVertical:
                DrawSplit(hdc, bounds, vertical: true, color);
                break;
            case Button.SplitHorizontal:
                DrawSplit(hdc, bounds, vertical: false, color);
                break;
            case Button.Close:
                DrawCross(hdc, bounds, color);
                break;
        }
    }

    /// <summary>矢印（三角＋軸）。グリフ用のフォントを持たずに済むよう座標で描く。</summary>
    private void DrawArrow(nint hdc, RECT bounds, int dx, int dy, uint color)
    {
        var cx = (bounds.Left + bounds.Right) / 2;
        var cy = (bounds.Top + bounds.Bottom) / 2;
        var reach = Math.Max(3, Scale(5, _dpi));
        var head = Math.Max(2, Scale(4, _dpi));
        var thick = Math.Max(1, Scale(3, _dpi));

        var brush = CreateSolidBrush(color);
        var pen = GetStockObject(NULL_PEN);
        var oldBrush = SelectObject(hdc, brush);
        var oldPen = SelectObject(hdc, pen);

        var points = stackalloc POINT[3];
        if (dy == 0)
        {
            var tipX = cx + (dx * reach);
            var baseX = tipX - (dx * head);
            points[0] = new POINT { X = tipX, Y = cy };
            points[1] = new POINT { X = baseX, Y = cy - reach };
            points[2] = new POINT { X = baseX, Y = cy + reach };
            Polygon(hdc, points, 3);
            var stem = new RECT
            {
                Left = Math.Min(baseX, cx - (dx * reach)),
                Top = cy - (thick / 2),
                Right = Math.Max(baseX, cx - (dx * reach)),
                Bottom = cy - (thick / 2) + thick,
            };
            FillRect(hdc, in stem, brush);
        }
        else
        {
            var tipY = cy + (dy * reach);
            var baseY = tipY - (dy * head);
            points[0] = new POINT { X = cx, Y = tipY };
            points[1] = new POINT { X = cx - reach, Y = baseY };
            points[2] = new POINT { X = cx + reach, Y = baseY };
            Polygon(hdc, points, 3);
            var stem = new RECT
            {
                Left = cx - (thick / 2),
                Top = Math.Min(baseY, cy - (dy * reach)),
                Right = cx - (thick / 2) + thick,
                Bottom = Math.Max(baseY, cy - (dy * reach)),
            };
            FillRect(hdc, in stem, brush);
        }

        SelectObject(hdc, oldPen);
        SelectObject(hdc, oldBrush);
        DeleteObject(brush);
    }

    /// <summary>分割ボタン（枠と、その中の仕切り）。</summary>
    private void DrawSplit(nint hdc, RECT bounds, bool vertical, uint color)
    {
        var cx = (bounds.Left + bounds.Right) / 2;
        var cy = (bounds.Top + bounds.Bottom) / 2;
        var halfW = Math.Max(4, Scale(6, _dpi));
        var halfH = Math.Max(3, Scale(5, _dpi));
        var frame = new RECT
        {
            Left = cx - halfW,
            Top = cy - halfH,
            Right = cx + halfW,
            Bottom = cy + halfH,
        };
        FrameRectWith(hdc, frame, color);
        var divider = vertical
            ? new RECT { Left = cx, Top = frame.Top, Right = cx + 1, Bottom = frame.Bottom }
            : new RECT { Left = frame.Left, Top = cy, Right = frame.Right, Bottom = cy + 1 };
        Fill(hdc, divider, color);
    }

    /// <summary>閉じるボタンの ✕。斜めの線をドットで置いていく
    /// （グリフ用のフォントを持たずに済ませるのは、ほかのアイコンと同じ理由）。</summary>
    private void DrawCross(nint hdc, RECT bounds, uint color)
    {
        var cx = (bounds.Left + bounds.Right) / 2;
        var cy = (bounds.Top + bounds.Bottom) / 2;
        var reach = Math.Max(3, Scale(4, _dpi));
        var thick = Math.Max(1, Scale(1, _dpi));
        for (var i = -reach; i <= reach; i++)
        {
            Fill(hdc, new RECT
            {
                Left = cx + i, Top = cy + i, Right = cx + i + thick, Bottom = cy + i + thick,
            }, color);
            Fill(hdc, new RECT
            {
                Left = cx + i, Top = cy - i, Right = cx + i + thick, Bottom = cy - i + thick,
            }, color);
        }
    }

    private void DrawSegments(nint hdc)
    {
        var separatorWidth = Scale(SeparatorWidth, _dpi);
        for (var i = 0; i < _segments.Count; i++)
        {
            var segment = _segments[i];
            if (i > 0)
            {
                var gap = new RECT
                {
                    Left = segment.Bounds.Left - separatorWidth,
                    Top = segment.Bounds.Top,
                    Right = segment.Bounds.Left,
                    Bottom = segment.Bounds.Bottom,
                };
                SetTextColor(hdc, SeparatorTextColor);
                DrawTextW(hdc, "›", -1, ref gap, DT_SINGLELINE | DT_VCENTER | DT_CENTER | DT_NOPREFIX);
            }
            if (_hotSegment == i)
            {
                Fill(hdc, segment.Bounds, HotColor);
            }
            var text = segment.Bounds;
            SetTextColor(hdc, GetSysColor(COLOR_WINDOWTEXT));
            DrawTextW(hdc, segment.Text, -1, ref text,
                DT_SINGLELINE | DT_VCENTER | DT_CENTER | DT_END_ELLIPSIS | DT_NOPREFIX);
        }
    }

    private void DrawError(nint hdc)
    {
        var area = AddressBounds() with { Left = EditorBounds().Right };
        SetTextColor(hdc, ErrorTextColor);
        DrawTextW(hdc, ErrorText, -1, ref area,
            DT_SINGLELINE | DT_VCENTER | DT_CENTER | DT_NOPREFIX);
    }

    private const string ErrorText = "パスが見つかりません";

    // --- 編集モード ---

    /// <summary>パンくずの余白がクリックされた。フルパスを全選択した編集にする。</summary>
    private void EnterEdit()
    {
        if (_hwnd == 0)
        {
            return;
        }
        HideError();
        if (_editor == 0)
        {
            var area = EditorBounds();
            _editor = CreateWindowExW(0, WC_EDIT, null,
                WS_CHILD | ES_AUTOHSCROLL,
                area.Left, area.Top, area.Width, area.Height,
                _hwnd, 0, _instance, 0);
            if (_editor == 0)
            {
                return;
            }
            SendMessageW(_editor, WM_SETFONT, _font, 1);
        }
        _editing = true;
        LayoutEditor();
        SetWindowTextW(_editor, _path);
        ShowWindow(_editor, SW_SHOW);
        SetFocus(_editor);
        SendMessageW(_editor, EM_SETSEL, 0, -1);
        Invalidate();
    }

    private void ExitEdit()
    {
        _editing = false;
        if (_editor != 0)
        {
            // 隠す窓にフォーカスが残るとキーの行き先が無くなる。親へ返してから隠す
            var hadFocus = GetFocus() == _editor;
            ShowWindow(_editor, SW_HIDE);
            if (hadFocus)
            {
                SetFocus(GetParent(_hwnd));
            }
        }
        HideError();
        Invalidate();
    }

    private void LayoutEditor()
    {
        if (_editor == 0)
        {
            return;
        }
        var area = EditorBounds();
        MoveWindow(_editor, area.Left, area.Top, area.Width, area.Height, repaint: true);
    }

    /// <summary>入力欄の場所。エラーを出している間は、その文字のぶんだけ右を空ける
    /// （編集を続けたまま「見つかりません」も見えるようにするため）。</summary>
    private RECT EditorBounds()
    {
        var area = AddressBounds();
        if (!_showingError)
        {
            return area;
        }
        return area with { Right = Math.Max(area.Left, area.Right - ErrorWidth()) };
    }

    private int ErrorWidth()
    {
        var hdc = GetDC(_hwnd);
        if (hdc == 0)
        {
            return 0;
        }
        try
        {
            var old = _font != 0 ? SelectObject(hdc, _font) : 0;
            var width = TextWidth(hdc, ErrorText) + Scale(SegmentPaddingX, _dpi) * 2;
            if (old != 0)
            {
                SelectObject(hdc, old);
            }
            return width;
        }
        finally
        {
            ReleaseDC(_hwnd, hdc);
        }
    }

    /// <summary>編集中のキー。Enter で確定、Esc で取り消し。
    /// それ以外は入力欄にそのまま渡す（<b>Backspace を「戻る」に取られないこと</b>）。</summary>
    internal bool HandleEditorKey(int key)
    {
        if (!_editing)
        {
            return false;
        }
        switch (key)
        {
            case VK_RETURN:
                Commit();
                return true;
            case VK_ESCAPE:
                ExitEdit();
                return true;
        }
        return false;
    }

    private void Commit()
    {
        var text = EditorText().Trim();
        if (text.Length == 0 || PathEntered is null)
        {
            ExitEdit();
            return;
        }
        HideError();
        // 成否はこのあと外から知らされる（移動できれば SetPath で閉じる）
        PathEntered.Invoke(text);
    }

    /// <summary>入力されたパスが見つからなかった。<b>編集は続ける</b>（仕様）。</summary>
    internal void ShowPathNotFound()
    {
        if (_hwnd == 0)
        {
            return;
        }
        _showingError = true;
        SetTimer(_hwnd, ErrorTimerId, ErrorMs, 0);
        LayoutEditor();
        Invalidate();
    }

    private string EditorText()
    {
        if (_editor == 0)
        {
            return "";
        }
        var length = GetWindowTextLengthW(_editor);
        if (length <= 0)
        {
            return "";
        }
        var buffer = new char[length + 1];
        fixed (char* text = buffer)
        {
            var written = GetWindowTextW(_editor, text, buffer.Length);
            return new string(text, 0, Math.Max(0, written));
        }
    }

    private void HideError()
    {
        if (_showingError)
        {
            _showingError = false;
            KillTimer(_hwnd, ErrorTimerId);
            LayoutEditor();
            Invalidate();
        }
    }

    // --- メッセージ ---

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
                hbrBackground = 0,
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
            if (!Bands.TryGetValue(hwnd, out var band))
            {
                return DefWindowProcW(hwnd, msg, wParam, lParam);
            }
            return band.HandleMessage(hwnd, msg, wParam, lParam);
        }
        catch (Exception ex)
        {
            Diagnostics.Report($"PaneBand.WndProc(0x{msg:X4})", ex);
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }
    }

    private nint HandleMessage(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_ERASEBKGND:
                return 1;

            case WM_PAINT:
                Paint();
                return 0;

            case WM_MOUSEMOVE:
                OnMouseMove(hwnd, PointOf(lParam));
                return 0;

            case WM_MOUSELEAVE:
                _tracking = false;
                SetHot(Button.None, -1);
                return 0;

            case WM_LBUTTONDOWN:
                OnClick(PointOf(lParam));
                return 0;

            case WM_TIMER when wParam == ErrorTimerId:
                HideError();
                return 0;

            case WM_COMMAND when (int)((wParam >> 16) & 0xFFFF) == EN_KILLFOCUS:
                // Enter 以外でフォーカスが外れたら編集を捨てる（仕様）
                if (_editing)
                {
                    ExitEdit();
                }
                return 0;

            case WM_DESTROY:
                KillTimer(hwnd, ErrorTimerId);
                Bands.Remove(hwnd);
                return 0;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void OnMouseMove(nint hwnd, POINT point)
    {
        if (!_tracking)
        {
            var track = new TRACKMOUSEEVENT
            {
                cbSize = (uint)sizeof(TRACKMOUSEEVENT),
                dwFlags = TME_LEAVE,
                hwndTrack = hwnd,
            };
            _tracking = TrackMouseEvent(ref track);
        }
        SetHot(HitButton(point), HitSegment(point));
    }

    private void SetHot(Button button, int segment)
    {
        if (_hot != button || _hotSegment != segment)
        {
            _hot = button;
            _hotSegment = segment;
            Invalidate();
        }
    }

    private void OnClick(POINT point)
    {
        Clicked?.Invoke();

        var button = HitButton(point);
        if (button != Button.None)
        {
            if (!IsEnabled(button))
            {
                return;
            }
            switch (button)
            {
                case Button.Back: BackRequested?.Invoke(); break;
                case Button.Forward: ForwardRequested?.Invoke(); break;
                case Button.Up: UpRequested?.Invoke(); break;
                case Button.SplitVertical: SplitRequested?.Invoke(SplitDirection.Vertical); break;
                case Button.SplitHorizontal: SplitRequested?.Invoke(SplitDirection.Horizontal); break;
                case Button.Close: CloseRequested?.Invoke(); break;
            }
            return;
        }

        var segment = HitSegment(point);
        if (segment >= 0)
        {
            NavigateRequested?.Invoke(_segments[segment].Path);
            return;
        }
        if (AddressBounds().Contains(point))
        {
            // 余白のクリックは編集モードへ（フォルダ名の上ではない場所）
            EnterEdit();
        }
    }

    private Button HitButton(POINT point)
    {
        foreach (var button in AllButtons)
        {
            if (ButtonBounds(button).Contains(point))
            {
                return button;
            }
        }
        return Button.None;
    }

    private int HitSegment(POINT point)
    {
        if (_editing)
        {
            return -1;
        }
        for (var i = 0; i < _segments.Count; i++)
        {
            if (_segments[i].Bounds.Contains(point))
            {
                return i;
            }
        }
        return -1;
    }

    private static void Fill(nint hdc, RECT rect, uint color)
    {
        var brush = CreateSolidBrush(color);
        FillRect(hdc, in rect, brush);
        DeleteObject(brush);
    }

    private static void FrameRectWith(nint hdc, RECT rect, uint color)
    {
        var brush = CreateSolidBrush(color);
        FrameRect(hdc, in rect, brush);
        DeleteObject(brush);
    }

    // 配色は現行 WinUI 版と同じ（COLORREF なので BGR の並び）
    private const uint BandColor = 0x00F0F0F0;
    private const uint BorderColor = 0x00D0D0D0;
    private const uint GroupFillColor = 0x00FCFCFC;
    private const uint GroupBorderColor = 0x00C8C8C8;
    private const uint SeparatorColor = 0x00E0E0E0;
    private const uint HotColor = 0x00FBF1E5;       // #E5F1FB
    private const uint IconColor = 0x002B2B2B;
    private const uint SeparatorTextColor = 0x00808080;
    private const uint ErrorTextColor = 0x002222C0;
}
