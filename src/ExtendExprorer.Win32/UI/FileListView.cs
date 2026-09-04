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

    /// <summary>一覧がフォーカスを受けた（分割時に、どのペインが手前かを切り替える合図）。</summary>
    internal event Action? Focused;

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
            LVS_REPORT | LVS_OWNERDATA | LVS_SHAREIMAGELISTS | LVS_SHOWSELALWAYS | LVS_EDITLABELS,
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

        // ドロップ先として登録する（受け取る側だけ。持ち出しは第 4d 段）
        _dropTarget = ListDropTarget.Register(_hwnd,
            currentFolder: () => _model.Path,
            folderAtRow: row =>
            {
                var entries = _model.Entries;
                if ((uint)row >= (uint)entries.Count || !entries[row].IsDirectory)
                {
                    return null;
                }
                return System.IO.Path.Combine(_model.Path, entries[row].Name);
            });

        // アイコンは OS のシステムイメージリストを借りる（破棄しない）
        var imageList = ShellImageList.Handle;
        if (imageList != 0)
        {
            SendMessageW(_hwnd, LVM_SETIMAGELIST, (nint)LVSIL_SMALL, imageList);
        }

        InsertColumns(dpi);

        // エラー表示用の重ね板。一覧そのものの「項目が無いときの文字」
        // （`LVN_GETEMPTYMARKUP`）は、空になった最初の一度しか聞かれないらしく、
        // 後から届くエラーに差し替わらなかった（BUG-020 の真因）。
        // 文字を出す場所を自分で持てば、いつ差し替えても確実に出る
        _message = CreateWindowExW(0, WC_STATIC, null,
            WS_CHILD | SS_CENTER | SS_CENTERIMAGE,
            bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            parent, 0, instance, 0);
        if (_message != 0)
        {
            SendMessageW(_message, WM_SETFONT, font, 1);
        }
    }

    /// <summary>エラー時に一覧の代わりに出す文字。</summary>
    private nint _message;

    internal nint MessageHandle => _message;

    /// <summary>ドロップ先の登録。窓を壊す前に必ず外す（外し忘れは落ちる形になる）。</summary>
    private ListDropTarget? _dropTarget;

    /// <summary>一覧と、その上に重ねた文字の板を壊す。
    ///
    /// <para><b>ドロップ先の登録を外すのが先。</b>窓が無くなってから外そうとしても
    /// 外れず、OLE 側に参照が残る（ペインを閉じるたびに増える）。</para></summary>
    internal void Destroy()
    {
        if (_hwnd != 0 && _dropTarget is not null)
        {
            ListDropTarget.Revoke(_hwnd);
            _dropTarget = null;
        }
        if (_message != 0)
        {
            DestroyWindow(_message);
            _message = 0;
        }
        if (_hwnd != 0)
        {
            DestroyWindow(_hwnd);
            _hwnd = 0;
        }
        _model.EntriesChanging -= OnEntriesChanging;
        _model.EntriesReset -= OnEntriesReset;
        _model.EntryAdded -= OnEntryAdded;
        _model.EntryRemoved -= OnEntryRemoved;
        _model.EntryUpdated -= OnEntryUpdated;
    }

    /// <summary>エラーが出ているかどうかで、一覧と文字を出し分ける。</summary>
    private void UpdateMessage()
    {
        if (_message == 0)
        {
            return;
        }
        var text = _model.ErrorMessage;
        if (text is null)
        {
            ShowWindow(_message, SW_HIDE);
            ShowWindow(_hwnd, SW_SHOWNORMAL);
            return;
        }
        SetWindowTextW(_message, text);
        ShowWindow(_hwnd, SW_HIDE);
        ShowWindow(_message, SW_SHOWNORMAL);
        InvalidateRect(_message, 0, erase: true);
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
        if (_message != 0)
        {
            MoveWindow(_message, bounds.Left, bounds.Top, bounds.Width, bounds.Height, repaint: true);
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
        UpdateMessage();
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

            case NM_RCLICK:
                OnRightClick(((NMLISTVIEW*)header)->iItem);
                return true;

            case LVN_BEGINDRAG:
                OnBeginDrag();
                return true;

            case LVN_BEGINLABELEDITW:
                result = BeginLabelEdit((NMLVDISPINFOW*)header);
                return true;

            case LVN_ENDLABELEDITW:
                result = EndLabelEdit((NMLVDISPINFOW*)header);
                return true;

            case NM_SETFOCUS:
                Focused?.Invoke();
                return false; // 既定の処理も行わせる
        }
        return false;
    }

    // --- インライン リネーム ---
    //
    // 「1 回目のクリックで選択、2 回目で編集」という間合いは comctl32 が自分で持っている
    // （LVS_EDITLABELS）。旧 WinUI 版はタイマーで組んでいたが、ここでは持ち込まない。
    // ダブルクリック（開く）と取り違えないのもコントロール側の仕事。

    /// <summary>編集中のときだけ、その入力欄のハンドル。キーの横取りの判定に使う。</summary>
    internal nint RenameEditorHandle { get; private set; }

    private int _renamingIndex = -1;

    /// <summary>Tab で確定した内容。フォーカスを外して確定させたときに
    /// 取り消し扱いで戻ってきても、こちらの控えで確定できるようにする。</summary>
    private string? _committedByTab;

    /// <summary>Tab のあとに編集を始める行。</summary>
    private int _renameNext = -1;

    /// <summary>キーボードから編集を始める（F2）。手前の行が対象。</summary>
    internal void BeginRename()
    {
        if (_hwnd == 0)
        {
            return;
        }
        var index = (int)SendMessageW(_hwnd, LVM_GETNEXTITEM, -1, (nint)LVNI_FOCUSED);
        BeginRename(index);
    }

    private void BeginRename(int index)
    {
        if (_hwnd == 0 || (uint)index >= (uint)_model.Entries.Count)
        {
            return;
        }
        SetFocus(_hwnd);
        SendMessageW(_hwnd, LVM_EDITLABELW, index, 0);
    }

    /// <summary>編集の開始。<b>拡張子を除いて選択</b>し、自動追随を止める。</summary>
    private nint BeginLabelEdit(NMLVDISPINFOW* info)
    {
        var index = info->item.iItem;
        if ((uint)index >= (uint)_model.Entries.Count)
        {
            return 1; // 開始させない
        }
        _renamingIndex = index;
        _model.SuspendAutoRefresh();
        RenameEditorHandle = SendMessageW(_hwnd, LVM_GETEDITCONTROL, 0, 0);

        var entry = _model.Entries[index];
        // フォルダと、先頭がドットの名前（.gitignore 等）は全部を選ぶ。
        // 拡張子だけの名前で 0 文字選択になると「何も選ばれていない」ように見える
        var stem = entry.IsDirectory ? entry.Name.Length : StemLength(entry.Name);
        if (RenameEditorHandle != 0)
        {
            SendMessageW(RenameEditorHandle, EM_SETSEL, 0, stem);
        }
        Diagnostics.Write($"[rename] 開始 行={index} 名前={entry.Name} 選択=0..{stem}");
        return 0;
    }

    private static int StemLength(string name)
    {
        var stem = System.IO.Path.GetFileNameWithoutExtension(name);
        return stem.Length > 0 ? stem.Length : name.Length;
    }

    /// <summary>編集の終了。<b>戻り値は必ず 0</b>。オーナーデータの一覧には
    /// 文字列を持つ項目が無いので、「受け入れた」と返しても書き込む先が無い。
    /// 表示は改名の通知（監視）で更新される。</summary>
    private nint EndLabelEdit(NMLVDISPINFOW* info)
    {
        var index = _renamingIndex;
        _renamingIndex = -1;
        RenameEditorHandle = 0;

        // 改名の通知を受け取れるよう、実行より先に戻す
        _model.ResumeAutoRefresh();

        var text = info->item.pszText != 0 ? new string((char*)info->item.pszText) : _committedByTab;
        _committedByTab = null;
        var next = _renameNext;
        _renameNext = -1;

        string? source = null;
        string? newName = null;
        if ((uint)index < (uint)_model.Entries.Count && text is { Length: > 0 })
        {
            var entry = _model.Entries[index];
            if (!string.Equals(text, entry.Name, StringComparison.Ordinal))
            {
                source = System.IO.Path.Combine(_model.Path, entry.Name);
                newName = text;
                Diagnostics.Write($"[rename] 確定 行={index} 旧={entry.Name} 新={text}");
            }
            else
            {
                Diagnostics.Write($"[rename] 変更なし 行={index} 名前={entry.Name}");
            }
        }
        else
        {
            Diagnostics.Write($"[rename] 取り消し 行={index}");
        }

        // ★ 通知の中でシェルの操作を走らせない。改名はモーダルのダイアログを出しうるので、
        // 一覧が編集を畳んでいる最中に入れ子で回すと状態が噛み合わない
        // （実機で 1 度だけ 0x80070057 が出た件の対策・2026-08-29）。
        // 次の行の編集も同じ 1 つの後回しの中で行い、順番を確実にする
        if (source is not null || next >= 0)
        {
            var owner = GetAncestor(_hwnd, GA_ROOT);
            UiDispatcher.Post(() =>
            {
                if (source is not null && newName is not null)
                {
                    ShellFileOperations.Rename(owner, source, newName);
                }
                if (next >= 0)
                {
                    BeginRename(next);
                }
            });
        }
        return 0;
    }

    /// <summary>編集中のキー。Tab は<b>確定して次の行</b>（Shift+Tab で前の行）。
    /// それ以外は入力欄に渡す（Backspace を「戻る」に取られないこと）。</summary>
    internal bool HandleRenameKey(int key)
    {
        if (RenameEditorHandle == 0 || key != VK_TAB)
        {
            return false;
        }
        var shift = (GetKeyState(VK_SHIFT) & 0x8000) != 0;
        _committedByTab = EditorText(RenameEditorHandle);
        _renameNext = _renamingIndex + (shift ? -1 : 1);
        // フォーカスを外すと確定する。取り消し扱いで戻ってきても _committedByTab で確定できる
        SetFocus(_hwnd);
        return true;
    }

    private static string EditorText(nint editor)
    {
        var length = GetWindowTextLengthW(editor);
        if (length <= 0)
        {
            return "";
        }
        var buffer = new char[length + 1];
        fixed (char* text = buffer)
        {
            var written = GetWindowTextW(editor, text, buffer.Length);
            return new string(text, 0, Math.Max(0, written));
        }
    }

    /// <summary>右クリック。押した行が選択に入っていなければ、その行だけを選び直してから出す
    /// （エクスプローラーと同じ）。行の外なら背景のメニュー。
    ///
    /// <para><b>メニューは通知の中で出さない。</b>シェルのメニューはモーダルで、
    /// しかも他社のシェル拡張がその中で動く。一覧の通知を処理している途中で
    /// 入れ子に回さない方が安全（リネームを後回しにしたのと同じ理由）。</para></summary>
    private void OnRightClick(int index)
    {
        var owner = GetAncestor(_hwnd, GA_ROOT);
        var folder = _model.Path;
        if (folder.Length == 0)
        {
            return;
        }
        if ((uint)index >= (uint)_model.Entries.Count)
        {
            UiDispatcher.Post(() => ShellContextMenuService.ShowForBackground(owner, folder));
            return;
        }
        if (!IsSelected(index))
        {
            ClearSelection();
            var item = new LVITEMW { state = LVIS_SELECTED | LVIS_FOCUSED, stateMask = LVIS_SELECTED | LVIS_FOCUSED };
            SendMessageW(_hwnd, LVM_SETITEMSTATE, index, (nint)(&item));
        }
        var names = CaptureSelection();
        var renameIndex = index;
        UiDispatcher.Post(() => ShellContextMenuService.ShowForItems(owner, folder, names,
            renameRequested: () => UiDispatcher.Post(() => BeginRename(renameIndex))));
    }

    /// <summary>選択した行を掴んで外へ持ち出す（第 4d 段）。
    ///
    /// <para><b>ここは後回しにしない。</b>シェルの操作は通知の中で走らせない決まりだが、
    /// ドラッグは<b>ボタンが押されたままのうちに</b>始めないと成立しない。
    /// <c>DoDragDrop</c> は落とされるまで戻らないので、その間の自動追随は止めておく
    /// （掴んでいる最中に一覧が作り直されないように）。</para></summary>
    private void OnBeginDrag()
    {
        var folder = _model.Path;
        if (_hwnd == 0 || folder.Length == 0)
        {
            return;
        }
        var names = CaptureSelection();
        if (names.Count == 0)
        {
            return;
        }
        var owner = GetAncestor(_hwnd, GA_ROOT);
        _model.SuspendAutoRefresh();
        try
        {
            ListDragSource.Begin(owner, folder, names);
        }
        finally
        {
            _model.ResumeAutoRefresh();
        }
    }

    /// <summary>選択中の項目のフルパス。Ctrl+C / Ctrl+X / Delete の対象。</summary>
    internal List<string> SelectedPaths()
    {
        var folder = _model.Path;
        var paths = new List<string>();
        if (folder.Length == 0)
        {
            return paths;
        }
        foreach (var name in CaptureSelection())
        {
            paths.Add(System.IO.Path.Combine(folder, name));
        }
        return paths;
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
        _drawCalls++;
        LogDraw(draw);
        switch (draw->nmcd.dwDrawStage)
        {
            case CDDS_PREPAINT:
                _drawPrePaint++;
                return CDRF_NOTIFYITEMDRAW;

            case CDDS_ITEMPREPAINT:
                // 行の段階では色を決めない。詳細表示では文字が列ごとに描かれ、
                // ここで指定しても列の描画で戻ってしまうため（BUG-019）。
                // 通知だけ受け取って、色は列の段階で指定する。
                //
                // 行番号と選択状態は、**行の段階のものを控えて**列の段階で使う。
                // 列の段階の `dwItemSpec` / `uItemState` は当てにできない（BUG-019）
                _drawItem++;
                _rowIndex = (int)draw->nmcd.dwItemSpec;
                _rowSelected = IsSelected(_rowIndex);
                return CDRF_NOTIFYSUBITEMDRAW;

            case CDDS_ITEMPREPAINT | CDDS_SUBITEM:
                _drawSubItem++;
                if (!IsDimmed())
                {
                    return CDRF_DODEFAULT;
                }
                _drawDimmed++;
                draw->clrText = DimmedTextColor;
                // 色を変えたら CDRF_NEWFONT を返す（DC を触ったことを一覧に伝える）
                return CDRF_NEWFONT;
        }
        return CDRF_DODEFAULT;
    }

    /// <summary>選択中かを一覧に直接聞く。オーナーデータでも選択はコントロールが
    /// 持っているので、描画中に聞いても項目の問い合わせは起きない。</summary>
    private bool IsSelected(int index) =>
        (SendMessageW(_hwnd, LVM_GETITEMSTATE, index, (nint)LVIS_SELECTED) & LVIS_SELECTED) != 0;

    // 実機でしか再現しない不具合の切り分け用。--diag 付きのときだけ書き出す
    private int _drawCalls;
    private int _drawPrePaint;
    private int _drawItem;
    private int _drawSubItem;
    private int _drawDimmed;

    /// <summary>描画の通知が実際に届いているかを書き出す。「条件が偽なのか、
    /// 色を指定しても反映されないのか」を切り分けるためのもの。</summary>
    internal void WriteDiagnostics()
    {
        var hidden = 0;
        foreach (var entry in _model.Entries)
        {
            if (entry.IsHiddenOrSystem)
            {
                hidden++;
            }
        }
        Diagnostics.Write($"[customdraw] 呼び出し={_drawCalls} 全体={_drawPrePaint} 行={_drawItem} " +
            $"列={_drawSubItem} 薄色にした列={_drawDimmed}");
        Diagnostics.Write($"[customdraw] 一覧 {_model.Entries.Count} 件のうち隠し/システム={hidden} " +
            $"薄色={DimmedTextColor:X6} 本文={GetSysColor(COLOR_WINDOWTEXT):X6} 背景={GetSysColor(COLOR_WINDOW):X6}");
    }

    /// <summary>いま描いている列を薄色で描くか。選択中の行は塗りつぶしの上に描かれるため、
    /// 読みにくくならないよう既定のままにする。</summary>
    private bool IsDimmed()
    {
        var entries = _model.Entries;
        return !_rowSelected
            && (uint)_rowIndex < (uint)entries.Count
            && entries[_rowIndex].IsHiddenOrSystem;
    }

    /// <summary>いま描いている行（行の段階で控えたもの）。</summary>
    private int _rowIndex = -1;
    private bool _rowSelected;

    /// <summary>描画通知の生の値。<c>--diag</c> のときだけ、最初の数回を書き出す。
    /// 行番号や状態がどこから来ているのかを、推測せずに確かめるため。</summary>
    private void LogDraw(NMLVCUSTOMDRAW* draw)
    {
        if (!Diagnostics.Enabled || _drawLogged >= 12)
        {
            return;
        }
        _drawLogged++;
        Diagnostics.Write($"  [cd] stage=0x{draw->nmcd.dwDrawStage:X} itemSpec={draw->nmcd.dwItemSpec} " +
            $"state=0x{draw->nmcd.uItemState:X} sub={draw->iSubItem} 控えた行={_rowIndex} 選択={_rowSelected}");
    }

    private int _drawLogged;

    /// <summary>本文と背景を混ぜた薄い文字色（現行版の不透明度 0.55 に相当）。</summary>
    internal static uint DimmedTextColor
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
            // パス文字列ではなく PIDL で渡す（エクスプローラーのダブルクリックと同じ経路）。
            // 文字列で渡すと、既定のアプリがあっても「開く方法」を聞かれることがある（旧版 BUG-004）。
            // 関連付けが無いときは、シェルが「開く方法」を出す
            ShellContextMenuService.OpenWithDefault(GetAncestor(_hwnd, GA_ROOT), full);
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
