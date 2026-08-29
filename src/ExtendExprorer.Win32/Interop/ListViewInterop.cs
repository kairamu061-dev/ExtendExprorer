using System.Runtime.InteropServices;
using static ExtendExprorer.Interop.Win32;

namespace ExtendExprorer.Interop;

/// <summary><c>SysListView32</c>（詳細表示・オーナーデータ）とヘッダの宣言。
///
/// <para><b>オーナーデータ（<c>LVS_OWNERDATA</c>）が移行の要</b>。項目の実体をコントロールに
/// 持たせず、行数だけ伝えて<b>見えている行の内容だけ</b> <c>LVN_GETDISPINFOW</c> で聞かれる方式にする。
/// 1 万件のフォルダでも作る文字列は画面に出ている数十行分で済むので、
/// 現行 WinUI 版のように行ごとの ViewModel と整形済み文字列を抱え込まずに済む。</para>
///
/// <para>構造体はすべて blittable に保つこと（<c>string</c> フィールドを持たせると
/// <c>sizeof</c> がネイティブの大きさを返さなくなる）。文字列ポインタは <c>nint</c> で宣言する。</para></summary>
internal static class ListView
{
    internal const string WC_LISTVIEW = "SysListView32";

    // --- スタイル ---
    internal const uint LVS_REPORT = 0x0001;
    internal const uint LVS_SHOWSELALWAYS = 0x0008;
    internal const uint LVS_SHAREIMAGELISTS = 0x0040;
    internal const uint LVS_EDITLABELS = 0x0200;
    internal const uint LVS_OWNERDATA = 0x1000;

    internal const uint LVS_EX_FULLROWSELECT = 0x00000020;
    internal const uint LVS_EX_HEADERDRAGDROP = 0x00000010;
    internal const uint LVS_EX_LABELTIP = 0x00004000;
    internal const uint LVS_EX_DOUBLEBUFFER = 0x00010000;

    // --- メッセージ ---
    private const uint LVM_FIRST = 0x1000;
    internal const uint LVM_SETIMAGELIST = LVM_FIRST + 3;
    internal const uint LVM_GETITEMCOUNT = LVM_FIRST + 4;
    internal const uint LVM_GETNEXTITEM = LVM_FIRST + 12;
    internal const uint LVM_ENSUREVISIBLE = LVM_FIRST + 19;
    internal const uint LVM_REDRAWITEMS = LVM_FIRST + 21;
    internal const uint LVM_SETCOLUMNWIDTH = LVM_FIRST + 30;
    internal const uint LVM_GETHEADER = LVM_FIRST + 31;
    internal const uint LVM_SETITEMSTATE = LVM_FIRST + 43;
    internal const uint LVM_GETITEMSTATE = LVM_FIRST + 44;
    internal const uint LVM_SETITEMCOUNT = LVM_FIRST + 47;
    internal const uint LVM_SETEXTENDEDLISTVIEWSTYLE = LVM_FIRST + 54;
    internal const uint LVM_INSERTCOLUMNW = LVM_FIRST + 97;
    internal const uint LVM_GETEDITCONTROL = LVM_FIRST + 24;
    internal const uint LVM_EDITLABELW = LVM_FIRST + 118;

    internal const uint LVSIL_SMALL = 1;

    /// <summary><c>LVM_SETITEMCOUNT</c> の flag。オーナーデータで行数だけが変わったとき、
    /// 表示位置とスクロール量を保ったまま更新する。</summary>
    internal const nint LVSICF_NOINVALIDATEALL = 0x00000001;
    internal const nint LVSICF_NOSCROLL = 0x00000002;

    // --- 通知 ---
    private const int LVN_FIRST = -100;
    internal const int LVN_ITEMCHANGED = LVN_FIRST - 1;
    internal const int LVN_COLUMNCLICK = LVN_FIRST - 8;
    internal const int LVN_ITEMACTIVATE = LVN_FIRST - 14;
    internal const int LVN_ODCACHEHINT = LVN_FIRST - 13;
    internal const int LVN_KEYDOWN = LVN_FIRST - 55;
    internal const int LVN_GETDISPINFOW = LVN_FIRST - 77;
    internal const int LVN_ODFINDITEMW = LVN_FIRST - 79;
    internal const int LVN_BEGINLABELEDITW = LVN_FIRST - 75;
    internal const int LVN_ENDLABELEDITW = LVN_FIRST - 76;

    // --- LVITEM ---
    internal const uint LVIF_TEXT = 0x0001;
    internal const uint LVIF_IMAGE = 0x0002;
    internal const uint LVIF_STATE = 0x0008;

    internal const uint LVNI_FOCUSED = 0x0001;

    internal const uint LVIS_FOCUSED = 0x0001;
    internal const uint LVIS_SELECTED = 0x0002;

    internal const nint LVNI_ALL = 0x0000;
    internal const nint LVNI_SELECTED = 0x0002;

    // --- 列 ---
    internal const uint LVCF_FMT = 0x0001;
    internal const uint LVCF_WIDTH = 0x0002;
    internal const uint LVCF_TEXT = 0x0004;
    internal const uint LVCF_SUBITEM = 0x0008;

