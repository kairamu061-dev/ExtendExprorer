using ExtendExprorer.Interop;
using ExtendExprorer.Models;
using ExtendExprorer.Services;
using ExtendExprorer.ViewModels;
using static ExtendExprorer.Interop.ListView;
using static ExtendExprorer.Interop.Win32;

namespace ExtendExprorer.UI;

/// <summary>ファイル一覧。<c>SysListView32</c> の詳細表示を<b>オーナーデータ</b>
/// （<c>LVS_OWNERDATA</c>）で動かす。
///
/// <para>項目の実体はコントロールに渡さない。行数だけ伝えておき、描画に必要な行の内容を
/// <c>LVN_GETDISPINFOW</c> で聞かれたときに作る。1 万件のフォルダでも、実際に作る文字列は
/// 画面に見えている数十行分で済む（現行 WinUI 版は行ごとの ViewModel と整形済み文字列を
/// 全件ぶん抱えていた）。</para>
///
/// <para><b>通知の処理中に一覧へメッセージを送ると、その場で再入してくる</b>点に注意。
/// <c>LVM_SETITEMCOUNT</c> は戻る前に <c>LVN_GETDISPINFOW</c> を呼び返すので、
/// 配列の差し替えを先に済ませてから行数を伝えること。</para></summary>
internal sealed unsafe class FileListView
{
    private readonly FileListViewModel _model;
    private nint _hwnd;

    /// <summary>列（名前・更新日時・種類・サイズ）。並びは <see cref="SortColumn"/> と揃えてある
    /// （ヘッダの列番号をそのまま並べ替えの指定に使うため）。</summary>
    private static readonly (string Title, int Width, int Format)[] Columns =
    [
        ("名前", 280, LVCFMT_LEFT),
        ("更新日時", 130, LVCFMT_LEFT),
        ("種類", 100, LVCFMT_LEFT),
        ("サイズ", 90, LVCFMT_RIGHT),
    ];

    internal nint Handle => _hwnd;

    internal FileListView(FileListViewModel model)
    {
        _model = model;
        _model.EntriesChanging += OnEntriesChanging;
        _model.EntriesReset += OnEntriesReset;
        _model.EntryAdded += OnEntryAdded;
        _model.EntryRemoved += OnEntryRemoved;
        _model.EntryUpdated += OnEntryUpdated;
    }

    internal void Create(nint parent, nint instance, RECT bounds, nint font, uint dpi)
    {
        _hwnd = CreateWindowExW(0, WC_LISTVIEW, null,
            WS_CHILD | WS_VISIBLE | WS_TABSTOP |
            LVS_REPORT | LVS_OWNERDATA | LVS_SHAREIMAGELISTS | LVS_SHOWSELALWAYS,
            bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            parent, 0, instance, 0);
        if (_hwnd == 0)
        {
            throw new InvalidOperationException(
                $"CreateWindowEx({WC_LISTVIEW}) failed: {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}");
        }

        // エクスプローラーと同じ見た目（選択の塗り・ヘッダのホバー）にする
        SetWindowTheme(_hwnd, "Explorer", null);
        SendMessageW(_hwnd, WM_SETFONT, font, 1);
        SendMessageW(_hwnd, LVM_SETEXTENDEDLISTVIEWSTYLE, 0,
            (nint)(LVS_EX_FULLROWSELECT | LVS_EX_DOUBLEBUFFER | LVS_EX_LABELTIP | LVS_EX_HEADERDRAGDROP));

        // アイコンは OS のシステムイメージリストを借りる（破棄しない）
        var imageList = ShellImageList.Handle;
        if (imageList != 0)
        {
            SendMessageW(_hwnd, LVM_SETIMAGELIST, (nint)LVSIL_SMALL, imageList);
        }

        InsertColumns(dpi);
    }

    private void InsertColumns(uint dpi)
    {
        for (var i = 0; i < Columns.Length; i++)
        {
            var (title, width, format) = Columns[i];
            fixed (char* text = title)
            {
                var column = new LVCOLUMNW
                {
                    mask = LVCF_TEXT | LVCF_WIDTH | LVCF_SUBITEM | LVCF_FMT,
                    fmt = format,
                    cx = Scale(width, dpi),
                    pszText = (nint)text,
                    iSubItem = i,
                };
                SendMessageW(_hwnd, LVM_INSERTCOLUMNW, i, (nint)(&column));
            }
        }
        UpdateSortIndicator();
    }

    internal void SetBounds(RECT bounds)
    {
        if (_hwnd != 0)
        {
            MoveWindow(_hwnd, bounds.Left, bounds.Top, bounds.Width, bounds.Height, repaint: true);
        }
    }

    internal void Focus()
    {
        if (_hwnd != 0)
        {
            SetFocus(_hwnd);
        }
    }

