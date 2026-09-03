using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using ExtendExprorer.Interop;

namespace ExtendExprorer.UI;

/// <summary>一覧から掴んだファイルを外へ持ち出す（ドラッグ＆ドロップの出し側）。
///
/// <para><b>関数表は自分で組む。</b><c>IDropSource</c> も
/// 「こちらが実装したものを OS が呼ぶ」側なので、受け側（<see cref="ListDropTarget"/>）で
/// 踏んだ罠（BUG-029・Control Flow Guard に間接呼び出しを弾かれる）が
/// そのまま当てはまる。飛び先は <c>[UnmanagedCallersOnly]</c> の静的メソッドに限る。</para>
///
/// <para><b>渡すデータはシェルに作らせる。</b><c>IShellFolder::GetUIObjectOf</c> で
/// <c>IDataObject</c> を取れば、<c>CF_HDROP</c> だけでなくシェルが使う形式が一式入る。
/// 自前で <c>IDataObject</c> を書くと——それも CCW になる——エクスプローラー側の
/// 動きに追随できない箇所が必ず出る。</para>
///
/// <para><b>移動でもこちらでファイルを消さない。</b>シェルのデータオブジェクトでは、
/// 実際のコピー／移動を行うのは<b>落とされた先</b>（エクスプローラー、あるいは
/// こちらの <see cref="ListDropTarget"/>）である。出し側でも消すと二重に消すことになる。</para></summary>
internal static unsafe class ListDragSource
{
    private const int S_OK = 0;
    private const int E_NOINTERFACE = unchecked((int)0x80004002);
    private const int E_POINTER = unchecked((int)0x80004003);

    private static readonly Guid IID_IUnknown = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid IID_IDropSource = new("00000121-0000-0000-C000-000000000046");
    private static readonly Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");

    private static readonly StrategyBasedComWrappers ComWrappers = new();

    /// <summary>OS に渡すオブジェクトの中身。<c>IDropSource</c> は状態を持たないので
    /// 関数表と参照数だけ。<see cref="Interlocked"/> で数えるのは受け側と同じ理由。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeObject
    {
        public nint Vtable;
        public long RefCount;
    }

    private static nint* _vtable;

    /// <summary>いま自分でドラッグを回している最中か。
    /// 落とされた先が<b>このアプリ自身</b>のとき（ペイン間・タブ間の移動）に効いてくる。</summary>
    internal static bool IsDragging { get; private set; }

    /// <summary>ドラッグの最中に落とされた分の実処理。ループを抜けてから走らせる。
    ///
    /// <para>受け側は本来 <see cref="UiDispatcher.Post"/> で後回しにするが、
    /// <b>そのループを回しているのがこちらだと後回しにならない</b>——
    /// <c>DoDragDrop</c> の中でもメッセージは配られるので、ドラッグが終わりきる前に
    /// シェルのダイアログが開きうる。自分のループの中でも同じ原則を守るために、
    /// ここへ預けて<b>抜けた直後</b>に実行する。</para></summary>
    private static Action? _afterDrag;

    /// <summary>ドラッグ中の落とし込みを預かる。預かれたら true。</summary>
    internal static bool TryDeferUntilDragEnds(Action action)
    {
        if (!IsDragging)
        {
            return false;
        }
        // 1 回のドラッグで落ちるのは 1 度きり。上書きではなく連ねる
        var previous = _afterDrag;
        _afterDrag = previous is null ? action : previous + action;
        return true;
    }

    /// <summary>ドラッグを始める。落とされる（か取り消される）までここで戻らない。
    ///
    /// <para><b>一覧の自動追随を止めてから回す。</b>この中でメッセージのループが回るので、
    /// 監視からの読み直しが挟まると、掴んでいる最中に一覧が作り直される。</para></summary>
    /// <returns>実際に何か持ち出されたら true（呼び出し側のログ用）。</returns>
    internal static bool Begin(nint hwnd, string folderPath, IReadOnlyList<string> itemNames)
    {
        if (itemNames.Count == 0 || folderPath.Length == 0)
        {
            return false;
        }
        var dataObject = CreateDataObject(hwnd, folderPath, itemNames);
        if (dataObject == 0)
        {
            Diagnostics.Write("[drag] IDataObject を作れない");
            return false;
        }

        var native = (NativeObject*)NativeMemory.Alloc((nuint)sizeof(NativeObject));
        native->Vtable = (nint)Vtable();
        native->RefCount = 1;
        try
        {
            const uint allowed = NativeMethods.DROPEFFECT_COPY | NativeMethods.DROPEFFECT_MOVE
                | NativeMethods.DROPEFFECT_LINK;
            Diagnostics.Write($"[drag] 開始 {itemNames.Count} 件 ← {folderPath}");
            IsDragging = true;
            int hr;
            uint effect;
            try
            {
                hr = NativeMethods.DoDragDrop(dataObject, (nint)native, allowed, out effect);
            }
            finally
            {
                IsDragging = false;
            }
            Diagnostics.Write($"[drag] 終了 0x{hr:X8} effect={effect}"
                + (hr == NativeMethods.DRAGDROP_S_DROP ? "（落とされた）" : "（取り消し）"));

            // このアプリ自身に落とされた分は、ループを抜けたここで走らせる
            var pending = _afterDrag;
            _afterDrag = null;
            if (pending is not null)
            {
                Diagnostics.Write("[drag] 自分に落とされた分をここで実行する");
                UiDispatcher.Post(pending);
            }
            return hr == NativeMethods.DRAGDROP_S_DROP;
        }
        catch (Exception ex)
        {
            Diagnostics.Report("ListDragSource.Begin", ex);
            return false;
        }
        finally
        {
            ReleaseNative(native);
            Marshal.Release(dataObject);
        }
    }

