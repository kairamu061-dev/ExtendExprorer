using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace ExtendExprorer.Interop;

/// <summary>シェルの名前空間（PIDL）を歩くための宣言。フォルダツリーの根を
/// エクスプローラーのナビゲーションウィンドウと同じ顔ぶれにするために足した（2026-09-13）。
///
/// <para><b>PIDL は COM のタスクアロケータの記憶。</b>取ったら <see cref="NativeMethods.CoTaskMemFree"/>
/// で返す。ツリーはノード 1 つにつき絶対 PIDL を 1 つ持つので、
/// <b>ノードを捨てる経路すべてで解放する</b>（`Ctrl+Shift+G` はマネージドしか数えないので
/// ここの漏れは捕まらない。診断の `[tree] PIDL 保持/解放` で見る）。</para></summary>
[GeneratedComInterface]
[Guid("000214F2-0000-0000-C000-000000000046")]
internal partial interface IEnumIDList
{
    /// <summary>celt 個まで取り出す。<paramref name="rgelt"/> は PIDL の配列の先頭。</summary>
    [PreserveSig] int Next(uint celt, nint rgelt, out uint pceltFetched);
    [PreserveSig] int Skip(uint celt);
    [PreserveSig] int Reset();
    [PreserveSig] int Clone(out nint ppenum);
}

internal static partial class NativeMethods
{
    internal static readonly Guid IID_IEnumIDList = new("000214F2-0000-0000-C000-000000000046");

    // --- 列挙の旗（SHCONTF） ---

    internal const uint SHCONTF_FOLDERS = 0x0020;

    /// <summary><b>ナビゲーションウィンドウと同じ顔ぶれで列挙する。</b>
    /// これを付けないと素のデスクトップの子になり、エクスプローラーの左ペインとずれる。
    /// Windows 7 以降。<b>実際に同じ集合が返るかは実機でしか確かめられない</b>ので、
    /// 初回の確認で根の顔ぶれをそのまま出させる。</summary>
    internal const uint SHCONTF_NAVIGATION_PANE = 0x1000;

    /// <summary>隠し属性の項目も出す。<b>付けないと落ちる。</b>
    /// 一覧（ファイルシステムを直接読む側）は隠しも出して薄色にしているので、
    /// 付けないと<b>同じアプリの中でツリーと一覧が食い違う</b>（2026-09-14 実測）。</summary>
    internal const uint SHCONTF_INCLUDEHIDDEN = 0x0080;

    /// <summary>システム属性（「保護されたオペレーティング システム ファイル」）も出す。
    /// <c>System Volume Information</c> などがこれ。一覧は出しているので合わせる。</summary>
    internal const uint SHCONTF_INCLUDESUPERHIDDEN = 0x10000;

    // --- 項目の属性（SFGAO） ---

    /// <summary>展開できる子がいるか。シェブロンの有無をこれで決める
    /// （今までは常に出しておいて、開いて 0 件だったら消していた）。</summary>
    internal const uint SFGAO_HASSUBFOLDER = 0x80000000;

    /// <summary>ファイルシステム上の実体があるか。<b>これだけでは移動先にできない</b>
    /// （コントロールパネルの一部のように、パスは取れても開けないものがある）。
    /// <c>Directory.Exists</c> と両方見る。</summary>
    internal const uint SFGAO_FILESYSTEM = 0x40000000;

    internal const uint SFGAO_HIDDEN = 0x00080000;

    /// <summary>1 つ目の引数をパス文字列ではなく PIDL として読む。</summary>
    internal const uint SHGFI_PIDL = 0x000000008;

    /// <summary>表示名（フォルダの中での名前）。</summary>
    internal const uint SIGDN_NORMALDISPLAY = 0x00000000;

    /// <summary>デスクトップから見た解析名（<c>::{CLSID}</c> 形式）。
    /// <b>言語に依らない見分け方</b>が要るときに使う。</summary>
    internal const uint SIGDN_DESKTOPABSOLUTEPARSING = 0x80028000;

    /// <summary>ファイルシステム上のパス。<b>持たない項目では失敗する</b>ので、
    /// 「パスがあるか」の判定そのものに使える。</summary>
    internal const uint SIGDN_FILESYSPATH = 0x80058000;

    [LibraryImport("shell32.dll", EntryPoint = "SHGetDesktopFolder")]
    internal static partial int SHGetDesktopFolder(out nint ppshf);

    /// <summary>PIDL から名前を取る。<c>IShellFolder.GetDisplayNameOf</c> と違って
    /// <c>STRRET</c> を自分でほどかなくてよい（Vista 以降）。
    /// 返った文字列は <see cref="CoTaskMemFree"/> で返す。</summary>
    [LibraryImport("shell32.dll", EntryPoint = "SHGetNameFromIDList")]
    internal static partial int SHGetNameFromIDList(nint pidl, uint sigdnName, out nint ppszName);

    /// <summary>親の絶対 PIDL と子の相対 PIDL をつないで、子の絶対 PIDL を作る。
    /// 返りは新しい記憶なので解放が要る。</summary>
    [LibraryImport("shell32.dll", EntryPoint = "ILCombine")]
    internal static partial nint ILCombine(nint pidl1, nint pidl2);

    /// <summary>PIDL の複製。<b>スレッドへ渡すときに要る。</b>借りたポインタを
    /// そのまま渡すと、持ち主が解放したあとに使うことになりうる。</summary>
    [LibraryImport("shell32.dll", EntryPoint = "ILClone")]
    internal static partial nint ILClone(nint pidl);

    /// <summary>PIDL を指定した <c>IShellFolder</c> 等に結び直す。
    /// <b>スレッドをまたぐときは、渡ってきた PIDL からその場で結び直す</b>
    /// （インターフェイスのポインタをまたがせない）。</summary>
    [LibraryImport("shell32.dll", EntryPoint = "SHBindToObject")]
    internal static partial int SHBindToObject(nint psf, nint pidl, nint pbc, in Guid riid, out nint ppv);

    /// <summary>「ダウンロード」等の場所を OS に聞く。<b>移せるので決め打ちにしない。</b>
    /// 返った文字列は <see cref="CoTaskMemFree"/> で返す。</summary>
    [LibraryImport("shell32.dll", EntryPoint = "SHGetKnownFolderPath")]
    internal static partial int SHGetKnownFolderPath(in Guid id, uint flags, nint token, out nint path);

    [LibraryImport("ole32.dll", EntryPoint = "CoTaskMemFree")]
    internal static partial void CoTaskMemFree(nint pv);

    /// <summary>PIDL を渡す版。<c>SHGFI_PIDL</c> と組で使う。</summary>
    [LibraryImport("shell32.dll", EntryPoint = "SHGetFileInfoW")]
    internal static partial nint SHGetFileInfoPidl(nint pidl, uint fileAttributes,
        ref SHFILEINFOW info, uint size, uint flags);

    // --- スレッドの用意 ---
    //
    // 列挙は専用スレッドで回す（`ネットワーク` はブロックしうる）。
    // **MTA にする。**ポンプの無い STA はデッドロックの温床で、ポンプを書くと
    // それ自体が検証の対象になる。シェルのフォルダは要るときに自分で marshaling する。

    internal const uint COINIT_MULTITHREADED = 0x0;

    [LibraryImport("ole32.dll", EntryPoint = "CoInitializeEx")]
    internal static partial int CoInitializeEx(nint reserved, uint coInit);

    [LibraryImport("ole32.dll", EntryPoint = "CoUninitialize")]
    internal static partial void CoUninitialize();
}
