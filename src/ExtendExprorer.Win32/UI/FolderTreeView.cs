using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ExtendExprorer.Interop;
using ExtendExprorer.Services;
using static ExtendExprorer.Interop.TreeViewControl;
using static ExtendExprorer.Interop.Win32;

namespace ExtendExprorer.UI;

/// <summary>ウィンドウ左のフォルダツリー（<c>SysTreeView32</c>）。
/// <b>根はシェルの名前空間の根</b>（＝エクスプローラーのナビゲーションウィンドウと
/// 同じ顔ぶれ。2026-09-13 に「ホーム＋ドライブ」から変えた）。枝は展開時に初めて列挙する。
///
/// <para><b>ノードはパス文字列ではなく PIDL で持つ。</b><c>PC</c> や <c>ネットワーク</c> は
/// ファイルシステム上のパスを持たないので、パスでは表せない。
/// パスを持つノードだけがクリックで移動でき、持たないノードは展開するだけになる
/// （一覧ペインはパスでフォルダを開く作りなので、開く先が無い）。</para>
///
/// <para><b>一覧と違ってオーナーデータは無い。</b>ツリーの項目は文字列も含めて
/// コントロールが持つので、展開した分だけ実体が増える。遅延展開を守ることが
/// そのままメモリの話になる（<c>docs/win32-migration/design.md</c>）。</para>
///
/// <para><b>行の高さは指定しない。</b>フォントと 16px のイメージリストから
/// コントロールに決めさせる。旧版の BUG-014（行ピッチ 19px・中身 7px）は
/// 高さを固定したことが原因だった。</para></summary>
internal sealed class FolderTreeView
{
    /// <summary>1 ノード分。<c>HTREEITEM</c> をキーにした表で引く。
    /// マネージドの参照をネイティブの <c>lParam</c> へ預けない（AOT で固定が要るうえ、
    /// 解放し忘れがそのまま漏れになる）。</summary>
    private sealed class Node
    {
        /// <summary>絶対 PIDL。<b>このノードが持ち主</b>なので、捨てるときに必ず解放する
        /// （<c>Ctrl+Shift+G</c> はマネージドしか数えないので、ここの漏れは捕まらない）。</summary>
        internal required nint Pidl { get; init; }

        /// <summary>ファイルシステム上のパス。<b><c>PC</c> や <c>ネットワーク</c> は null。</b>
        /// null のノードはクリックしても移動せず、その場で展開する。</summary>
        internal required string? Path { get; init; }

        internal required bool IsHiddenOrSystem { get; init; }
        internal nint Item { get; set; }

        /// <summary>子を列挙済みか。展開のたびに読み直さないための印。</summary>
        internal bool Loaded { get; set; }

        internal bool Loading { get; set; }
    }

    private readonly Dictionary<nint, Node> _nodes = [];
    private nint _hwnd;
    private uint _dpi = 96;

    /// <summary>ノードがクリック（または Enter）された。引数は移動先のフルパス。</summary>
    internal event Action<string>? FolderInvoked;

    internal nint Handle => _hwnd;

    /// <summary>ファイルシステムのサービスは使わない。
    /// 根も枝もシェルの名前空間から引くようになったため（2026-09-13）。</summary>
    internal FolderTreeView()
    {
    }

