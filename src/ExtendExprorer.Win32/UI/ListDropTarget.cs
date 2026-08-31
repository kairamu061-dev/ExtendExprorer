using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using ExtendExprorer.Interop;
using ExtendExprorer.Services;
using static ExtendExprorer.Interop.ListView;
using static ExtendExprorer.Interop.Win32;

namespace ExtendExprorer.UI;

/// <summary>一覧に落とされたファイルを受け取る（ドラッグ＆ドロップの受け側）。
///
/// <para><b>この 1 つだけ COM の向きが逆になる。</b>これまではシェルのオブジェクトを
/// こちらが呼んでいたが、これは<b>こちらが実装したものを OS が呼ぶ</b>。
/// Native AOT でそれをやるには <c>[GeneratedComClass]</c> が要る
/// （実行時にリフレクションで vtable を組めないため）。</para>
///
/// <para><b>落とし先は「行の上ならそのフォルダ、それ以外は表示中のフォルダ」。</b>
/// フォルダ以外の行の上は、エクスプローラーと同じく表示中のフォルダ扱いにする。</para>
///
/// <para>実際のコピー／移動は<b>この呼び出しの中では行わない</b>。OS のドラッグの
/// ループの中でモーダルのダイアログを回すことになるため、いったん抜けてから実行する
/// （改名・コンテキストメニューと同じ扱い）。</para></summary>
[GeneratedComClass]
internal sealed unsafe partial class ListDropTarget : IDropTarget
{
    private static readonly StrategyBasedComWrappers Wrappers = new();

    private readonly nint _hwnd;
    private readonly Func<string> _currentFolder;
    private readonly Func<int, string?> _folderAtRow;

    private List<string> _sources = [];
    private int _highlighted = -1;

    /// <summary>何回目の <c>DragOver</c> か。落ちたときに「どこまで進んだか」が
    /// ログだけで分かるよう、最初の数回だけ書き出す（毎回書くと重くなる）。</summary>
    private int _overCount;

    private const int LoggedOverCalls = 5;

    /// <summary><paramref name="folderAtRow"/> は、その行がフォルダならフルパス、
    /// 違えば null を返す。</summary>
    internal ListDropTarget(nint hwnd, Func<string> currentFolder, Func<int, string?> folderAtRow)
    {
        _hwnd = hwnd;
        _currentFolder = currentFolder;
        _folderAtRow = folderAtRow;
    }