    internal const int LVCFMT_LEFT = 0x0000;
    internal const int LVCFMT_RIGHT = 0x0001;

    // --- 検索（頭文字キー入力） ---
    internal const uint LVFI_STRING = 0x0002;
    internal const uint LVFI_PARTIAL = 0x0008;

    // --- ヘッダ（ソート矢印） ---
    private const uint HDM_FIRST = 0x1200;
    internal const uint HDM_GETITEMW = HDM_FIRST + 11;
    internal const uint HDM_SETITEMW = HDM_FIRST + 12;

    internal const uint HDI_FORMAT = 0x0004;
    internal const int HDF_SORTDOWN = 0x0200;
    internal const int HDF_SORTUP = 0x0400;

    [StructLayout(LayoutKind.Sequential)]
    internal struct NMHDR
    {
        public nint hwndFrom;
        public nint idFrom;
        public int code;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LVITEMW
    {
        public uint mask;
        public int iItem;
        public int iSubItem;
        public uint state;
        public uint stateMask;
        /// <summary>受け取り側（<c>LVN_GETDISPINFO</c>）では、コントロールが用意した
        /// <c>cchTextMax</c> 文字分のバッファへのポインタ。ここへ書き込む。</summary>
        public nint pszText;
        public int cchTextMax;
        public int iImage;
        public nint lParam;
        public int iIndent;
        public int iGroupId;
        public uint cColumns;
        public nint puColumns;
        public nint piColFmt;
        public int iGroup;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NMLVDISPINFOW
    {
        public NMHDR hdr;
        public LVITEMW item;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NMLISTVIEW
    {
        public NMHDR hdr;
        public int iItem;
        public int iSubItem;
        public uint uNewState;
        public uint uOldState;
        public uint uChanged;
        public int ptActionX;
        public int ptActionY;
        public nint lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NMLVKEYDOWN
    {
        public NMHDR hdr;
        public ushort wVKey;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LVFINDINFOW
    {
        public uint flags;
        public nint psz;
        public nint lParam;
        public int ptX;
        public int ptY;
        public uint vkDirection;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NMLVFINDITEMW
    {
        public NMHDR hdr;
        public int iStart;
        public LVFINDINFOW lvfi;
    }

    // --- 空表示（エラー・項目なしのときに一覧領域の中央へ出す文字列） ---
    internal const int LVN_GETEMPTYMARKUP = LVN_FIRST - 87;
    internal const uint EMF_CENTERED = 0x00000001;
    internal const int L_MAX_URL_LENGTH = 2084;

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct NMLVEMPTYMARKUP
    {
        public NMHDR hdr;
        public uint dwFlags;
        public fixed char szMarkup[L_MAX_URL_LENGTH];
    }

    // --- カスタム描画（隠し・システム属性の行を薄色にするために使う） ---
    internal const int NM_CUSTOMDRAW = -12;
    internal const int NM_SETFOCUS = -7;

    internal const uint CDDS_PREPAINT = 0x00000001;
    internal const uint CDDS_ITEM = 0x00010000;
    internal const uint CDDS_ITEMPREPAINT = CDDS_ITEM | CDDS_PREPAINT;
    internal const uint CDDS_SUBITEM = 0x00020000;

    internal const nint CDRF_DODEFAULT = 0x00000000;
    internal const nint CDRF_NEWFONT = 0x00000002;
    internal const nint CDRF_NOTIFYITEMDRAW = 0x00000020;

    /// <summary><c>CDRF_NOTIFYITEMDRAW</c> と同じ値。行の描画中に返すと列単位の通知になる。</summary>
    internal const nint CDRF_NOTIFYSUBITEMDRAW = 0x00000020;

    internal const uint CDIS_SELECTED = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    internal struct NMCUSTOMDRAW
    {
        public NMHDR hdr;
        public uint dwDrawStage;
        public nint hdc;
        public RECT rc;
        public nint dwItemSpec;
        public uint uItemState;
        public nint lItemlParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NMLVCUSTOMDRAW
    {
        public NMCUSTOMDRAW nmcd;
        public uint clrText;
        public uint clrTextBk;
        public int iSubItem;
        public uint dwItemType;
        public uint clrFace;
        public int iIconEffect;
        public int iIconPhase;
        public int iPartId;
        public int iStateId;
        public RECT rcText;
        public uint uAlign;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LVCOLUMNW
    {
        public uint mask;
        public int fmt;
        public int cx;
        public nint pszText;
        public int cchTextMax;
        public int iSubItem;
        public int iImage;
        public int iOrder;
        public int cxMin;
        public int cxDefault;
        public int cxIdeal;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HDITEMW
    {
        public uint mask;
        public int cxy;
        public nint pszText;
        public nint hbm;
        public int cchTextMax;
        public int fmt;
        public nint lParam;
        public int iImage;
        public int iOrder;
        public uint type;
        public nint pvFilter;
        public uint state;
    }
}
