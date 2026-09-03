using System.Runtime.InteropServices;
using static ExtendExprorer.Interop.Win32;

namespace ExtendExprorer.Interop;

/// <summary>ドラッグ＆ドロップの宣言（受け取る側・持ち出す側とも）。
///
/// <para><b>ここだけ COM の向きが逆になる。</b>これまではシェルのオブジェクトを
/// こちらが呼ぶ形だったが、<c>IDropTarget</c>／<c>IDropSource</c> は
/// <b>こちらが実装したものを OS が呼ぶ</b>。
/// その関数表は生成に任せず自分で組んでいる
/// （<see cref="UI.ListDropTarget"/>／<see cref="UI.ListDragSource"/>・BUG-029）。</para></summary>
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
    internal const uint MK_LBUTTON = 0x0001;
    internal const uint MK_RBUTTON = 0x0002;

    /// <summary><c>IDropSource</c> が返す成功コード。<b>負の値ではないので
    /// 「失敗」と一緒に扱わないこと。</b><c>DoDragDrop</c> の戻り値でもある。</summary>
    internal const int DRAGDROP_S_DROP = 0x00040100;
    internal const int DRAGDROP_S_CANCEL = 0x00040101;
    internal const int DRAGDROP_S_USEDEFAULTCURSORS = 0x00040102;

    internal static readonly Guid IID_IDataObject = new("0000010E-0000-0000-C000-000000000046");

    /// <summary>ドラッグ中の座標（<c>POINTL</c>・<b>画面座標</b>）を取り出す。
    ///
    /// <para><b>構造体のまま受け取らない。</b>x64 では 8 バイトの構造体は
    /// レジスタに 1 つ載せて値で渡される。同じ大きさの整数で受けて自分でほどく方が、
    /// 呼び出し規約の解釈を間に挟まずに済む。</para></summary>
    internal static POINT PointOfDrag(ulong pt) => new()
    {
        X = (int)(pt & 0xFFFFFFFF),
        Y = (int)(pt >> 32),
    };

    [LibraryImport("ole32.dll", EntryPoint = "RegisterDragDrop")]
    internal static partial int RegisterDragDrop(nint hwnd, nint dropTarget);

    [LibraryImport("ole32.dll", EntryPoint = "RevokeDragDrop")]
    internal static partial int RevokeDragDrop(nint hwnd);

    /// <summary>ドラッグを始めて、離されるまで<b>この中でループが回る</b>。
    /// 戻ってくるのは落とされたか取り消されたとき。
    ///
    /// <para>受け側は <c>pdwEffect</c> に実際の効果を書いて返す。<b>移動でも
    /// こちらでファイルを消さない</b>——シェルのデータオブジェクトを渡しているので、
    /// 移動そのものを行うのは落とされた先（エクスプローラー／こちらの一覧）である。</para></summary>
    [LibraryImport("ole32.dll", EntryPoint = "DoDragDrop")]
    internal static partial int DoDragDrop(nint dataObject, nint dropSource, uint okEffects, out uint effect);

    [LibraryImport("ole32.dll", EntryPoint = "ReleaseStgMedium")]
    internal static unsafe partial void ReleaseStgMedium(STGMEDIUM* medium);

    /// <summary><c>IDataObject::GetData</c> を<b>関数表から直に</b>呼ぶ。
    ///
    /// <para>相手のオブジェクトのために包み（RCW）を立てると、生成された
    /// インターフェイス・参照の管理・解放のタイミングが一式ぶら下がる。
    /// <b>1 つのメソッドを呼ぶだけなら、その一式は要らない。</b></para>
    ///
    /// <para>スロット 3 ＝ <c>IUnknown</c> の 3 つ（QueryInterface / AddRef / Release）の次。</para></summary>
    internal static unsafe int DataObjectGetData(nint dataObject, FORMATETC* format, STGMEDIUM* medium)
    {
        var vtable = *(nint**)dataObject;
        var getData = (delegate* unmanaged[Stdcall]<nint, FORMATETC*, STGMEDIUM*, int>)vtable[3];
        return getData(dataObject, format, medium);
    }
}