    /// <summary>選択中の項目のシェル側 <c>IDataObject</c>。
    /// コンテキストメニューを取るのと同じ道筋（親フォルダ ＋ 子 PIDL）。</summary>
    private static nint CreateDataObject(nint hwnd, string folderPath, IReadOnlyList<string> itemNames)
    {
        var fullPidls = new List<nint>();
        try
        {
            foreach (var name in itemNames)
            {
                if (NativeMethods.SHParseDisplayName(System.IO.Path.Combine(folderPath, name),
                        0, out var pidl, 0, out _) >= 0 && pidl != 0)
                {
                    fullPidls.Add(pidl);
                }
            }
            if (fullPidls.Count == 0)
            {
                return 0; // 掴んだ直後に全部消えた等
            }

            // 全部同じフォルダの中なので親は 1 つ。子 PIDL は絶対 PIDL の中を
            // 指しているので、個別には解放しない
            IShellFolder? parent = null;
            var children = new nint[fullPidls.Count];
            for (var i = 0; i < fullPidls.Count; i++)
            {
                if (NativeMethods.SHBindToParent(fullPidls[i], in IID_IShellFolder,
                        out var parentPtr, out children[i]) < 0)
                {
                    return 0;
                }
                if (parent is null)
                {
                    try
                    {
                        parent = (IShellFolder)ComWrappers.GetOrCreateObjectForComInstance(
                            parentPtr, CreateObjectFlags.None);
                    }
                    finally
                    {
                        Marshal.Release(parentPtr);
                    }
                }
                else
                {
                    Marshal.Release(parentPtr);
                }
            }

            fixed (nint* pChildren = children)
            {
                var hr = parent!.GetUIObjectOf(hwnd, (uint)children.Length, (nint)pChildren,
                    in NativeMethods.IID_IDataObject, 0, out var dataPtr);
                if (hr < 0 || dataPtr == 0)
                {
                    Diagnostics.Write($"[drag] GetUIObjectOf(IDataObject)=0x{hr:X8}");
                    return 0;
                }
                return dataPtr;
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Report("ListDragSource.CreateDataObject", ex);
            return 0;
        }
        finally
        {
            foreach (var pidl in fullPidls)
            {
                Marshal.FreeCoTaskMem(pidl);
            }
        }
    }

    // --- 関数表 ---

    /// <summary>関数表は 1 つだけ作って使い回す。中身は
    /// <c>[UnmanagedCallersOnly]</c> の静的メソッド＝必ずイメージの中にある。</summary>
    private static nint* Vtable()
    {
        if (_vtable is not null)
        {
            return _vtable;
        }
        var table = (nint*)NativeMemory.Alloc(5, (nuint)sizeof(nint));
        table[0] = (nint)(delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)&QueryInterface;
        table[1] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&AddRef;
        table[2] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&Release;
        table[3] = (nint)(delegate* unmanaged[Stdcall]<nint, int, uint, int>)&QueryContinueDrag;
        table[4] = (nint)(delegate* unmanaged[Stdcall]<nint, uint, int>)&GiveFeedback;
        _vtable = table;
        return table;
    }

    private static long ReleaseNative(NativeObject* native)
    {
        var remaining = Interlocked.Decrement(ref native->RefCount);
        if (remaining > 0)
        {
            return remaining;
        }
        NativeMemory.Free(native);
        return 0;
    }

    // --- OS から呼ばれる 5 つ。例外は 1 つも外へ出さない ---

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
        if (iid != IID_IUnknown && iid != IID_IDropSource)
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

    /// <summary>ドラッグを続けるか。<b>マウスが動くたびに呼ばれる</b>ので、
    /// ここでは何もしない——判断に必要なものは全部引数で渡ってくる。
    ///
    /// <list type="bullet">
    /// <item>Esc → 取り消し</item>
    /// <item>ボタンが離された → そこで落とす</item>
    /// <item>それ以外 → 続ける</item>
    /// </list></summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int QueryContinueDrag(nint self, int escapePressed, uint grfKeyState)
    {
        if (escapePressed != 0)
        {
            return NativeMethods.DRAGDROP_S_CANCEL;
        }
        var buttons = grfKeyState & (NativeMethods.MK_LBUTTON | NativeMethods.MK_RBUTTON);
        return buttons == 0 ? NativeMethods.DRAGDROP_S_DROP : S_OK;
    }

    /// <summary>カーソルの見た目。<b>OS の既定に任せる</b>と答える。
    /// 自前で用意すると「＋ コピー」「→ 移動」の表示がエクスプローラーと食い違う。</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GiveFeedback(nint self, uint effect) => NativeMethods.DRAGDROP_S_USEDEFAULTCURSORS;
}
