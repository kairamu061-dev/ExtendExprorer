using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace ExtendExprorer.Interop;

/// <summary>ドラッグ＆ドロップ（受け取る側）の宣言。
///
/// <para><b>ここだけ向きが逆になる。</b>これまでの COM はすべて「シェルのオブジェクトを
/// こちらが呼ぶ」形（RCW）だったが、<c>IDropTarget</c> は<b>こちらが実装したものを
/// OS に呼ばせる</b>（CCW）。Native AOT では実装クラスに
/// <c>[GeneratedComClass]</c> が要る（<see cref="UI.ListDropTarget"/>）。</para></summary>
[GeneratedComInterface]
[Guid("00000122-0000-0000-C000-000000000046")]
internal partial interface IDropTarget
{
    [PreserveSig] int DragEnter(nint pDataObj, uint grfKeyState, ulong pt, ref uint pdwEffect);
    [PreserveSig] int DragOver(uint grfKeyState, ulong pt, ref uint pdwEffect);
    [PreserveSig] int DragLeave();
    [PreserveSig] int Drop(nint pDataObj, uint grfKeyState, ulong pt, ref uint pdwEffect);
}

[StructLayout(LayoutKind.Sequential)]
internal struct FORMATETC
{
    public ushort cfFormat;
    public nint ptd;
    public uint dwAspect;
    public int lindex;
    public uint tymed;
}

[StructLayout(LayoutKind.Sequential)]
internal struct STGMEDIUM
{
    public uint tymed;
    public nint unionMember;
    public nint pUnkForRelease;
}

internal static partial class NativeMethods
{
    internal const uint DVASPECT_CONTENT = 1;
    internal const uint TYMED_HGLOBAL = 1;

    internal const uint DROPEFFECT_NONE = 0;

    /// <summary>ドラッグ中に押されているキー（<c>grfKeyState</c>）。</summary>
    internal const uint MK_CONTROL = 0x0008;
    internal const uint MK_SHIFT = 0x0004;

    internal const int DRAGDROP_S_DROP = 0x00040100;

    /// <summary>ドラッグ中の座標（<c>POINTL</c>・<b>画面座標</b>）を取り出す。
    ///
    /// <para><b>構造体のまま受け取らない。</b>x64 では 8 バイトの構造体は
    /// レジスタに 1 つ載せて値で渡されるが、「こちらが実装したものを OS が呼ぶ」側では
    /// その受け取り方がずれ、スタックの破壊とみなされて即死することがある
    /// （<c>0xc0000409</c>・BUG-029）。<b>同じ大きさの整数で受けて自分でほどく</b>。</para></summary>
    internal static POINT PointOfDrag(ulong pt) => new()
    {
        X = (int)(pt & 0xFFFFFFFF),
        Y = (int)(pt >> 32),
    };

    [LibraryImport("ole32.dll", EntryPoint = "RegisterDragDrop")]
    internal static partial int RegisterDragDrop(nint hwnd, nint dropTarget);

    [LibraryImport("ole32.dll", EntryPoint = "RevokeDragDrop")]
    internal static partial int RevokeDragDrop(nint hwnd);

    [LibraryImport("ole32.dll", EntryPoint = "ReleaseStgMedium")]
    internal static unsafe partial void ReleaseStgMedium(STGMEDIUM* medium);

    /// <summary><c>IDataObject::GetData</c> を<b>vtable から直に</b>呼ぶ。
    ///
    /// <para>相手のオブジェクトのために包み（RCW）を立てると、ソース生成の
    /// インターフェイス・参照の管理・解放のタイミングが一式ぶら下がる。
    /// <b>1 つのメソッドを呼ぶだけなら、その一式は要らない。</b>
    /// ドラッグ中に落ちる件（BUG-029）で、この経路から包みを外した。</para>
    ///
    /// <para>スロット 3 ＝ <c>IUnknown</c> の 3 つ（QueryInterface / AddRef / Release）の次。</para></summary>
    internal static unsafe int DataObjectGetData(nint dataObject, FORMATETC* format, STGMEDIUM* medium)
    {
        var vtable = *(nint**)dataObject;
        var getData = (delegate* unmanaged[Stdcall]<nint, FORMATETC*, STGMEDIUM*, int>)vtable[3];
        return getData(dataObject, format, medium);
    }
}
