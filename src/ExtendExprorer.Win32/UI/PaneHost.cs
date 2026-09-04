using ExtendExprorer.Services;
using static ExtendExprorer.Interop.Win32;

namespace ExtendExprorer.UI;

internal enum SplitDirection { Horizontal, Vertical }

/// <summary>ペインの分割を持つ木。葉がペイン、節が分割。
/// 現行 WinUI 版の <c>LayoutNodeViewModel</c> と同じ形を、XAML 抜きで持つ。</summary>
internal sealed class LayoutNode
{
    /// <summary>葉のとき。</summary>
    internal PaneView? Pane { get; set; }

    /// <summary>節のとき。<see cref="SplitDirection.Vertical"/> は左右に分ける。</summary>
    internal SplitDirection Direction { get; init; }
    internal double Ratio { get; set; } = 0.5;
    internal LayoutNode? First { get; set; }
    internal LayoutNode? Second { get; set; }
    internal SplitterView? Splitter { get; set; }

    internal bool IsLeaf => Pane is not null;

    internal IEnumerable<PaneView> Panes
    {
        get
        {
            if (Pane is { } pane)
            {
                yield return pane;
                yield break;
            }
            foreach (var p in First?.Panes ?? [])
            {
                yield return p;
            }
            foreach (var p in Second?.Panes ?? [])
            {
                yield return p;
            }
        }
    }
}

/// <summary>ペイン領域全体。分割の木を持ち、与えられた矩形に並べる。
///
/// <para>分割は<b>木</b>で持つ（左右 2 分割の繰り返しではなく）。こうしておくと
/// 「左を上下に、右をさらに左右に」のような入れ子がそのまま表せる。
/// 現行 WinUI 版と同じ構造なので、session の復元（第 5 段）もそのまま移せる。</para></summary>
internal sealed class PaneHost
{
    private readonly IFileSystemService _fs;
    private readonly nint _parent;
    private readonly nint _instance;

    private LayoutNode _root;
    private RECT _bounds;
    private nint _font;
    private uint _dpi = 96;

    /// <summary>いま手前のペイン。キー操作（タブ追加・移動）の宛先。</summary>
    internal PaneView Active { get; private set; }

    internal IEnumerable<PaneView> Panes => _root.Panes;

    /// <summary>並べ直しが必要になった（タブ帯の行数が変わった等）。</summary>
    internal event Action? LayoutChanged;

    /// <summary>手前のペインが変わった、または手前のペインの表示フォルダが変わった。
    /// タイトルバーの更新に使う。</summary>
    internal event Action? ActiveChanged;

    internal PaneHost(IFileSystemService fs, nint parent, nint instance, nint font, uint dpi)
    {
        _fs = fs;
        _parent = parent;
        _instance = instance;
        _font = font;
        _dpi = dpi;

        var pane = CreatePane();
        _root = new LayoutNode { Pane = pane };
        Active = pane;
    }

    private PaneView CreatePane()
    {
        var pane = new PaneView(_fs);
        pane.Activated += p =>
        {
            Active = p;
            ActiveChanged?.Invoke();
        };
        pane.LayoutChanged += () => LayoutChanged?.Invoke();
        // 帯の分割ボタンは「そのペインを割る」。手前のペインではない
        pane.SplitRequested += (target, direction) => Split(direction, target);
        // 閉じるボタンも同じく「そのペイン」を閉じる
        pane.CloseRequested += Close;
        // 手前のペインが移動したときだけタイトルを更新する（裏のペインでは動かさない）
        pane.Model.FileList.StateChanged += () =>
        {
            if (ReferenceEquals(Active, pane))
            {
                ActiveChanged?.Invoke();
            }
        };
        return pane;
    }

    /// <summary>最初のペインの子ウィンドウを作る。</summary>
    internal void Create(RECT bounds)
    {
        _bounds = bounds;
        Active.Create(_parent, _instance, bounds, _font, _dpi);
    }

    internal void SetBounds(RECT bounds)
    {
        _bounds = bounds;
        Arrange(_root, bounds);
    }

    internal void SetFont(nint font, uint dpi)
    {
        _font = font;
        _dpi = dpi;
        foreach (var pane in Panes)
        {
            pane.SetFont(font, dpi);
        }
        Arrange(_root, _bounds);
    }

