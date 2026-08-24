using System.Runtime.InteropServices;
using ExtendExprorer.Interop;
using ExtendExprorer.Services;
using static ExtendExprorer.Interop.TreeViewControl;
using static ExtendExprorer.Interop.Win32;

namespace ExtendExprorer.UI;

/// <summary>ウィンドウ左のフォルダツリー（<c>SysTreeView32</c>）。
/// ルートは「ホーム」＋準備完了ドライブ、枝は展開時に初めて列挙する。
///
/// <para><b>一覧と違ってオーナーデータは無い。</b>ツリーの項目は文字列も含めて
/// コントロールが持つので、展開した分だけ実体が増える。遅延展開を守ることが
/// そのままメモリの話になる（<c>docs/win32-migration/design.md</c>）。</para>
///
/// <para><b>行の高さは指定しない。</b>フォントと 16px のイメージリストから
/// コントロールに決めさせる。旧版の BUG-014（行ピッチ 19px・中身 7px）は
/// 高さを固定したことが原因だった。</para></summary>
internal sealed unsafe class FolderTreeView
{
    /// <summary>1 ノード分。<c>HTREEITEM</c> をキーにした表で引く。
    /// マネージドの参照をネイティブの <c>lParam</c> へ預けない（AOT で固定が要るうえ、
    /// 解放し忘れがそのまま漏れになる）。</summary>
    private sealed class Node
    {
        internal required string Path { get; init; }
        internal required bool IsHiddenOrSystem { get; init; }
        internal nint Item { get; set; }

        /// <summary>子を列挙済みか。展開のたびに読み直さないための印。</summary>
        internal bool Loaded { get; set; }

        internal bool Loading { get; set; }
    }

    private readonly IFileSystemService _fs;
    private readonly Dictionary<nint, Node> _nodes = [];
    private nint _hwnd;
    private uint _dpi = 96;

    /// <summary>ノードがクリック（または Enter）された。引数は移動先のフルパス。</summary>
    internal event Action<string>? FolderInvoked;

    internal nint Handle => _hwnd;

    internal FolderTreeView(IFileSystemService fs) => _fs = fs;

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