    // --- モデルからの通知 ---

    /// <summary>一覧が変わる<b>直前</b>に、選択中の項目名を控える。
    /// 変わった後では「その行番号に今いる項目」＝別のファイルしか分からない（BUG-017）。</summary>
    private void OnEntriesChanging() => _selectionSnapshot = CaptureSelection();

    private List<string> _selectionSnapshot = [];

    private List<string> TakeSelectionSnapshot()
    {
        var snapshot = _selectionSnapshot;
        _selectionSnapshot = [];
        return snapshot;
    }

    private void OnEntriesReset(bool keepSelection)
    {
        // 別のフォルダへ移動したときは引き継がない（同名のファイルが選ばれてしまう）
        var selected = TakeSelectionSnapshot();
        SetItemCount(_model.Entries.Count, keepPosition: false);
        ClearSelection();
        if (keepSelection)
        {
            RestoreSelection(selected);
        }
        UpdateSortIndicator();
        InvalidateRect(_hwnd, 0, erase: true);
    }

    /// <summary>追加は末尾なので、既存の行番号は動かない（選択の付け直しは要らない）。
    /// ただし行数を伝えるだけでは新しい行が描かれないので、そこだけ描き直す。</summary>
    private void OnEntryAdded(int index)
    {
        SetItemCount(_model.Entries.Count, keepPosition: true);
        RedrawFrom(index);
    }

    private void OnEntryRemoved(int index)
    {
        // 消えた行より後ろの行番号がずれるので、ここだけは選択を付け直す
        // （控えは EntriesChanging で、消される前に取ってある）
        var selected = TakeSelectionSnapshot();
        SetItemCount(_model.Entries.Count, keepPosition: true);
        ClearSelection();
        RestoreSelection(selected);
        RedrawFrom(index);
    }

    /// <summary>リネームは位置を保ったまま名前だけ差し替わる（行数も行番号も動かない）。</summary>
    private void OnEntryUpdated(int index) => RedrawFrom(index, index);

    private void SetItemCount(int count, bool keepPosition)
    {
        if (_hwnd == 0)
        {
            return;
        }
        // 配列の差し替えは呼び出し元で済んでいること。このメッセージは戻る前に
        // LVN_GETDISPINFOW を呼び返してくる
        var flags = keepPosition ? LVSICF_NOINVALIDATEALL | LVSICF_NOSCROLL : 0;
        SendMessageW(_hwnd, LVM_SETITEMCOUNT, count, flags);
    }

    private void RedrawFrom(int first, int last = int.MaxValue)
    {
        if (_hwnd == 0)
        {
            return;
        }
        var count = _model.Entries.Count;
        if (count == 0 || first >= count)
        {
            InvalidateRect(_hwnd, 0, erase: true);
            return;
        }
        SendMessageW(_hwnd, LVM_REDRAWITEMS, first, Math.Min(last, count - 1));
    }

    // --- 選択 ---

    /// <summary>選択中の項目名を控える。オーナーデータの選択は<b>行番号</b>で持たれているため、
    /// 並べ替えや読み直しで行が動くと、そのままでは別のファイルが選択された状態になる
    /// （現行 WinUI 版はオブジェクトの同一性で保たれていた部分）。</summary>
    private List<string> CaptureSelection()
    {
        var selected = new List<string>();
        if (_hwnd == 0)
        {
            return selected;
        }
        var entries = _model.Entries;
        var index = -1;
        while (true)
        {
            index = (int)SendMessageW(_hwnd, LVM_GETNEXTITEM, index, LVNI_SELECTED);
            if (index < 0)
            {
                break;
            }
            // 念のため範囲外は読み飛ばすだけにする（break すると後続の選択を取りこぼす）
            if ((uint)index < (uint)entries.Count)
            {
                selected.Add(entries[index].Name);
            }
        }
        return selected;
    }

    /// <summary>選択をすべて外す。<c>LVM_SETITEMCOUNT</c> は選択状態を消さないので、
    /// これを先に出さないと<b>古い行番号の選択が残ったまま</b>付け直しの分が足される。</summary>
    private void ClearSelection()
    {
        if (_hwnd == 0)
        {
            return;
        }
        var item = new LVITEMW { state = 0, stateMask = LVIS_SELECTED | LVIS_FOCUSED };
        SendMessageW(_hwnd, LVM_SETITEMSTATE, -1, (nint)(&item));
    }

    private void RestoreSelection(List<string> names)
    {
        if (_hwnd == 0 || names.Count == 0)
        {
            return;
        }
        var wanted = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var entries = _model.Entries;
        var item = new LVITEMW { state = LVIS_SELECTED, stateMask = LVIS_SELECTED };
        for (var i = 0; i < entries.Count; i++)
        {
            if (wanted.Contains(entries[i].Name))
            {
                SendMessageW(_hwnd, LVM_SETITEMSTATE, i, (nint)(&item));
            }
        }
    }

