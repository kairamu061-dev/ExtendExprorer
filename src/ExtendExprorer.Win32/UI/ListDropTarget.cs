using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ExtendExprorer.Interop;
using ExtendExprorer.Services;
using static ExtendExprorer.Interop.ListView;
using static ExtendExprorer.Interop.Win32;

namespace ExtendExprorer.UI;

/// <summary>一覧に落とされたファイルを受け取る（ドラッグ＆ドロップの受け側）。
///
/// <para><b>関数表を自分で組む。</b>これは「こちらが実装したものを OS が呼ぶ」側で、
/// しかも相手（エクスプローラー）は別のプロセスなので、呼び出しは RPC 経由で入ってくる。
/// 生成に任せた関数表では、その間接呼び出しが Control Flow Guard に弾かれて
/// プロセスが即死した（BUG-029・<c>FAST_FAIL_GUARD_ICALL_CHECK_FAILURE</c>）。</para>
///
/// <para>この実行ファイルは CFG 無しでリンクされている（配布物の PE ヘッダで確認済み。
/// <c>DllCharacteristics=0x8160</c>＝<c>GUARD_CF</c> は立っていない）。
/// <b>CFG 無しのイメージの中にあるコードは、常に正当な飛び先として扱われる。</b>
/// それでも弾かれたということは、飛び先が<b>イメージの中に無かった</b>ということ。
/// だから飛び先は <c>[UnmanagedCallersOnly]</c> の静的メソッド
/// （＝確実にイメージの中にあるアドレス）に限り、関数表は 1 つだけ作って解放しない。
/// これで落ちなくなった（実機で確認済み）。</para>
///
/// <para><b>座標は構造体で受け取らない。</b><c>POINTL</c> は 8 バイトで、x64 では
/// レジスタに 1 つ載って値で渡される。同じ大きさの整数で受けて自分でほどく。</para>
///
/// <para>実際のコピー／移動は<b>この呼び出しの中では行わない</b>。相手のプロセスが
/// 回しているループの中でモーダルを開くことになるため、いったん抜けてから実行する。</para></summary>
internal sealed unsafe class ListDropTarget
{
    private const int S_OK = 0;
    private const int E_NOINTERFACE = unchecked((int)0x80004002);
    private const int E_POINTER = unchecked((int)0x80004003);

    private static readonly Guid IID_IUnknown = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid IID_IDropTarget = new("00000122-0000-0000-C000-000000000046");

    /// <summary>OS に渡すオブジェクトの中身。先頭が関数表であることが COM の約束。
    ///
    /// <para>参照数は <see cref="Interlocked"/> で数える。<b>別プロセスからの呼び出しは
    /// RPC のスレッドから入ってくる</b>ので、素の <c>++</c> では数え損ねる。
    /// 数え損ねればまだ使われているものを解放してしまい、
    /// いま調べている症状と<b>見分けがつかない</b>不具合になる。</para></summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeObject
    {
        public nint Vtable;
        public long RefCount;
        public nint Handle; // マネージド側の ListDropTarget への GCHandle
    }

    private static nint* _vtable;

    private readonly nint _hwnd;
    private readonly Func<string> _currentFolder;
    private readonly Func<int, string?> _folderAtRow;

    private List<string> _sources = [];
    private int _highlighted = -1;
    private int _overCount;

    private const int LoggedOverCalls = 5;

    internal ListDropTarget(nint hwnd, Func<string> currentFolder, Func<int, string?> folderAtRow)
    {
        _hwnd = hwnd;
        _currentFolder = currentFolder;
        _folderAtRow = folderAtRow;
    }

    // --- 関数表 ---