        // 三角のシェブロン・ホバー・選択の塗りはここで決まる。当てないと +/- の四角になる
        SetWindowTheme(_hwnd, "Explorer", null);
        SendMessageW(_hwnd, WM_SETFONT, font, 1);

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
        // 借り物を返してから壊す。付けたままだと OS 共有のイメージリストを
        // 道連れにしかねず、そうなるとアイコンがプロセス全体で壊れる
        SendMessageW(_hwnd, TVM_SETIMAGELIST, TVSIL_NORMAL, 0);
        DestroyWindow(_hwnd);
        _hwnd = 0;
        _nodes.Clear();
    }

    /// <summary>ルート（ホーム＋準備完了ドライブ）を作る。<c>IsReady</c> はドライブに
    /// 実アクセスするので UI スレッドで回さない。</summary>
    private void LoadRoots()
    {
        var home = _fs.HomePath;
        _ = Task.Run(() =>
        {
            List<string> drives;
            try
            {
                drives = DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.Name).ToList();
            }
            catch (Exception ex)
            {
                Diagnostics.Report("FolderTreeView.LoadRoots", ex);
                drives = [];
            }
            UiDispatcher.Post(() =>
            {
                Insert(TVI_ROOT, "ホーム", home, isHiddenOrSystem: false, ShellImageList.IndexOfPath(home));
                foreach (var drive in drives)
                {
                    // 末尾の \ を落とすと「C:」になり、エクスプローラーの表記と離れる。そのまま出す
                    Insert(TVI_ROOT, drive, drive, isHiddenOrSystem: false, ShellImageList.IndexOfPath(drive));
                }
            });
        });
    }

    /// <summary>ノードを 1 つ足す。子がいるかは開くまで分からないので、
    /// いったんシェブロンを出しておき（<c>cChildren = 1</c>）、
    /// 開いて 0 件だったところで消す。</summary>
    private nint Insert(nint parent, string name, string path, bool isHiddenOrSystem, int image)
    {
        var node = new Node { Path = path, IsHiddenOrSystem = isHiddenOrSystem };
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
                    cChildren = 1,
                },
            };
            item = SendMessageW(_hwnd, TVM_INSERTITEMW, 0, (nint)(&insert));
        }
        if (item != 0)
        {
            node.Item = item;
            _nodes[item] = node;
        }
        return item;
    }

    /// <summary>ウィンドウからの通知の振り分け。自分宛てでなければ何もしない。</summary>
    internal bool TryHandleNotify(ListView.NMHDR* header, out nint result)
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
    private nint OnExpanding(NMTREEVIEWW* notify)
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
            _ = LoadChildrenAsync(node);
        }
        return 1; // 読み終わるまでは開かない
    }

    private async Task LoadChildrenAsync(Node node)
    {
        IReadOnlyList<Models.Entry> directories;
        try
        {
            directories = await _fs.ListDirectoriesAsync(node.Path);
        }
        catch (Exception ex)
        {
            // 読めないフォルダは「子 0 件」として扱う（ダイアログは出さない）
            Diagnostics.Report($"FolderTreeView.LoadChildren({node.Path})", ex);
            directories = [];
        }
        UiDispatcher.Post(() =>
        {
            node.Loading = false;
            node.Loaded = true;
            if (_hwnd == 0)
            {
                return;
            }
            foreach (var directory in directories)
            {
                Insert(node.Item, directory.Name,
                    System.IO.Path.Combine(node.Path, directory.Name),
                    directory.IsHiddenOrSystem, ShellImageList.Folder);
            }
            if (directories.Count == 0)
            {
                // 子がいなかった。シェブロンを消して「これ以上は無い」を示す
                SetChildCount(node.Item, 0);
                return;
            }
            SendMessageW(_hwnd, TVM_EXPAND, TVE_EXPAND, node.Item);
        });
    }

    private void SetChildCount(nint item, int count)
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
    private void OnClick()
    {
        if (!GetCursorPos(out var point))
        {
            return;
        }
        ScreenToClient(_hwnd, ref point);
        var hit = new TVHITTESTINFO { pt = point };
        var item = SendMessageW(_hwnd, TVM_HITTEST, 0, (nint)(&hit));
        if (item != 0 && (hit.flags & TVHT_ONITEM) != 0)
        {
            Invoke(item);
        }
    }

    private nint SelectedItem => SendMessageW(_hwnd, TVM_GETNEXTITEM, TVGN_CARET, 0);

    private const nint TVGN_CARET = 9;

    private void Invoke(nint item)
    {
        if (item != 0 && _nodes.TryGetValue(item, out var node))
        {
            FolderInvoked?.Invoke(node.Path);
        }
    }

    /// <summary>隠し・システム属性のフォルダを薄色にする（一覧と同じ規則）。
    /// ツリーには列が無いので、行の段階で色を差し替えるだけでよい。</summary>
    private nint CustomDraw(NMTVCUSTOMDRAW* draw)
    {
        switch (draw->nmcd.dwDrawStage)
        {
            case ListView.CDDS_PREPAINT:
                return ListView.CDRF_NOTIFYITEMDRAW;

            case ListView.CDDS_ITEMPREPAINT:
                var item = draw->nmcd.dwItemSpec;
                if (_nodes.TryGetValue(item, out var node) && node.IsHiddenOrSystem)
                {
                    draw->clrText = FileListView.DimmedTextColor;
                    return ListView.CDRF_NEWFONT;
                }
                return ListView.CDRF_DODEFAULT;
        }
        return ListView.CDRF_DODEFAULT;
    }

    /// <summary>実機でしか分からない数字を残す（<c>--diag</c>）。旧版の BUG-014 は
    /// 「行ピッチだけ見て中身の高さを見なかった」ことで 2 セッション見逃した。</summary>
    internal void WriteDiagnostics()
    {
        if (_hwnd == 0)
        {
            return;
        }
        var height = SendMessageW(_hwnd, TVM_GETITEMHEIGHT, 0, 0);
        Diagnostics.Write($"[tree] 行の高さ={height} dpi={_dpi} ノード数={_nodes.Count}");
    }
}
