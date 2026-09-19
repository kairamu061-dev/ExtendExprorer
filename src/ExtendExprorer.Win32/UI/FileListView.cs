using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
        LiveObjects.Track(this, "FileListView");
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
            // 「PC」は落とし先にしない（フォルダではないので受ける先が無い）
            currentFolder: () => _model.IsDrives ? string.Empty : _model.Path,
            folderAtRow: row =>
            {
                var entries = _model.Entries;
                if (_model.IsDrives || (uint)row >= (uint)entries.Count || !entries[row].IsDirectory)
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
        SubclassHeader();

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

    // --- 列見出しの右クリック（フォルダごとの並べ替えの設定） ---
    //
    // ★ 見出しは一覧の子で、通知は**一覧へ**行く。こちらまで上がってこないので、
    //   見出しそのものを差し替えて右クリックだけ拾う。
    //   「通知が来ているはず」で作ると、来ていないことに気付くのに 1 往復かかる。

    private static readonly Dictionary<nint, FileListView> Headers = [];
    private nint _header;
    private nint _headerProc;

    private void SubclassHeader()
    {
        _header = SendMessageW(_hwnd, LVM_GETHEADER, 0, 0);
        if (_header == 0)
        {
            return;
        }
        Headers[_header] = this;
        _headerProc = SetWindowLongPtrW(_header, GWLP_WNDPROC,
            (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&HeaderProc);
        // ★ 差し替えられたかを残す。メニューが出ないときに
        //   「差し替えに失敗した」のか「右クリックが来ていない」のかを分ける
        Diagnostics.Write($"[list] 見出しを差し替えた 見出し=0x{_header:X} 元のプロシージャ=0x{_headerProc:X}");
    }

    private void UnsubclassHeader()
    {
        if (_header == 0)
        {
            return;
        }
        if (_headerProc != 0)
        {
            SetWindowLongPtrW(_header, GWLP_WNDPROC, _headerProc);
            _headerProc = 0;
        }
        Headers.Remove(_header);
        _header = 0;
    }

    /// <summary>列見出しのプロシージャの手前。右クリックだけ横取りして、
    /// それ以外は素通しする（例外はここで止めること）。</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint HeaderProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        nint original = 0;
        try
        {
            if (!Headers.TryGetValue(hwnd, out var view))
            {
                return DefWindowProcW(hwnd, msg, wParam, lParam);
            }
            original = view._headerProc;
            if (msg is WM_RBUTTONUP or WM_CONTEXTMENU)
            {
                Diagnostics.Write($"[list] 見出しの右クリック msg=0x{msg:X4}");
                view.ShowHeaderMenu();
                return 0;
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Report($"FileList.HeaderProc(0x{msg:X4})", ex);
        }
        return original != 0
            ? CallWindowProcW(original, hwnd, msg, wParam, lParam)
            : DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private const int CmdFoldersFirst = 1;

    /// <summary>列見出しの右クリックで出す小さなメニュー。いまは 1 項目だけ。</summary>
    private void ShowHeaderMenu()
    {
        // ★ 右クリックは 2 つの道から来うる（差し替えた見出し／一覧が転送する通知）。
        //   どちらが効くか実機でしか分からないので両方受けている。
        //   二重に開かないよう、出している間は入れない
        if (_headerMenuOpen)
        {
            return;
        }
        if (_model.IsDrives || _model.Path.Length == 0)
        {
            Diagnostics.Write($"[list] 見出しのメニューを出さない（PC={_model.IsDrives}）");
            return; // 「PC」には並べ替えが無い
        }
        _headerMenuOpen = true;
        try
        {
            ShowHeaderMenuCore();
        }
        finally
        {
            _headerMenuOpen = false;
        }
    }

    private bool _headerMenuOpen;

    private void ShowHeaderMenuCore()
    {
        var menu = CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }
        try
        {
            AppendMenuW(menu, MF_STRING | (_model.FoldersFirst ? MF_CHECKED : 0),
                CmdFoldersFirst, "フォルダを先頭にまとめる");
            GetCursorPos(out var point);
            var command = TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD,
                point.X, point.Y, 0, GetAncestor(_hwnd, GA_ROOT), 0);
            Diagnostics.Write($"[list] 見出しのメニュー 選ばれた={command}");
            if (command == CmdFoldersFirst)
            {
                // ★ 通知の中でモデルを触らない。メニューを畳んでから
                _model.ToggleFoldersFirst();
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

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
        // ★ 差し替えを戻すのは、一覧を壊す前。一覧を壊すと見出しも道連れになる
        UnsubclassHeader();
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
        // ★ どちらへ倒れたかを残す。「真っ白で何も出ない」ときに、
        //   エラーが立っていないのか・立っているのに描かれていないのかを分ける（BUG-032）
        Diagnostics.Write($"[list] 文字の板={(text is null ? "隠す" : $"出す「{text}」")}"
            + $" 板={_message:X} 件数={_model.Entries.Count}");
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
        ReportMessageGeometry(text);
    }

    /// <summary>文字の板の「どこに描かれるはず」かを画面座標で出す。
    ///
    /// <para><b>板は縦中央に描く（<c>SS_CENTERIMAGE</c>）。</b>803px の板なら文字は
    /// y+400 あたりで、板の上端から 120px・400px を切り取っても<b>文字は入らない</b>。
    /// BUG-032 の 2 枚の写真がまさにその形だったので、
    /// <b>写真をどこで撮ればよいかをアプリ自身に言わせる</b>（言葉で指示すると、また外す）。</para></summary>
    private void ReportMessageGeometry(string text)
    {
        if (!Diagnostics.Enabled || _message == 0)
        {
            return;
        }
        if (!GetWindowRect(_message, out var screen) || !GetClientRect(_message, out var client))
        {
            return;
        }
        var size = default(SIZE);
        var dc = GetDC(_message);
        if (dc != 0)
        {
            var font = SendMessageW(_message, WM_GETFONT, 0, 0);
            var previous = font != 0 ? SelectObject(dc, font) : 0;
            GetTextExtentPoint32W(dc, text, text.Length, out size);
            if (previous != 0)
            {
                SelectObject(dc, previous);
            }
            ReleaseDC(_message, dc);
        }
        var centerY = screen.Top + client.Height / 2;
        var centerX = screen.Left + client.Width / 2;
        Diagnostics.Write($"[list] 板の位置 画面 {screen.Left},{screen.Top}-{screen.Right},{screen.Bottom}"
            + $" 内側 {client.Width}×{client.Height} 文字 {size.cx}×{size.cy}"
            + $" → 文字はここ 画面 {centerX - size.cx / 2},{centerY - size.cy / 2}"
            + $"-{centerX + size.cx / 2},{centerY + size.cy / 2}");
    }

    /// <summary>「PC」を開いているときの列。エクスプローラーに合わせて入れ替える。</summary>
    private static readonly (string Title, int Width, int Format)[] DriveColumns =
    [
        ("名前", 240, LVCFMT_LEFT),
        ("種類", 130, LVCFMT_LEFT),
        ("合計サイズ", 100, LVCFMT_RIGHT),
        ("空き領域", 100, LVCFMT_RIGHT),
    ];

    /// <summary>いま入っている列がドライブ用か。入れ替えは変わったときだけ行う。</summary>
    private bool _driveColumns;

    /// <summary><b>いま実際に入っている列の数。</b>消すときはこれを使う——
    /// 定数の配列の長さで消すと、2 つの配列の長さがたまたま同じことに頼ることになる。
    /// 片方の列を増やした日に、余った列が残るか 1 つ多く消してしまい、
    /// <c>LVN_GETDISPINFOW</c> が居ない列番号で聞かれ始める。</summary>
    private int _columnCount;

    private uint _columnDpi = 96;

    /// <summary>開いている先に合わせて列を入れ替える。<b>変わったときだけ</b>触る
    /// （毎回入れ直すと、ユーザーが広げた列幅が消える）。</summary>
    private void SyncColumns()
    {
        if (_hwnd == 0 || _driveColumns == _model.IsDrives)
        {
            return;
        }
        _driveColumns = _model.IsDrives;
        // 後ろから消す。前から消すと番号が詰まってずれる
        for (var i = _columnCount - 1; i >= 0; i--)
        {
            SendMessageW(_hwnd, LVM_DELETECOLUMN, i, 0);
        }
        InsertColumns(_columnDpi);
    }

    private void InsertColumns(uint dpi)
    {
        _columnDpi = dpi;
        var columns = _driveColumns ? DriveColumns : Columns;
        for (var i = 0; i < columns.Length; i++)
        {
            var (title, width, format) = columns[i];
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
        _columnCount = columns.Length;
        UpdateSortIndicator();
    }

    internal void SetBounds(RECT bounds)
    {
        if (_hwnd != 0)
        {
            MoveWindowNoCopy(_hwnd, bounds);
        }
        if (_message != 0)
        {
            MoveWindowNoCopy(_message, bounds);
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
        // ★ 行数を伝える前に列を入れ替える。LVM_SETITEMCOUNT はその場で
        //   LVN_GETDISPINFOW を呼び返してくるので、先に列を合わせておかないと
        //   古い列の番号で中身を聞かれる
        SyncColumns();
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
        if (TakeNewItemExpectation() && (uint)index < (uint)_model.Entries.Count)
        {
            // 「新規作成」で作られた項目。エクスプローラーと同じく、その場で名前を編集させる。
            // 通知の中で編集を始めない（一覧へ入れ子でメッセージが飛ぶ）ので、いったん抜ける。
            //
            // ★ 行番号は持ち越さない。抜けている間に別の通知が届けば行は動く
            //   （BUG-017 と同じ形）。名前で控えて、実行する時点で引き直す
            var name = _model.Entries[index].Name;
            UiDispatcher.Post(() =>
            {
                var row = RowOfName(name);
                if (row < 0)
                {
                    Diagnostics.Write($"[new] {name} が見つからない（作成が取り消された？）");
                    return;
                }
                Diagnostics.Write($"[new] {name} の編集を始める 行={row}");
                SelectOnly(row);
                BeginRename(row);
            });
        }
    }

    // --- 「新規作成」の直後にインライン編集を始める（第 4d 段） ---
    //
    // 作るのはシェルで、こちらに届くのは監視の通知（＝どれが新しいのか分からない）。
    // そこで「新規作成の下から選ばれた」時点で合図を立てておき、
    // **次に増えた 1 件**をその項目とみなす。
    //
    // 時限を付けるのは取り違えを防ぐため。作成に失敗した・別のアプリが
    // 同じフォルダにファイルを作った、という場合に、無関係な行の編集が始まらないようにする。

    private long _expectNewItemUntil;

    private const int NewItemWindowMs = 5000;

    /// <summary>「新規作成」が選ばれた。次に増える 1 件の編集を始める。</summary>
    private void ExpectNewItem() => _expectNewItemUntil = Environment.TickCount64 + NewItemWindowMs;

    /// <summary>合図が立っていれば下ろして true。<b>1 件にしか使わない。</b></summary>
    private bool TakeNewItemExpectation()
    {
        if (_expectNewItemUntil == 0)
        {
            return false;
        }
        var expected = Environment.TickCount64 <= _expectNewItemUntil;
        _expectNewItemUntil = 0;
        return expected;
    }

    /// <summary>その名前の行番号。無ければ -1。</summary>
    private int RowOfName(string name)
    {
        var entries = _model.Entries;
        for (var i = 0; i < entries.Count; i++)
        {
            if (string.Equals(entries[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>その行だけを選び直す。</summary>
    private void SelectOnly(int index)
    {
        if (_hwnd == 0 || (uint)index >= (uint)_model.Entries.Count)
        {
            return;
        }
        ClearSelection();
        var item = new LVITEMW { state = LVIS_SELECTED | LVIS_FOCUSED, stateMask = LVIS_SELECTED | LVIS_FOCUSED };
        SendMessageW(_hwnd, LVM_SETITEMSTATE, index, (nint)(&item));
        SendMessageW(_hwnd, LVM_ENSUREVISIBLE, index, 0);
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
        // ★ 見出しからの通知も受ける。一覧が親へ転送してくることがあり、
        //   そのときは差し替えた側に右クリックが来ない（どちらが効くかは実機次第なので、
        //   両方受けて、二重に開かないようにしてある）
        if (_header != 0 && header->hwndFrom == _header)
        {
            if (header->code == NM_RCLICK)
            {
                Diagnostics.Write("[list] 見出しの右クリック（一覧からの転送）");
                ShowHeaderMenu();
                return true;
            }
            return false;
        }
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

    /// <summary>編集が開いた時刻と、そのときのドラッグ開始回数。
    /// 取り消されたときに「誰が畳んだか」を切り分けるためだけに持つ。</summary>
    private long _renameStarted;
    private int _renameDragMark;

    /// <summary><c>LVN_BEGINDRAG</c> が来た回数。値そのものに意味は無く、
    /// 編集の前後で変わったかどうかだけを見る。</summary>
    private int _beginDragCount;

    /// <summary>キーボードから編集を始める（F2）。
    ///
    /// <para><b>対象は「手前の行」。</b>複数選んでいるときにどれか 1 つを選ぶと、
    /// 「複数選択では始めない」という約束（旧版 E-36）と食い違う。</para>
    ///
    /// <para><b>手前が無いときだけ、1 件だけ選ばれていればそちらへ落とす。</b>
    /// 「選ばれている」と「手前にある」は別で、選択だけを付ける経路
    /// （<c>LVIS_SELECTED</c> だけを立てる自動操作など）だと手前が -1 になり、
    /// 何も起きない。使う側からは「F2 が効かない」としか見えない。</para></summary>
    internal void BeginRename()
    {
        if (_hwnd == 0)
        {
            return;
        }
        var index = (int)SendMessageW(_hwnd, LVM_GETNEXTITEM, -1, (nint)LVNI_FOCUSED);
        if (index < 0 && (int)SendMessageW(_hwnd, LVM_GETSELECTEDCOUNT, 0, 0) == 1)
        {
            index = (int)SendMessageW(_hwnd, LVM_GETNEXTITEM, -1, LVNI_SELECTED);
            Diagnostics.Write($"[rename] F2 手前の行が無いので、選ばれている 1 件を使う 行={index}");
        }
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
        // ★ 「PC」では改名させない。ドライブはフォルダではないので、名前を変えるなら
        //   ボリュームラベルの書き換えになる。止めるのは**ここ 1 か所**——
        //   編集は F2 でも 2 回目クリックでも同じ通知から始まるので、
        //   入口ごとに塞ぐと必ずどれか漏れる（実際、2 回目クリックだけ通っていた）
        if (_model.IsDrives)
        {
            Diagnostics.Write("[rename] 「PC」なので始めない");
            return 1;
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
        _renameStarted = Environment.TickCount64;
        _renameDragMark = _beginDragCount;
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
                // 行の指す先はモデルに聞く（「PC」では改名を止めてあるので通らないが、
                // 「いまのフォルダ＋名前」を仮定する場所を増やさない）
                source = _model.PathOf(index);
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
            // ★ 取り消しは「誰が畳んだか」で原因が変わる。3 つを 1 行に並べる。
            //   経過が数 ms ＝ 開いた直後に別の入力が来て畳まれた（合成入力を疑う）
            //   ドラッグ=有 ＝ DoDragDrop の入れ子が始まって畳まれた
            //   どちらも違えば、それ以外の理由（旧版 BUG-006 の形）
            Diagnostics.Write($"[rename] 取り消し 行={index}"
                + $" 経過={Environment.TickCount64 - _renameStarted}ms"
                + $" フォーカス=0x{GetFocus():X} 一覧=0x{_hwnd:X}"
                + $" ドラッグ={(_beginDragCount != _renameDragMark ? "有" : "無")}");
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
        // 「PC」ではシェルのメニューを出さない。ここは「フォルダのパス＋その中の名前」で
        // 組み立てる作りで、ドライブの一覧はその形に当てはまらない。
        // 出せないより、間違ったものを出す方が危ない（spec の未対応ケース）
        if (folder.Length == 0 || _model.IsDrives)
        {
            return;
        }
        if ((uint)index >= (uint)_model.Entries.Count)
        {
            UiDispatcher.Post(() => ShellContextMenuService.ShowForBackground(owner, folder,
                newItemRequested: ExpectNewItem));
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
        _beginDragCount++;
        var folder = _model.Path;
        if (_hwnd == 0 || folder.Length == 0 || _model.IsDrives)
        {
            return; // 「PC」からドライブを持ち出すことはしない
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
        // ★ 「PC」では空を返す＝コピー・切り取り・削除が効かなくなる。
        //   ドライブの行で Delete が通ってしまう方が、よほど困る
        if (folder.Length == 0 || _model.IsDrives)
        {
            return paths;
        }
        foreach (var name in CaptureSelection())
        {
            paths.Add(System.IO.Path.Combine(folder, name));
        }
        return paths;
    }

    /// <summary>「PC」を開いているときの列の中身。
    /// 読めなかったドライブ（空の光学ドライブなど）は容量を「—」にする。</summary>
    private static string DriveTextOf(Models.DriveRow drive, int column) => column switch
    {
        0 => drive.Label,
        1 => drive.TypeName,
        2 => drive.Total > 0 ? Models.EntryFormat.CapacityLabel(drive.Total) : "—",
        3 => drive.Total > 0 ? Models.EntryFormat.CapacityLabel(drive.Free) : "—",
        _ => string.Empty,
    };

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
        var drive = _model.IsDrives && (uint)index < (uint)_model.Drives.Count
            ? _model.Drives[index]
            : null;

        if ((info->item.mask & LVIF_TEXT) != 0)
        {
            var text = drive is null
                ? TextOf(entry, info->item.iSubItem)
                : DriveTextOf(drive, info->item.iSubItem);
            CopyText(text, info->item.pszText, info->item.cchTextMax);
        }
        if ((info->item.mask & LVIF_IMAGE) != 0 && info->item.iSubItem == 0)
        {
            // ドライブは実パスで引く（ハードディスク・光学・ネットワークで絵が違う）
            info->item.iImage = drive is null
                ? ShellImageList.IndexOf(_model.Path, entry)
                : ShellImageList.IndexOfPath(drive.Root);
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
        // 行の指す先はモデルに聞く（「PC」ではドライブの根になる）
        if (_model.PathOf(index) is not { Length: > 0 } full)
        {
            return;
        }
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