    // --- 通知の処理（親の WM_NOTIFY から回ってくる） ---

    internal bool TryHandleNotify(NMHDR* header, out nint result)
    {
        result = 0;
        if (header->hwndFrom != _hwnd)
        {
            return false;
        }
        switch (header->code)
        {
            case LVN_GETDISPINFOW:
                OnGetDispInfo((NMLVDISPINFOW*)header);
                return true;

            case LVN_COLUMNCLICK:
                var click = (NMLISTVIEW*)header;
                if ((uint)click->iSubItem < (uint)Columns.Length)
                {
                    _model.SetSort((SortColumn)click->iSubItem);
                }
                return true;

            case LVN_ITEMACTIVATE:
                Activate(((NMLISTVIEW*)header)->iItem);
                return true;

            case LVN_ODFINDITEMW:
                result = FindItem((NMLVFINDITEMW*)header);
                return true;

            case LVN_GETEMPTYMARKUP:
                result = SetEmptyMarkup((NMLVEMPTYMARKUP*)header);
                return true;

            case NM_CUSTOMDRAW:
                result = CustomDraw((NMLVCUSTOMDRAW*)header);
                return true;
        }
        return false;
    }

    /// <summary>描画に必要になった行の内容を渡す。オーナーデータの中心。</summary>
    private void OnGetDispInfo(NMLVDISPINFOW* info)
    {
        var entries = _model.Entries;
        var index = info->item.iItem;
        // 読み直しの直後などに、古い行番号で聞かれることがある
        if ((uint)index >= (uint)entries.Count)
        {
            return;
        }
        var entry = entries[index];

        // マスクを見てから触ること。画像だけ聞かれているときに pszText が有効とは限らない
        if ((info->item.mask & LVIF_TEXT) != 0)
        {
            CopyText(TextOf(entry, info->item.iSubItem), info->item.pszText, info->item.cchTextMax);
        }
        if ((info->item.mask & LVIF_IMAGE) != 0 && info->item.iSubItem == 0)
        {
            info->item.iImage = ShellImageList.IndexOf(_model.Path, entry);
        }
    }

    private static ReadOnlySpan<char> TextOf(Entry entry, int column) => column switch
    {
        1 => EntryFormat.ModifiedLabel(entry),
        2 => EntryFormat.TypeLabel(entry),
        3 => EntryFormat.SizeLabel(entry),
        _ => entry.Name,
    };

    /// <summary>コントロールが用意したバッファへ書き込む（<c>cchTextMax</c> は終端を含む文字数）。</summary>
    private static void CopyText(ReadOnlySpan<char> text, nint buffer, int max)
    {
        if (buffer == 0 || max <= 0)
        {
            return;
        }
        var destination = new Span<char>((void*)buffer, max);
        var length = Math.Min(text.Length, max - 1);
        text[..length].CopyTo(destination);
        destination[length] = '\0';
    }

    /// <summary>頭文字キーでの項目移動。オーナーデータでは一覧が中身を知らないので、
    /// 探すのはこちらの役目になる（これが無いと文字キーで選択が飛ばない）。</summary>
    private nint FindItem(NMLVFINDITEMW* find)
    {
        // フラグでは絞らない。頭文字入力のとき一覧が何を立ててくるかは版によって違い
        // （`LVFI_STRING` / `LVFI_PARTIAL` / Vista 以降の `LVFI_SUBSTRING`）、
        // `LVFI_STRING` を必須にしていたために無反応だった（BUG-018）。
        // 探す文字列さえ来ていれば前方一致で探す
        if (find->lvfi.psz == 0)
        {
            return -1;
        }
        var text = System.Runtime.InteropServices.Marshal.PtrToStringUni(find->lvfi.psz);
        if (string.IsNullOrEmpty(text))
        {
            return -1;
        }
        var entries = _model.Entries;
        if (entries.Count == 0)
        {
            return -1;
        }
        var start = Math.Max(0, find->iStart);
        // 見つからなければ先頭へ回り込む（エクスプローラーと同じ）
        for (var offset = 0; offset < entries.Count; offset++)
        {
            var index = (start + offset) % entries.Count;
            if (entries[index].Name.StartsWith(text, StringComparison.CurrentCultureIgnoreCase))
            {
                return index;
            }
        }
        return -1;
    }

    /// <summary>項目が無いときに一覧領域の中央へ出す文字列。エラーはここに出す（file-list 仕様）。</summary>
    private nint SetEmptyMarkup(NMLVEMPTYMARKUP* markup)
    {
        var text = _model.ErrorMessage ?? "このフォルダーは空です";
        markup->dwFlags = EMF_CENTERED;
        CopyText(text, (nint)markup->szMarkup, L_MAX_URL_LENGTH);
        return 1;
    }