    /// <summary>木をたどって矩形を配る。節では仕切りのぶんを差し引いてから分ける。</summary>
    private void Arrange(LayoutNode node, RECT bounds)
    {
        if (node.Pane is { } pane)
        {
            pane.SetBounds(bounds);
            return;
        }
        if (node.First is not { } first || node.Second is not { } second)
        {
            return;
        }
        var thickness = Scale(SplitterView.Thickness, _dpi);
        if (node.Direction == SplitDirection.Vertical)
        {
            var usable = Math.Max(0, bounds.Width - thickness);
            var firstWidth = (int)Math.Round(usable * node.Ratio);
            Arrange(first, bounds with { Right = bounds.Left + firstWidth });
            node.Splitter?.SetBounds(bounds with
            {
                Left = bounds.Left + firstWidth,
                Right = bounds.Left + firstWidth + thickness,
            });
            Arrange(second, bounds with { Left = bounds.Left + firstWidth + thickness });
        }
        else
        {
            var usable = Math.Max(0, bounds.Height - thickness);
            var firstHeight = (int)Math.Round(usable * node.Ratio);
            Arrange(first, bounds with { Bottom = bounds.Top + firstHeight });
            node.Splitter?.SetBounds(bounds with
            {
                Top = bounds.Top + firstHeight,
                Bottom = bounds.Top + firstHeight + thickness,
            });
            Arrange(second, bounds with { Top = bounds.Top + firstHeight + thickness });
        }
    }

    // --- 分割 ---

    /// <summary>手前のペインを 2 つに割る。新しいペインは同じフォルダを開く。</summary>
    internal PaneView Split(SplitDirection direction) => Split(direction, Active);

    /// <summary>指定したペインを 2 つに割る。</summary>
    internal PaneView Split(SplitDirection direction, PaneView pane)
    {
        var target = Find(_root, pane);
        if (target is null)
        {
            return pane;
        }

        var existing = pane;
        var added = CreatePane();

        // 葉だったところを節に作り替え、元のペインと新しいペインを両方ぶら下げる
        var splitter = new SplitterView(direction == SplitDirection.Vertical);
        var node = new LayoutNode
        {
            Direction = direction,
            Ratio = 0.5,
            First = new LayoutNode { Pane = existing },
            Second = new LayoutNode { Pane = added },
            Splitter = splitter,
        };
        ReplaceLeaf(target, node);

        splitter.Create(_parent, _instance, default);
        splitter.Dragged += point => OnSplitterDragged(node, point);

        added.Create(_parent, _instance, default, _font, _dpi);
        added.Model.AddTab(existing.Model.ActiveTab?.Path ?? _fs.HomePath);

        Arrange(_root, _bounds);
        UpdateCloseButtons();
        Active = added;
        ActiveChanged?.Invoke();
        added.Focus();
        return added;
    }

    // --- 閉じる ---

    /// <summary>ペインが 2 つ以上あるか（1 つしか無いときは閉じさせない）。</summary>
    internal bool CanClose => _root.Panes.Skip(1).Any();

    /// <summary>ペインを 1 つ閉じる。<b>相方が親の位置へ繰り上がる</b>——
    /// 分割は木で持っているので、節を相方の枝ごと差し替えればそれで済む。
    ///
    /// <para>閉じたペインの持ち物（子ウィンドウ 4 つ・仕切り・監視・ドロップ先の登録）は
    /// ここで全部手放す。<b>1 つでも残すと、閉じるたびに増える</b>ので、
    /// 手放したことを <c>--diag</c> に 1 行残しておく（メモリ実測で当たりを付けるため）。</para></summary>
    internal void Close(PaneView pane)
    {
        if (!CanClose)
        {
            return;
        }
        var parent = FindParent(_root, pane);
        if (parent is null)
        {
            return; // 木に無い＝既に閉じている
        }
        var target = Find(_root, pane);
        var survivor = ReferenceEquals(parent.First, target) ? parent.Second : parent.First;
        if (target is null || survivor is null)
        {
            return;
        }

        ReplaceLeaf(parent, survivor);
        parent.Splitter?.Destroy();
        parent.Splitter = null;
        pane.Dispose();

        // 手前のペインが消えたときは、繰り上がった側の先頭へ移す
        if (ReferenceEquals(Active, pane))
        {
            Active = survivor.Panes.First();
        }
        Arrange(_root, _bounds);
        UpdateCloseButtons();
        ActiveChanged?.Invoke();
        Active.Focus();
        Diagnostics.Write($"[pane] 閉じた（窓 4 つ・仕切り 1 つ・監視 1 つを手放した）残り={_root.Panes.Count()}");
    }

