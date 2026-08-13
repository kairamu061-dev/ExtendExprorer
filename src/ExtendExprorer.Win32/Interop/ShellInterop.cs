using System.Runtime.InteropServices;

namespace ExtendExprorer.Interop;

/// <summary>シェル（shell32 / shlwapi）の宣言。
/// <para>WinUI 版から引き継ぐが、<b>アイコンの扱いだけは変える</b>。旧版は
/// <c>SHGFI_ICON</c> で <c>HICON</c> を取り出して 1 枚ずつ <c>WriteableBitmap</c> に変換していたが、
/// こちらは <c>SHGFI_SYSICONINDEX</c> で <b>OS のシステムイメージリストの番号</b>だけを受け取る。
/// 画像の実体は OS 側に置いたままなので、項目ごとに持つのは <c>int</c> ひとつで済む
/// （メモリ要件 30MB を満たすための中心的な判断・<c>docs/win32-migration/design.md</c>）。</para></summary>
internal static partial class NativeMethods
{
    internal const uint SHGFI_SMALLICON = 0x000000001;
    internal const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    internal const uint SHGFI_SYSICONINDEX = 0x000004000;
    internal const uint SHGFI_TYPENAME = 0x000000400;

    internal const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
    internal const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    /// <summary>SHFILEINFOW (shellapi.h)。文字列は <c>fixed</c> の固定長にして blittable に保つ
    /// （<c>string</c> にすると管理型になり <c>sizeof</c> がネイティブの大きさを返さない）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct SHFILEINFOW
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;
        public fixed char szDisplayName[260];
        public fixed char szTypeName[80];
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHGetFileInfoW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint SHGetFileInfoW(string path, uint fileAttributes,
        ref SHFILEINFOW info, uint size, uint flags);

    /// <summary>エクスプローラーと同じ自然順の文字列比較（数字を数値として扱う）。</summary>
    [LibraryImport("shlwapi.dll", EntryPoint = "StrCmpLogicalW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int StrCmpLogicalW(string psz1, string psz2);
}
