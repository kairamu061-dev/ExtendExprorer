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

    // --- フォルダが読めるかの確認 ---
    //
    // .NET の列挙は、対象フォルダ自身が読めないとき「例外」ではなく「0 件」を返してくる
    // （`EnumerationOptions.IgnoreInaccessible` は下位の項目にしか効かない）。
    // そのため「アクセス拒否」と「空フォルダ」を区別できない(BUG-020)。
    // 列挙が 0 件だったときだけ、Win32 の戻り値で直接確かめる。

    private const int ERROR_ACCESS_DENIED = 5;
    private static readonly nint InvalidHandleValue = -1;

    /// <summary>WIN32_FIND_DATAW。<c>FILETIME</c> は DWORD 2 つ（4 バイト境界）なので、
    /// <c>long</c> で置くと以降の項目の位置がずれる。ここは native と同じ並びにしておく。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct WIN32_FIND_DATAW
    {
        public uint dwFileAttributes;
        public uint ftCreationTimeLow;
        public uint ftCreationTimeHigh;
        public uint ftLastAccessTimeLow;
        public uint ftLastAccessTimeHigh;
        public uint ftLastWriteTimeLow;
        public uint ftLastWriteTimeHigh;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;
        public uint dwReserved1;
        public fixed char cFileName[260];
        public fixed char cAlternateFileName[14];
    }

    [LibraryImport("kernel32.dll", EntryPoint = "FindFirstFileW",
        StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint FindFirstFileW(string fileName, out WIN32_FIND_DATAW data);

    [LibraryImport("kernel32.dll", EntryPoint = "FindClose", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FindClose(nint handle);

    private const uint FILE_LIST_DIRECTORY = 0x0001;
    private const uint FILE_SHARE_ALL = 0x0007;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

    /// <summary>フォルダを「中身を列挙する権限」で開く。<c>FindFirstFileW</c> とは別経路で
    /// 同じことを確かめられるので、片方の宣言や呼び方を間違えていても取りこぼさない。</summary>
    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW",
        StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint CreateFileW(string fileName, uint access, uint share,
        nint security, uint creation, uint flags, nint template);

    [LibraryImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    /// <summary>そのフォルダの列挙が拒否されるか。読めるとき・存在しないときは false。
    ///
    /// <para><b>2 通りで確かめる</b>。実機で 2 度、直したはずのものが直っていなかったため
    /// （BUG-020）、どちらか一方でも「拒否」と言えば拒否として扱う。
    /// <c>--diag</c> 付きで起動すると、両方の生の結果を <c>diag.log</c> に書き出す。</para></summary>
    internal static bool IsEnumerationDenied(string path)
    {
        var byFind = ProbeFindFirstFile(path);
        var byOpen = ProbeOpenDirectory(path);
        return byFind || byOpen;
    }

    private static bool ProbeFindFirstFile(string path)
    {
        try
        {
            // 一覧の列挙と同じ形（末尾に \*）。フォルダ自身を指すと親から見えてしまい成功する
            var pattern = System.IO.Path.Combine(path, "*");
            var handle = FindFirstFileW(pattern, out _);
            var error = Marshal.GetLastPInvokeError();
            var denied = handle == InvalidHandleValue && error == ERROR_ACCESS_DENIED;
            UI.Diagnostics.Write($"  FindFirstFileW(\"{pattern}\") handle=0x{handle:X} gle={error} denied={denied}");
            if (handle != InvalidHandleValue)
            {
                FindClose(handle);
            }
            return denied;
        }
        catch (Exception ex)
        {
            UI.Diagnostics.Write($"  FindFirstFileW 例外: {ex.GetType().Name} {ex.Message}");
            return false;
        }
    }

    private static bool ProbeOpenDirectory(string path)
    {
        try
        {
            var handle = CreateFileW(path, FILE_LIST_DIRECTORY, FILE_SHARE_ALL, 0,
                OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, 0);
            var error = Marshal.GetLastPInvokeError();
            var denied = handle == InvalidHandleValue && error == ERROR_ACCESS_DENIED;
            UI.Diagnostics.Write($"  CreateFileW(\"{path}\", FILE_LIST_DIRECTORY) handle=0x{handle:X} gle={error} denied={denied}");
            if (handle != InvalidHandleValue)
            {
                CloseHandle(handle);
            }
            return denied;
        }
        catch (Exception ex)
        {
            UI.Diagnostics.Write($"  CreateFileW 例外: {ex.GetType().Name} {ex.Message}");
            return false;
        }
    }

    /// <summary>拡張子の関連付けに従って開く（file-list 仕様 3b）。</summary>
    [LibraryImport("shell32.dll", EntryPoint = "ShellExecuteW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint ShellExecuteW(nint hwnd, string? verb, string file,
        string? parameters, string? directory, int showCmd);

    internal const int SW_SHOWNORMAL = 1;
}