    /// <summary>隠し・システム属性の行を薄色にする（file-list 仕様 1）。</summary>
    private nint CustomDraw(NMLVCUSTOMDRAW* draw)
    {
        switch (draw->nmcd.dwDrawStage)
        {
            case CDDS_PREPAINT:
                return CDRF_NOTIFYITEMDRAW;

            case CDDS_ITEMPREPAINT:
                if (!IsDimmed(draw))
                {
                    return CDRF_DODEFAULT;
                }
                draw->clrText = DimmedTextColor;
                // 詳細表示では列ごとに描画され、行で指定した色が列の描画で戻ってしまう。
                // 列単位の通知も受け取って各列で指定し直す（BUG-019）。
                // 色を変えたら CDRF_NEWFONT を返す（DC を触ったことを一覧に伝える）
                return CDRF_NEWFONT | CDRF_NOTIFYSUBITEMDRAW;

            case CDDS_ITEMPREPAINT | CDDS_SUBITEM:
                if (!IsDimmed(draw))
                {
                    return CDRF_DODEFAULT;
                }
                draw->clrText = DimmedTextColor;
                return CDRF_NEWFONT;
        }
        return CDRF_DODEFAULT;
    }

    /// <summary>この行を薄色で描くか。選択中の行は塗りつぶしの上に描かれるため、
    /// 読みにくくならないよう既定のままにする。</summary>
    private bool IsDimmed(NMLVCUSTOMDRAW* draw)
    {
        var index = (int)draw->nmcd.dwItemSpec;
        var entries = _model.Entries;
        return (uint)index < (uint)entries.Count
            && entries[index].IsHiddenOrSystem
            && (draw->nmcd.uItemState & CDIS_SELECTED) == 0;
    }

    /// <summary>本文と背景を混ぜた薄い文字色（現行版の不透明度 0.55 に相当）。</summary>
    private static uint DimmedTextColor
    {
        get
        {
            if (!_dimmedReady)
            {
                _dimmed = Blend(GetSysColor(COLOR_WINDOWTEXT), GetSysColor(COLOR_WINDOW), 0.55);
                _dimmedReady = true;
            }
            return _dimmed;
        }
    }

    private static uint _dimmed;
    private static bool _dimmedReady;

    private static uint Blend(uint foreground, uint background, double ratio)
    {
        static uint Channel(uint value, int shift) => (value >> shift) & 0xFF;
        var r = (uint)(Channel(foreground, 0) * ratio + Channel(background, 0) * (1 - ratio));
        var g = (uint)(Channel(foreground, 8) * ratio + Channel(background, 8) * (1 - ratio));
        var b = (uint)(Channel(foreground, 16) * ratio + Channel(background, 16) * (1 - ratio));
        return r | (g << 8) | (b << 16);
    }

    // --- 移動 ---

    /// <summary>ダブルクリック / Enter。フォルダは移動、ファイルは関連付けで開く（仕様 3・3b）。</summary>
    private void Activate(int index)
    {
        var entries = _model.Entries;
        if ((uint)index >= (uint)entries.Count)
        {
            return;
        }
        var entry = entries[index];
        var full = System.IO.Path.Combine(_model.Path, entry.Name);
        if (entry.IsDirectory)
        {
            _model.Navigate(full);
            return;
        }
        try
        {
            // 関連付けが無いときはシェルの既定動作（「開く方法」ダイアログ）に任せる
            NativeMethods.ShellExecuteW(_hwnd, null, full, null, _model.Path, NativeMethods.SW_SHOWNORMAL);
        }
        catch (Exception ex)
        {
            Diagnostics.Report($"ShellExecute({full})", ex);
        }
    }

    // --- ヘッダのソート矢印 ---

    private void UpdateSortIndicator()
    {
        if (_hwnd == 0)
        {
            return;
        }
        var header = SendMessageW(_hwnd, LVM_GETHEADER, 0, 0);
        if (header == 0)
        {
            return;
        }
        var sorted = (int)_model.SortColumn;
        for (var i = 0; i < Columns.Length; i++)
        {
            var item = new HDITEMW { mask = HDI_FORMAT };
            if (SendMessageW(header, HDM_GETITEMW, i, (nint)(&item)) == 0)
            {
                continue;
            }
            item.fmt &= ~(HDF_SORTUP | HDF_SORTDOWN);
            if (i == sorted)
            {
                item.fmt |= _model.SortAscending ? HDF_SORTUP : HDF_SORTDOWN;
            }
            SendMessageW(header, HDM_SETITEMW, i, (nint)(&item));
        }
    }
}