    /// <summary>関数表は 1 つだけ作って使い回す。中身は
    /// <c>[UnmanagedCallersOnly]</c> の静的メソッドのアドレスなので、必ずイメージの中にある。</summary>
    private static nint* Vtable()
    {
        if (_vtable is not null)
        {
            return _vtable;
        }
        var table = (nint*)NativeMemory.Alloc(7, (nuint)sizeof(nint));
        table[0] = (nint)(delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)&QueryInterface;
        table[1] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&AddRef;
        table[2] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&Release;
        table[3] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, uint, ulong, uint*, int>)&DragEnterThunk;
        table[4] = (nint)(delegate* unmanaged[Stdcall]<nint, uint, ulong, uint*, int>)&DragOverThunk;
        table[5] = (nint)(delegate* unmanaged[Stdcall]<nint, int>)&DragLeaveThunk;
        table[6] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, uint, ulong, uint*, int>)&DropThunk;
        _vtable = table;
        return table;
    }

    /// <summary>この一覧をドロップ先として登録する。</summary>
    internal static ListDropTarget? Register(nint hwnd, Func<string> currentFolder, Func<int, string?> folderAtRow)
    {
        try
        {
            var target = new ListDropTarget(hwnd, currentFolder, folderAtRow);
            var native = (NativeObject*)NativeMemory.Alloc((nuint)sizeof(NativeObject));
            native->Vtable = (nint)Vtable();
            native->RefCount = 1;
            native->Handle = GCHandle.ToIntPtr(GCHandle.Alloc(target));

            var hr = NativeMethods.RegisterDragDrop(hwnd, (nint)native);

            if (hr < 0)
            {
                Diagnostics.Write($"[drop] RegisterDragDrop=0x{hr:X8}");
                ReleaseNative(native);
                return null;
            }

            // 登録が成功したなら OLE が参照を 1 つ持っている（実測で 2 になることを確認済み）。
            // こちらが作ったときの 1 つを返し、残る 1 つは RevokeDragDrop で返る。
            //
            // ★ 万一 OLE が参照を取っていなかったら手放さない。返した瞬間に解放され、
            //   OLE の側に宙に浮いたポインタが残る——ドラッグが来た時点で落ちる形になる。
            //   実測では起きていないが、ここで落ちると原因が遠いので機械的に避ける
            //
            // ★ 返す前と後を両方出す。片方だけだと、返せていなくても
            //   同じ数字に見える（＝戻し損ねが見えない）
            var before = Interlocked.Read(ref native->RefCount);
            var remaining = before > 1 ? ReleaseNative(native) : before;

            Diagnostics.Write(
                $"[drop] RegisterDragDrop=0x{hr:X8} 参照数={before}→{remaining}（OLE の 1 つが残る）");
            return target;
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

    /// <summary>参照を 1 つ返す。残りの数を返し、0 になったらその場で捨てる。</summary>
    private static long ReleaseNative(NativeObject* native)
    {
        var remaining = Interlocked.Decrement(ref native->RefCount);
        if (remaining > 0)
        {
            return remaining;
        }
        if (native->Handle != 0)
        {
            GCHandle.FromIntPtr(native->Handle).Free();
        }
        NativeMemory.Free(native);
        return 0;
    }

    private static ListDropTarget? Self(nint self)
    {
        if (self == 0)
        {
            return null;
        }
        var handle = ((NativeObject*)self)->Handle;
        return handle == 0 ? null : GCHandle.FromIntPtr(handle).Target as ListDropTarget;
    }

    // --- OS から呼ばれる 7 つ。例外は 1 つも外へ出さない ---

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int QueryInterface(nint self, Guid* riid, nint* ppv)
    {
        if (ppv is null)
        {
            return E_POINTER;
        }
        *ppv = 0;
        if (self == 0 || riid is null)
        {
            return E_POINTER;
        }
        var iid = *riid;
        if (iid != IID_IUnknown && iid != IID_IDropTarget)
        {
            return E_NOINTERFACE;
        }
        Interlocked.Increment(ref ((NativeObject*)self)->RefCount);
        *ppv = self;
        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint AddRef(nint self) =>
        self == 0 ? 0 : (uint)Interlocked.Increment(ref ((NativeObject*)self)->RefCount);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint Release(nint self)
    {
        if (self == 0)
        {
            return 0;
        }
        return (uint)ReleaseNative((NativeObject*)self);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int DragEnterThunk(nint self, nint pDataObj, uint grfKeyState, ulong pt, uint* pdwEffect)
    {
        // ★ 何よりも先に書く。ここが出れば「呼ばれている」ことだけは確定する
        Diagnostics.Write("[drop] DragEnter 入口");
        var effect = NativeMethods.DROPEFFECT_NONE;
        try
        {
            effect = Self(self)?.OnDragEnter(pDataObj, grfKeyState, pt) ?? NativeMethods.DROPEFFECT_NONE;
        }
        catch (Exception ex)
        {
            Diagnostics.Report("ListDropTarget.DragEnter", ex);
        }
        if (pdwEffect is not null)
        {
            *pdwEffect = effect;
        }
        Diagnostics.Write($"[drop] DragEnter 出口 effect={effect}");
        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int DragOverThunk(nint self, uint grfKeyState, ulong pt, uint* pdwEffect)
    {
        var effect = NativeMethods.DROPEFFECT_NONE;
        try
        {
            effect = Self(self)?.OnDragOver(grfKeyState, pt) ?? NativeMethods.DROPEFFECT_NONE;
        }
        catch (Exception ex)
        {
            Diagnostics.Report("ListDropTarget.DragOver", ex);
        }
        if (pdwEffect is not null)
        {
            *pdwEffect = effect;
        }
        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int DragLeaveThunk(nint self)
    {
        Diagnostics.Write("[drop] DragLeave");
        try
        {
            Self(self)?.OnDragLeave();
        }
        catch (Exception ex)
        {
            Diagnostics.Report("ListDropTarget.DragLeave", ex);
        }
        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int DropThunk(nint self, nint pDataObj, uint grfKeyState, ulong pt, uint* pdwEffect)
    {
        Diagnostics.Write("[drop] Drop 入口");
        var effect = NativeMethods.DROPEFFECT_NONE;
        try
        {
            effect = Self(self)?.OnDrop(pDataObj, grfKeyState, pt) ?? NativeMethods.DROPEFFECT_NONE;
        }
        catch (Exception ex)
        {
            Diagnostics.Report("ListDropTarget.Drop", ex);
        }
        if (pdwEffect is not null)
        {
            *pdwEffect = effect;
        }
        Diagnostics.Write("[drop] Drop 出口");
        return S_OK;
    }

    // --- 中身 ---

    private uint OnDragEnter(nint pDataObj, uint grfKeyState, ulong pt)
    {
        _overCount = 0;
        _sources = ReadFileList(pDataObj);
        Diagnostics.Write($"[drop] DragEnter 読めた={_sources.Count} 件");
        return EffectFor(grfKeyState, pt, _sources);
    }

    private uint OnDragOver(uint grfKeyState, ulong pt)
    {
        if (++_overCount <= LoggedOverCalls)
        {
            Diagnostics.Write($"[drop] DragOver #{_overCount}");
        }
        return EffectFor(grfKeyState, pt, _sources);
    }

    private void OnDragLeave()
    {
        Highlight(-1);
        _sources = [];
    }

    private uint OnDrop(nint pDataObj, uint grfKeyState, ulong pt)
    {
        // DragEnter で読んだものを使う
        var sources = _sources.Count > 0 ? _sources : ReadFileList(pDataObj);
        var destination = DestinationAt(pt);
        var effect = EffectFor(grfKeyState, pt, sources);
        Highlight(-1);
        _sources = [];

        if (sources.Count == 0 || destination is null || effect == NativeMethods.DROPEFFECT_NONE)
        {
            return NativeMethods.DROPEFFECT_NONE;
        }
        var move = (effect & NativeMethods.DROPEFFECT_MOVE) != 0;

        // 元の場所へそのまま落とした（移動）＝何も起きないのが正しい。
        // 実処理側でも弾いてはいるが、ここで抜ければログにも残らない
        if (move && AllAlreadyIn(sources, destination))
        {
            Diagnostics.Write($"[drop] 元の場所と同じなので何もしない（{sources.Count} 件）");
            return NativeMethods.DROPEFFECT_NONE;
        }
        var owner = GetAncestor(_hwnd, GA_ROOT);
        Diagnostics.Write($"[drop] {sources.Count} 件 → {destination} 移動={move}");

        // 相手のプロセスが回しているループの中でモーダルを開かない。
        // 掴んだのがこのアプリ自身なら、回っているのはこちらの DoDragDrop なので、
        // 後回しにしても同じループの中で走ってしまう。そちらへ預ける
        void Run() => ShellFileOperations.Transfer(owner, sources, destination, move);
        if (!ListDragSource.TryDeferUntilDragEnds(Run))
        {
            UiDispatcher.Post(Run);
        }
        return effect;
    }

    // --- 落とし先と効果 ---

    /// <summary>いま指している行。フォルダの行ならそのフォルダ、それ以外は表示中のフォルダ。</summary>
    private string? DestinationAt(ulong pt)
    {
        var row = RowAt(pt);
        if (row >= 0 && _folderAtRow(row) is { } folder)
        {
            return folder;
        }
        var current = _currentFolder();
        return current.Length > 0 ? current : null;
    }

    private int RowAt(ulong pt)
    {
        var point = NativeMethods.PointOfDrag(pt);
        ScreenToClient(_hwnd, ref point);
        var hit = new LVHITTESTINFO { pt = point };
        return (int)SendMessageW(_hwnd, LVM_HITTEST, 0, (nint)(&hit));
    }

    /// <summary>コピーか移動か。エクスプローラーに合わせて
    /// <b>同じドライブなら移動・違うドライブならコピー</b>を既定とし、
    /// Ctrl でコピー・Shift で移動に固定する。</summary>
    private uint EffectFor(uint grfKeyState, ulong pt, List<string> sources)
    {
        var destination = DestinationAt(pt);

        // 掴んでいるフォルダ自身（やその中）へは落とさせない。
        // 置けるように見えて、離すとシェルが
        // 「受け側のフォルダーは、送り側フォルダーのサブフォルダーです」と断ってくる
        if (sources.Count == 0 || destination is null || IsInsideDragged(destination, sources))
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

    /// <summary>落とし先が、掴んでいるもの自身か、その中か。
    /// <b>自分の中へは入れられない</b>ので、落とし先として扱わない。</summary>
    private static bool IsInsideDragged(string destination, List<string> sources)
    {
        var dest = System.IO.Path.TrimEndingDirectorySeparator(destination);
        foreach (var source in sources)
        {
            var src = System.IO.Path.TrimEndingDirectorySeparator(source);
            if (string.Equals(dest, src, StringComparison.OrdinalIgnoreCase))
            {
                return true; // そのフォルダ自身の行
            }
            if (dest.Length > src.Length + 1
                && dest.StartsWith(src, StringComparison.OrdinalIgnoreCase)
                && (dest[src.Length] == '\\' || dest[src.Length] == '/'))
            {
                return true; // そのフォルダの中
            }
        }
        return false;
    }

    /// <summary>掴んでいるものが、すべてもう落とし先の中にあるか（＝移動しても動かない）。</summary>
    private static bool AllAlreadyIn(List<string> sources, string destination)
    {
        var dest = System.IO.Path.TrimEndingDirectorySeparator(destination);
        foreach (var source in sources)
        {
            var parent = System.IO.Path.GetDirectoryName(
                System.IO.Path.TrimEndingDirectorySeparator(source));
            if (!string.Equals(parent, dest, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>落とし先の行を強調する。
    ///
    /// <para><b>この場では一覧へ書き込まない。</b>いまは相手のプロセスが回している
    /// ループの中なので、そこから一覧へメッセージを送ると再描画・通知が入れ子で走る。
    /// いったん抜けてから当てる（改名・コンテキストメニューと同じ扱い）。</para></summary>
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
    /// 相手のオブジェクトには包み（RCW）を立てず、<c>GetData</c> を関数表から直に呼ぶ。</summary>
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
