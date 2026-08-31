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
    [PreserveSig] int DragEnter(nint pDataObj, uint grfKeyState, POINTL pt, ref uint pdwEffect);
    [PreserveSig] int DragOver(uint grfKeyState, POINTL pt, ref uint pdwEffect);
    [PreserveSig] int DragLeave();
    [PreserveSig] int Drop(nint pDataObj, uint grfKeyState, POINTL pt, ref uint pdwEffect);
}

/// <summary>ドラッグ中の座標（<b>画面座標</b>で来る）。値渡しなので blittable に保つ。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct POINTL
{
    public int X;
    public int Y;
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
