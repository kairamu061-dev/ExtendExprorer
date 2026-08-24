using System.Runtime.InteropServices;
using static ExtendExprorer.Interop.Win32;

namespace ExtendExprorer.Interop;

/// <summary><c>SysTreeView32</c>（フォルダツリー）のメッセージ・通知・構造体。
///
/// <para>通知の共通部分（<c>NMHDR</c>・<c>NMCUSTOMDRAW</c>・<c>CDDS_*</c>・<c>CDRF_*</c>・
/// <c>NM_*</c>）は <see cref="ListView"/> に置いてあるものをそのまま使う。
/// コモンコントロール共通の型なので、二重に定義しない。</para>
///
/// <para><b>行の高さは指定しない。</b><c>TVM_SETITEMHEIGHT</c> は使わず、フォントと
/// 16px のイメージリストから控除させる。高さを固定すると中身がその枠に押し込められ、
/// 行ピッチは 19px でもアイコンと文字が潰れる（旧版の BUG-014）。
/// なお、このメッセージは <c>TVS_NONEVENHEIGHT</c> が無いと奇数を偶数へ切り下げるので、
/// 将来使うことがあっても「19 を渡したら 18 になる」ことに注意。</para></summary>
internal static class TreeViewControl
{
    internal const string WC_TREEVIEW = "SysTreeView32";

    internal const uint TVS_HASBUTTONS = 0x0001;
    internal const uint TVS_HASLINES = 0x0002;
    internal const uint TVS_LINESATROOT = 0x0004;
    internal const uint TVS_SHOWSELALWAYS = 0x0020;
    internal const uint TVS_TRACKSELECT = 0x0200;
    internal const uint TVS_FULLROWSELECT = 0x1000;
    internal const uint TVS_NONEVENHEIGHT = 0x4000;

    private const uint TV_FIRST = 0x1100;

    internal const uint TVM_DELETEITEM = TV_FIRST + 1;
    internal const uint TVM_EXPAND = TV_FIRST + 2;
    internal const uint TVM_SETIMAGELIST = TV_FIRST + 9;
    internal const uint TVM_GETNEXTITEM = TV_FIRST + 10;
    internal const uint TVM_SELECTITEM = TV_FIRST + 11;
    internal const uint TVM_HITTEST = TV_FIRST + 17;
    internal const uint TVM_ENSUREVISIBLE = TV_FIRST + 20;
    internal const uint TVM_GETITEMHEIGHT = TV_FIRST + 28;
    internal const uint TVM_INSERTITEMW = TV_FIRST + 50;
    internal const uint TVM_GETITEMW = TV_FIRST + 62;
    internal const uint TVM_SETITEMW = TV_FIRST + 63;

    internal const nint TVSIL_NORMAL = 0;

    // commctrl.h では ((HTREEITEM)(ULONG_PTR)-0x10000) 等。ULONG_PTR は 64bit なので
    // 0xFFFF0000 ではなく符号拡張した値になる（32bit の値を書くと挿入位置が化ける）
    internal const nint TVI_ROOT = -0x10000;
    internal const nint TVI_LAST = -0x0FFFE;

    internal const uint TVIF_TEXT = 0x0001;
    internal const uint TVIF_IMAGE = 0x0002;
    internal const uint TVIF_PARAM = 0x0004;
    internal const uint TVIF_HANDLE = 0x0010;
    internal const uint TVIF_SELECTEDIMAGE = 0x0020;
    internal const uint TVIF_CHILDREN = 0x0040;

    internal const nint TVE_COLLAPSE = 1;
    internal const nint TVE_EXPAND = 2;

    /// <summary>ヒットテストの結果のうち「項目そのもの」を指すもの
    /// （アイコン・ラベル・状態アイコン）。シェブロン（<c>TVHT_ONITEMBUTTON</c>）は含まない。</summary>
    internal const uint TVHT_ONITEM = 0x0046;

    /// <summary>名前より右の余白。<c>TVS_FULLROWSELECT</c> を付けているので行の一部で、
    /// エクスプローラーもここのクリックで移動する。字下げ（<c>TVHT_ONITEMINDENT</c>）は含めない。</summary>
    internal const uint TVHT_ONITEMRIGHT = 0x0020;

    private const int TVN_FIRST = -400;

    internal const int TVN_SELCHANGEDW = TVN_FIRST - 51;
    internal const int TVN_ITEMEXPANDINGW = TVN_FIRST - 54;
    internal const int TVN_DELETEITEMW = TVN_FIRST - 58;

    internal const int NM_CLICK = -2;
    internal const int NM_RETURN = -4;

    [StructLayout(LayoutKind.Sequential)]
    internal struct TVITEMW
    {
        public uint mask;
        public nint hItem;
        public uint state;
        public uint stateMask;
        public nint pszText;
        public int cchTextMax;
        public int iImage;
        public int iSelectedImage;
        public int cChildren;
        public nint lParam;
    }

    /// <summary><c>TVITEMW</c> の拡張版。<c>TVINSERTSTRUCTW</c> はこちらを持つ。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct TVITEMEXW
    {
        public uint mask;
        public nint hItem;
        public uint state;
        public uint stateMask;
        public nint pszText;
        public int cchTextMax;
        public int iImage;
        public int iSelectedImage;
        public int cChildren;
        public nint lParam;
        public int iIntegral;
        public uint uStateEx;
        public nint hwnd;
        public int iExpandedImage;
        public int iReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TVINSERTSTRUCTW
    {
        public nint hParent;
        public nint hInsertAfter;
        public TVITEMEXW item;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NMTREEVIEWW
    {
        public ListView.NMHDR hdr;
        public uint action;
        public TVITEMW itemOld;
        public TVITEMW itemNew;
        public POINT ptDrag;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NMTVCUSTOMDRAW
    {
        public ListView.NMCUSTOMDRAW nmcd;
        public uint clrText;
        public uint clrTextBk;
        public int iLevel;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TVHITTESTINFO
    {
        public POINT pt;
        public uint flags;
        public nint hItem;
    }
}