    internal void Create(nint parent, nint instance, RECT bounds, nint font, uint dpi)
    {
        _dpi = dpi;

        // TVS_HASLINES は入れない（エクスプローラーのナビゲーションウィンドウに連結線は無い）。
        // シェブロンを根の項目にも出すには TVS_LINESATROOT が要る（線の有無とは別の話）
        _hwnd = CreateWindowExW(0, WC_TREEVIEW, null,
            WS_CHILD | WS_VISIBLE | WS_TABSTOP
            | TVS_HASBUTTONS | TVS_LINESATROOT | TVS_SHOWSELALWAYS
            | TVS_TRACKSELECT | TVS_FULLROWSELECT,
            bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            parent, 0, instance, 0);
        if (_hwnd == 0)
        {
            throw new InvalidOperationException($"CreateWindowEx({WC_TREEVIEW}) failed: {Marshal.GetLastPInvokeError()}");
        }

        // 三角のシェブロン・選択の塗りはここで決まる。当てないと +/- の四角になる
        SetWindowTheme(_hwnd, "Explorer", null);
        SendMessageW(_hwnd, WM_SETFONT, font, 1);

        // ホバーの強調は TVS_TRACKSELECT が出す。テーマだけでは出ない（実機で確認）。
        // ただしこのスタイルは項目をリンク扱いにするので、カーソルまで指の形になる。
        // エクスプローラーは矢印なので、WM_SETCURSOR だけ横取りして矢印に戻す（BUG-024）
        Subclass();
        SendMessageW(_hwnd, TVM_SETEXTENDEDSTYLE, TVS_EX_DOUBLEBUFFER, TVS_EX_DOUBLEBUFFER);

        // OS の共有イメージリストを借りる。一覧（LVS_SHAREIMAGELISTS）と違って
        // ツリーには「共有」を伝えるスタイルが無いので、破棄の前に自分で外す
        SendMessageW(_hwnd, TVM_SETIMAGELIST, TVSIL_NORMAL, ShellImageList.Handle);

        LoadRoots();
    }

    internal void SetBounds(RECT bounds)
    {
        if (_hwnd != 0)
        {
            MoveWindow(_hwnd, bounds.Left, bounds.Top, bounds.Width, bounds.Height, repaint: true);
        }
    }

    internal void SetFont(nint font, uint dpi)
    {
        _dpi = dpi;
        if (_hwnd != 0)
        {
            SendMessageW(_hwnd, WM_SETFONT, font, 1);
        }
    }

    internal void Show(bool visible)
    {
        if (_hwnd != 0)
        {
            ShowWindow(_hwnd, visible ? SW_SHOW : SW_HIDE);
        }
    }

    internal void Destroy()
    {
        if (_hwnd == 0)
        {
            return;
        }
        Unsubclass();
        // 借り物を返してから壊す。付けたままだと OS 共有のイメージリストを
        // 道連れにしかねず、そうなるとアイコンがプロセス全体で壊れる
        SendMessageW(_hwnd, TVM_SETIMAGELIST, TVSIL_NORMAL, 0);
        DestroyWindow(_hwnd);
        _hwnd = 0;
        // ★ ノードごとに PIDL を持っている。表を消すだけでは返らない
        var expected = _nodes.Count;
        foreach (var node in _nodes.Values)
        {
            ShellNamespace.Free(node.Pidl);
        }
        _nodes.Clear();
        // ★ 片付けの経路はここ 1 本しかないので、**ここで数えないと確かめる機会が無い。**
        //   残っているはずの数が 0 でなければ、どこかで持ち主が食い違っている
        var (held, freed) = ShellNamespace.PidlCounters;
        Diagnostics.Write($"[tree] 片付け ノード={expected} 解放累計={freed} 残り={held}（残りが 0 であること）");
    }

    // --- サブクラス化（カーソルだけ横取りする） ---

    /// <summary>差し替えたウィンドウプロシージャから自分に戻るための表。</summary>
    private static readonly Dictionary<nint, FolderTreeView> Trees = [];

    private nint _originalProc;