    /// <summary>この一覧をドロップ先として登録する。CCW を作って OS に預ける。</summary>
    internal static ListDropTarget? Register(nint hwnd, Func<string> currentFolder, Func<int, string?> folderAtRow)
    {
        try
        {
            var target = new ListDropTarget(hwnd, currentFolder, folderAtRow);
            var ptr = Wrappers.GetOrCreateComInterfaceForObject(target, CreateComInterfaceFlags.None);
            try
            {
                var hr = NativeMethods.RegisterDragDrop(hwnd, ptr);
                Diagnostics.Write($"[drop] RegisterDragDrop=0x{hr:X8}");
                return hr >= 0 ? target : null;
            }
            finally
            {
                // RegisterDragDrop が自分で参照を持つので、こちらの分は返す
                Marshal.Release(ptr);
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Report("ListDropTarget.Register", ex);
            return null;
        }
    }

    internal static void Revoke(nint hwnd)
    {
        if (hwnd != 0)
        {
            NativeMethods.RevokeDragDrop(hwnd);
        }
    }

    public int DragEnter(nint pDataObj, uint grfKeyState, POINTL pt, ref uint pdwEffect)
    {
        // ★ 何よりも先に書く。ここが出れば「呼ばれている」ことだけは確定する
        Diagnostics.Write("[drop] DragEnter 入口");
        try
        {
            _overCount = 0;
            _sources = ReadFileList(pDataObj);
            Diagnostics.Write($"[drop] DragEnter 読めた={_sources.Count} 件");
            pdwEffect = EffectFor(grfKeyState, pt);
        }
        catch (Exception ex)
        {
            Diagnostics.Report("ListDropTarget.DragEnter", ex);
            pdwEffect = NativeMethods.DROPEFFECT_NONE;
        }
        Diagnostics.Write($"[drop] DragEnter 出口 effect={pdwEffect}");
        return 0;
    }

    public int DragOver(uint grfKeyState, POINTL pt, ref uint pdwEffect)
    {
        var logged = ++_overCount <= LoggedOverCalls;
        if (logged)
        {
            Diagnostics.Write($"[drop] DragOver #{_overCount} 入口");
        }
        try
        {
            pdwEffect = EffectFor(grfKeyState, pt);
        }
        catch (Exception ex)
        {
            Diagnostics.Report("ListDropTarget.DragOver", ex);
            pdwEffect = NativeMethods.DROPEFFECT_NONE;
        }
        if (logged)
        {
            Diagnostics.Write($"[drop] DragOver #{_overCount} 出口 effect={pdwEffect}");
        }
        return 0;
    }

    public int DragLeave()
    {
        Diagnostics.Write("[drop] DragLeave 入口");
        try
        {
            Highlight(-1);
            _sources = [];
        }
        catch (Exception ex)
        {
            Diagnostics.Report("ListDropTarget.DragLeave", ex);
        }
        Diagnostics.Write("[drop] DragLeave 出口");
        return 0;
    }

    public int Drop(nint pDataObj, uint grfKeyState, POINTL pt, ref uint pdwEffect)
    {
        Diagnostics.Write("[drop] Drop 入口");
        try
        {
            // DragEnter で読んだものを使う。ここで読み直すと、そのぶん
            // 相手のオブジェクトへの参照をもう 1 つ作ることになる
            var sources = _sources.Count > 0 ? _sources : ReadFileList(pDataObj);
            var destination = DestinationAt(pt);
            var effect = EffectFor(grfKeyState, pt, sources);
            Highlight(-1);
            _sources = [];
            pdwEffect = effect;

            if (sources.Count == 0 || destination is null || effect == NativeMethods.DROPEFFECT_NONE)
            {
                return 0;
            }
            var move = (effect & NativeMethods.DROPEFFECT_MOVE) != 0;
            var owner = GetAncestor(_hwnd, GA_ROOT);
            Diagnostics.Write($"[drop] {sources.Count} 件 → {destination} 移動={move}");

            // OS のドラッグのループの中でモーダルを回さない。いったん抜けてから実行する
            UiDispatcher.Post(() => ShellFileOperations.Transfer(owner, sources, destination, move));
        }
        catch (Exception ex)
        {
            Diagnostics.Report("ListDropTarget.Drop", ex);
            pdwEffect = NativeMethods.DROPEFFECT_NONE;
        }
        Diagnostics.Write("[drop] Drop 出口");
        return 0;
    }

    // --- 落とし先と効果 ---

    /// <summary>いま指している行。フォルダの行ならそのフォルダ、それ以外は表示中のフォルダ。</summary>
    private string? DestinationAt(POINTL pt)
    {
        var row = RowAt(pt);
        if (row >= 0 && _folderAtRow(row) is { } folder)
        {
            return folder;
        }
        var current = _currentFolder();
        return current.Length > 0 ? current : null;
    }

    private int RowAt(POINTL pt)
    {
        var point = new POINT { X = pt.X, Y = pt.Y };
        ScreenToClient(_hwnd, ref point);
        var hit = new LVHITTESTINFO { pt = point };
        return (int)SendMessageW(_hwnd, LVM_HITTEST, 0, (nint)(&hit));
    }

    private uint EffectFor(uint grfKeyState, POINTL pt) => EffectFor(grfKeyState, pt, _sources);

    /// <summary>コピーか移動か。エクスプローラーに合わせて
    /// <b>同じドライブなら移動・違うドライブならコピー</b>を既定とし、
    /// Ctrl でコピー・Shift で移動に固定する。</summary>
    private uint EffectFor(uint grfKeyState, POINTL pt, List<string> sources)
    {
        var destination = DestinationAt(pt);
        if (sources.Count == 0 || destination is null)
        {
            Highlight(-1);
            return NativeMethods.DROPEFFECT_NONE;
        }

        // フォルダの行の上にいるときだけ、その行を強調する
        var row = RowAt(pt);
        Highlight(row >= 0 && _folderAtRow(row) is not null ? row : -1);

        if ((grfKeyState & NativeMethods.MK_CONTROL) != 0)
        {
            return NativeMethods.DROPEFFECT_COPY;
        }
        if ((grfKeyState & NativeMethods.MK_SHIFT) != 0)
        {
            return NativeMethods.DROPEFFECT_MOVE;
        }
        var sourceRoot = System.IO.Path.GetPathRoot(sources[0]) ?? "";
        var destinationRoot = System.IO.Path.GetPathRoot(destination) ?? "";
        return string.Equals(sourceRoot, destinationRoot, StringComparison.OrdinalIgnoreCase)
            ? NativeMethods.DROPEFFECT_MOVE
            : NativeMethods.DROPEFFECT_COPY;
    }

    /// <summary>落とし先の行を強調する。
    ///
    /// <para><b>この場では一覧へ書き込まない。</b>いまは OS のドラッグのループの中
    /// （しかも相手のプロセスが回しているループ）なので、そこから一覧へメッセージを送ると
    /// 再描画・通知が入れ子で走る。改名やコンテキストメニューと同じ理由で、
    /// <b>いったん抜けてから</b>当てる（BUG-029 の切り分けを兼ねる）。</para></summary>
    private void Highlight(int row)
    {
        if (_highlighted == row)
        {
            return;
        }
        var previous = _highlighted;
        _highlighted = row;
        var hwnd = _hwnd;
        UiDispatcher.Post(() => ApplyHighlight(hwnd, previous, row));
    }

    private static void ApplyHighlight(nint hwnd, int previous, int row)
    {
        var item = new LVITEMW { stateMask = LVIS_DROPHILITED };
        if (previous >= 0)
        {
            item.state = 0;
            SendMessageW(hwnd, LVM_SETITEMSTATE, previous, (nint)(&item));
        }
        if (row >= 0)
        {
            item.state = LVIS_DROPHILITED;
            SendMessageW(hwnd, LVM_SETITEMSTATE, row, (nint)(&item));
        }
    }

    /// <summary>ドラッグされてきた <c>CF_HDROP</c> からフルパスを読む。
    ///
    /// <para><b>相手のオブジェクトに包み（RCW）を立てない。</b>呼ぶのは
    /// <c>GetData</c> の 1 つだけなので、vtable から直に呼ぶ。
    /// 包みを立てると、参照の管理と解放のタイミングが一式ぶら下がり、
    /// そのどれかが原因でドラッグ中に落ちていた可能性がある（BUG-029）。</para></summary>
    private static List<string> ReadFileList(nint pDataObj)
    {
        var result = new List<string>();
        if (pDataObj == 0)
        {
            return result;
        }
        var format = new FORMATETC
        {
            cfFormat = (ushort)NativeMethods.CF_HDROP,
            dwAspect = NativeMethods.DVASPECT_CONTENT,
            lindex = -1,
            tymed = NativeMethods.TYMED_HGLOBAL,
        };
        var medium = default(STGMEDIUM);
        var hr = NativeMethods.DataObjectGetData(pDataObj, &format, &medium);
        if (hr < 0)
        {
            Diagnostics.Write($"[drop] GetData=0x{hr:X8}");
            return result;
        }
        try
        {
            var hDrop = medium.unionMember;
            if (hDrop == 0)
            {
                return result;
            }
            var count = NativeMethods.DragQueryFileW(hDrop, 0xFFFFFFFF, 0, 0);
            var buffer = stackalloc char[520];
            for (uint i = 0; i < count; i++)
            {
                var length = NativeMethods.DragQueryFileW(hDrop, i, (nint)buffer, 520);
                if (length > 0)
                {
                    result.Add(new string(buffer, 0, (int)length));
                }
            }
        }
        finally
        {
            NativeMethods.ReleaseStgMedium(&medium);
        }
        return result;
    }
}