    /// <summary>ある葉の親の節。根そのものが対象のときは null（＝閉じられない）。</summary>
    private static LayoutNode? FindParent(LayoutNode node, PaneView pane)
    {
        if (node.First is { } first)
        {
            if (ReferenceEquals(first.Pane, pane))
            {
                return node;
            }
            if (FindParent(first, pane) is { } found)
            {
                return found;
            }
        }
        if (node.Second is { } second)
        {
            if (ReferenceEquals(second.Pane, pane))
            {
                return node;
            }
            if (FindParent(second, pane) is { } found)
            {
                return found;
            }
        }
        return null;
    }

    /// <summary>閉じるボタンの有効・無効を、いまのペイン数に合わせる。</summary>
    private void UpdateCloseButtons()
    {
        var canClose = CanClose;
        foreach (var pane in Panes)
        {
            pane.Band.CanClose = canClose;
        }
    }

    /// <summary>木の中の 1 か所を差し替える。</summary>
    private void ReplaceLeaf(LayoutNode leaf, LayoutNode node)
    {
        if (ReferenceEquals(leaf, _root))
        {
            _root = node;
            return;
        }
        Replace(_root, leaf, node);
    }

    private static void Replace(LayoutNode node, LayoutNode target, LayoutNode replacement)
    {
        if (node.First is { } first)
        {
            if (ReferenceEquals(first, target))
            {
                node.First = replacement;
                return;
            }
            Replace(first, target, replacement);
        }
        if (node.Second is { } second)
        {
            if (ReferenceEquals(second, target))
            {
                node.Second = replacement;
                return;
            }
            Replace(second, target, replacement);
        }
    }

    private static LayoutNode? Find(LayoutNode node, PaneView pane)
    {
        if (ReferenceEquals(node.Pane, pane))
        {
            return node;
        }
        return (node.First is null ? null : Find(node.First, pane))
            ?? (node.Second is null ? null : Find(node.Second, pane));
    }

    /// <summary>仕切りのドラッグ。画面座標を、その節が占めている範囲の中での比に直す。</summary>
    private void OnSplitterDragged(LayoutNode node, POINT screen)
    {
        var local = screen;
        ScreenToClient(_parent, ref local);
        var area = BoundsOf(_root, _bounds, node);
        if (area is not { } rect)
        {
            return;
        }
        var thickness = Scale(SplitterView.Thickness, _dpi);
        var ratio = node.Direction == SplitDirection.Vertical
            ? (double)(local.X - rect.Left) / Math.Max(1, rect.Width - thickness)
            : (double)(local.Y - rect.Top) / Math.Max(1, rect.Height - thickness);

        // 端まで寄せると片方が潰れて操作できなくなるので、少し余裕を残す
        node.Ratio = Math.Clamp(ratio, 0.1, 0.9);
        Arrange(_root, _bounds);
    }

    /// <summary>ある節がいま占めている矩形。ドラッグ中の比の計算に使う。</summary>
    private RECT? BoundsOf(LayoutNode node, RECT bounds, LayoutNode target)
    {
        if (ReferenceEquals(node, target))
        {
            return bounds;
        }
        if (node.First is not { } first || node.Second is not { } second)
        {
            return null;
        }
        var thickness = Scale(SplitterView.Thickness, _dpi);
        if (node.Direction == SplitDirection.Vertical)
        {
            var usable = Math.Max(0, bounds.Width - thickness);
            var width = (int)Math.Round(usable * node.Ratio);
            return BoundsOf(first, bounds with { Right = bounds.Left + width }, target)
                ?? BoundsOf(second, bounds with { Left = bounds.Left + width + thickness }, target);
        }
        var usableY = Math.Max(0, bounds.Height - thickness);
        var height = (int)Math.Round(usableY * node.Ratio);
        return BoundsOf(first, bounds with { Bottom = bounds.Top + height }, target)
            ?? BoundsOf(second, bounds with { Top = bounds.Top + height + thickness }, target);
    }

    internal void Dispose()
    {
        foreach (var pane in Panes)
        {
            pane.Dispose();
        }
    }
}