    private unsafe void Subclass()
    {
        Trees[_hwnd] = this;
        _originalProc = SetWindowLongPtrW(_hwnd, GWLP_WNDPROC,
            (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&TreeProc);
    }

    private void Unsubclass()
    {
        if (_originalProc != 0)
        {
            SetWindowLongPtrW(_hwnd, GWLP_WNDPROC, _originalProc);
            _originalProc = 0;
        }
        Trees.Remove(_hwnd);
    }

    /// <summary>ツリー本来のプロシージャの手前。<c>WM_SETCURSOR</c> だけ横取りして、
    /// <c>TVS_TRACKSELECT</c> が付ける指のカーソルを矢印に戻す。
    /// それ以外は素通しする（例外はここで止めること）。</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint TreeProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        nint original = 0;
        try
        {
            if (!Trees.TryGetValue(hwnd, out var tree))
            {
                return DefWindowProcW(hwnd, msg, wParam, lParam);
            }
            original = tree._originalProc;
            if (msg == WM_SETCURSOR)
            {
                SetCursor(LoadCursorW(0, IDC_ARROW));
                return 1;
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Report($"FolderTree.TreeProc(0x{msg:X4})", ex);
        }
        return original != 0
            ? CallWindowProcW(original, hwnd, msg, wParam, lParam)
            : DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    /// <summary>根（シェルの名前空間の根）を作る。列挙は専用スレッドで回す。</summary>
    private void LoadRoots()
    {
        ShellNamespace.EnumerateAsync(0, items =>
        {
            if (_hwnd == 0)
            {
                // 窓がもう無い。受け取った PIDL はここで返す（持ち主はこちらに移っている）
                foreach (var item in items)
                {
                    ShellNamespace.Free(item.Pidl);
                }
                return;
            }
            foreach (var item in items)
            {
                Insert(TVI_ROOT, item);
            }
            // ★ 根の顔ぶれをそのまま出す。SHCONTF_NAVIGATION_PANE が本当に
            //   エクスプローラーと同じ集合を返すかは、実機でしか確かめられない
            Diagnostics.Write($"[tree] 根 {items.Count} 件: "
                + string.Join(" / ", items.Select(i =>
                    $"{i.Name}[{(i.Path is null ? "パス無し" : "パス有り")}{(i.HasChildren ? "・子有り" : string.Empty)}{(i.IsHidden ? "・隠し" : string.Empty)}]")));
        });
    }

    /// <summary>ノードを 1 つ足す。<b>シェブロンはシェルに聞いた答え</b>
    /// （<c>SFGAO_HASSUBFOLDER</c>）で決める——以前は常に出しておいて、
    /// 開いて 0 件だったところで消していた。</summary>
    private unsafe nint Insert(nint parent, ShellNamespace.Item source)
    {
        var name = source.Name;
        var image = source.Icon;
        var node = new Node
        {
            Pidl = source.Pidl,
            Path = source.Path,
            IsHiddenOrSystem = source.IsHidden,
        };
        nint item;
        fixed (char* text = name)
        {
            var insert = new TVINSERTSTRUCTW
            {
                hParent = parent,
                hInsertAfter = TVI_LAST,
                item = new TVITEMEXW
                {
                    mask = TVIF_TEXT | TVIF_IMAGE | TVIF_SELECTEDIMAGE | TVIF_CHILDREN,
                    pszText = (nint)text,
                    iImage = image,
                    iSelectedImage = image,
                    cChildren = source.HasChildren ? 1 : 0,
                },
            };
            item = SendMessageW(_hwnd, TVM_INSERTITEMW, 0, (nint)(&insert));
        }
        if (item != 0)
        {
            node.Item = item;
            _nodes[item] = node;
        }
        else
        {
            // 挿せなかった。持ち主になった PIDL をここで返す
            ShellNamespace.Free(source.Pidl);
        }
        return item;
    }

    /// <summary>ウィンドウからの通知の振り分け。自分宛てでなければ何もしない。</summary>
    internal unsafe bool TryHandleNotify(ListView.NMHDR* header, out nint result)
    {
        result = 0;
        if (_hwnd == 0 || header->hwndFrom != _hwnd)
        {
            return false;
        }
        switch (header->code)
        {
            case TVN_ITEMEXPANDINGW:
                result = OnExpanding((NMTREEVIEWW*)header);
                return true;

            case NM_CLICK:
                OnClick();
                return true;

            case NM_RETURN:
                Invoke(SelectedItem);
                return true;

            case ListView.NM_CUSTOMDRAW:
                result = CustomDraw((NMTVCUSTOMDRAW*)header);
                return true;
        }
        return false;
    }

    /// <summary>展開の直前。まだ読んでいなければ、<b>この場では開かせずに</b>列挙を始め、
    /// 揃ってから開く。プレースホルダの子を挿しておく方式にしないのは、
    /// 一瞬だけ空の子が見えるのと、消し忘れが選択の位置ずれになるため。</summary>
    private unsafe nint OnExpanding(NMTREEVIEWW* notify)
    {
        if (notify->action != (uint)TVE_EXPAND)
        {
            return 0;
        }
        var item = notify->itemNew.hItem;
        if (!_nodes.TryGetValue(item, out var node) || node.Loaded)
        {
            return 0;
        }
        if (!node.Loading)
        {
            node.Loading = true;
            LoadChildren(node);
        }
        return 1; // 読み終わるまでは開かない
    }

    private void LoadChildren(Node node)
    {
        ShellNamespace.EnumerateAsync(node.Pidl, items =>
        {
            node.Loading = false;
            node.Loaded = true;
            if (_hwnd == 0 || !_nodes.ContainsKey(node.Item))
            {
                // 窓が無いか、待っている間にこのノードが消えた。
                // 受け取った PIDL はここで返す（持ち主はこちらに移っている）
                foreach (var item in items)
                {
                    ShellNamespace.Free(item.Pidl);
                }
                return;
            }
            foreach (var item in items)
            {
                Insert(node.Item, item);
            }
            if (items.Count == 0)
            {
                // 子がいなかった。シェブロンを消して「これ以上は無い」を示す
                SetChildCount(node.Item, 0);
            }
            else
            {
                SendMessageW(_hwnd, TVM_EXPAND, TVE_EXPAND, node.Item);
            }
            // 展開のたびに数字を出す。起動 3 秒後の 1 回だけだと、その時点では
            // ルートしか無く「隠し/システム=0」しか取れない（確認側からの指摘）
            if (Diagnostics.Enabled)
            {
                WriteDiagnostics();
            }
        });
    }

    private unsafe void SetChildCount(nint item, int count)
    {
        var update = new TVITEMW
        {
            mask = TVIF_HANDLE | TVIF_CHILDREN,
            hItem = item,
            cChildren = count,
        };
        SendMessageW(_hwnd, TVM_SETITEMW, 0, (nint)(&update));
    }

    /// <summary>クリック。シェブロンを押しただけのときは移動しない。</summary>
    private unsafe void OnClick()
    {
        if (!GetCursorPos(out var point))
        {
            return;
        }
        ScreenToClient(_hwnd, ref point);
        var hit = new TVHITTESTINFO { pt = point };
        var item = SendMessageW(_hwnd, TVM_HITTEST, 0, (nint)(&hit));
        if (item != 0 && (hit.flags & (TVHT_ONITEM | TVHT_ONITEMRIGHT)) != 0)
        {
            Invoke(item);
        }
    }

    private nint SelectedItem => SendMessageW(_hwnd, TVM_GETNEXTITEM, TVGN_CARET, 0);

    private const nint TVGN_CARET = 9;

    /// <summary>ノードのクリック（と Enter）。
    ///
    /// <para><b>移動できるのは、開ける場所を持つノードだけ。</b>
    /// <c>SFGAO_FILESYSTEM</c> が立っていてもパスが開けないもの（コントロールパネルの
    /// 一部など）があるので、<c>Directory.Exists</c> と<b>両方</b>見る。
    /// 片方だけで通すと、一覧に開けないパスを渡すことになり、
    /// BUG-020（「空です」と「アクセス拒否」が見分けられない）と同じ形になる。</para>
    ///
    /// <para>移動できないノードは<b>その場で展開する</b>。
    /// エラーにはしない——<c>PC</c> をクリックしたらドライブが出る、という動きになる。</para></summary>
    private void Invoke(nint item)
    {
        if (item == 0 || !_nodes.TryGetValue(item, out var node))
        {
            return;
        }
        if (node.Path is { Length: > 0 } path && Directory.Exists(path))
        {
            FolderInvoked?.Invoke(path);
            return;
        }
        SendMessageW(_hwnd, TVM_EXPAND, TVE_EXPAND, item);
    }

    /// <summary>隠し・システム属性のフォルダを薄色にする（一覧と同じ規則）。
    /// ツリーには列が無いので、行の段階で色を差し替えるだけでよい。</summary>
    private unsafe nint CustomDraw(NMTVCUSTOMDRAW* draw)
    {
        switch (draw->nmcd.dwDrawStage)
        {
            case ListView.CDDS_PREPAINT:
                return ListView.CDRF_NOTIFYITEMDRAW;

            case ListView.CDDS_ITEMPREPAINT:
                _drawItems++;
                var item = draw->nmcd.dwItemSpec;
                if (_nodes.TryGetValue(item, out var node) && node.IsHiddenOrSystem)
                {
                    draw->clrText = FileListView.DimmedTextColor;
                    _drawDimmed++;
                    return ListView.CDRF_NEWFONT;
                }
                return ListView.CDRF_DODEFAULT;
        }
        return ListView.CDRF_DODEFAULT;
    }

    /// <summary>実機でしか分からない数字を残す（<c>--diag</c>）。
    ///
    /// <para><b>行ピッチだけでは足りない。</b>旧版の BUG-014 は「ピッチは 19px なのに
    /// 中身が 7px」だったので、ピッチと並べて<b>アイコンの大きさと文字の高さ</b>も出す。
    /// ログだけで「ピッチ ≧ 中身」が確かめられる形にしておく。</para>
    ///
    /// <para>薄色表示の数え上げも出す。BUG-019 は「通知が来ていないのか、条件が
    /// 合っていないのか」を切り分けられずに 3 往復した。対象が 0 件だったのか、
    /// 通知が来なかったのか、条件で落ちたのかが 1 行で分かるようにする。</para></summary>
    internal void WriteDiagnostics()
    {
        if (_hwnd == 0)
        {
            return;
        }
        var pitch = SendMessageW(_hwnd, TVM_GETITEMHEIGHT, 0, 0);
        ImageList_GetIconSize(ShellImageList.Handle, out var iconWidth, out var iconHeight);
        Diagnostics.Write($"[tree] 行ピッチ={pitch} アイコン={iconWidth}x{iconHeight} " +
            $"文字高={TextHeight()} dpi={_dpi}");
        Diagnostics.Write($"[tree] ノード数={_nodes.Count} " +
            $"隠し/システム={_nodes.Values.Count(n => n.IsHiddenOrSystem)} " +
            $"パス無し={_nodes.Values.Count(n => n.Path is null)} " +
            $"行の描画通知={_drawItems} 薄色にした={_drawDimmed}");
        // ★ PIDL はノードごとに持つネイティブの記憶。Ctrl+Shift+G では数えられないので、
        //   「持っている数」がノード数と一致しているかをここで見る
        var (held, freed) = ShellNamespace.PidlCounters;
        Diagnostics.Write($"[tree] PIDL 保持={held} 解放={freed}（ノード数と保持が一致していること）");
    }

    /// <summary>ツリーのフォントでの文字の高さ。行ピッチがこれを下回っていたら
    /// 中身が潰れている。</summary>
    private int TextHeight()
    {
        var hdc = GetDC(_hwnd);
        if (hdc == 0)
        {
            return -1;
        }
        try
        {
            var font = SendMessageW(_hwnd, WM_GETFONT, 0, 0);
            var old = font != 0 ? SelectObject(hdc, font) : 0;
            // 日本語を混ぜる（英字だけだと下端の余裕を見誤る）
            var rect = default(RECT);
            DrawTextW(hdc, "Agドキュメント", -1, ref rect, DT_CALCRECT | DT_SINGLELINE | DT_NOPREFIX);
            if (old != 0)
            {
                SelectObject(hdc, old);
            }
            return rect.Height;
        }
        finally
        {
            ReleaseDC(_hwnd, hdc);
        }
    }

    private int _drawItems;
    private int _drawDimmed;
}
