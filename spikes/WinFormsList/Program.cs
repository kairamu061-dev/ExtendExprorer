using System.Runtime.InteropServices;

namespace WinFormsList;

/// <summary>メモリ計測用のスパイク: C# / WinForms（Native AOT 非対応なので JIT）。
/// Win32Aot スパイクと<b>同じものを表示</b>して差だけを見る。WinForms も内部は Win32
/// コントロールなので、素の Win32 との差＝WinForms 層と JIT ランタイムのコストになる。</summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var folder = args.Length > 0
            ? args[0]
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Application.Run(new ListForm(folder));
    }
}

internal sealed class ListForm : Form
{
    private readonly (string Name, string Modified, string Size, int Icon)[] _rows;

    public ListForm(string folder)
    {
        Text = $"WinForms spike - {folder}";
        ClientSize = new Size(1000, 700);

        var list = new ShellListView(folder)
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            VirtualMode = true,
            FullRowSelect = true,
        };
        list.Columns.Add("名前", 320);
        list.Columns.Add("更新日時", 140);
        list.Columns.Add("サイズ", 100);

        _rows = Enumerate(folder);
        list.VirtualListSize = _rows.Length;
        list.RetrieveVirtualItem += (_, e) =>
        {
            var row = _rows[e.ItemIndex];
            e.Item = new ListViewItem([row.Name, row.Modified, row.Size]) { ImageIndex = row.Icon };
        };
        Controls.Add(list);
    }

    private static (string, string, string, int)[] Enumerate(string folder)
    {
        List<(string, string, string, int)> rows = [];
        try
        {
            foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos())
            {
                var isDirectory = (entry.Attributes & FileAttributes.Directory) != 0;
                long length = 0;
                if (!isDirectory && entry is FileInfo file)
                {
                    try { length = file.Length; } catch { }
                }
                rows.Add((
                    entry.Name,
                    entry.LastWriteTime.ToString("yyyy/MM/dd HH:mm"),
                    isDirectory ? "" : $"{(length + 1023) / 1024:N0} KB",
                    Shell.IconIndexOf(entry.FullName, isDirectory)));
            }
        }
        catch
        {
            // 読めないフォルダは空（計測用）
        }
        return [.. rows];
    }
}

/// <summary>OS のシステムイメージリストをそのまま使う ListView。
/// <c>ImageList</c> にアイコンを積むとアプリ側のメモリに載るので、載せずに借りる。</summary>
internal sealed class ShellListView(string folder) : ListView
{
    private const int LVM_FIRST = 0x1000;
    private const int LVM_SETIMAGELIST = LVM_FIRST + 3;
    private const int LVSIL_SMALL = 1;
    private const int GWL_STYLE = -16;
    private const int LVS_SHAREIMAGELISTS = 0x0040;

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint hwnd, int msg, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // 破棄時に OS のイメージリストを道連れにされないようにする
        SetWindowLongPtr(Handle, GWL_STYLE, GetWindowLongPtr(Handle, GWL_STYLE) | LVS_SHAREIMAGELISTS);
        var himl = Shell.SmallImageList(folder);
        if (himl != 0)
        {
            SendMessage(Handle, LVM_SETIMAGELIST, LVSIL_SMALL, himl);
        }
    }
}

internal static class Shell
{
    private const uint SHGFI_SMALLICON = 0x1;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint SHGFI_SYSICONINDEX = 0x4000;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, int size, uint flags);

    public static int IconIndexOf(string path, bool isDirectory)
    {
        var info = default(SHFILEINFO);
        var attributes = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
        var result = SHGetFileInfo(path, attributes, ref info, Marshal.SizeOf<SHFILEINFO>(),
            SHGFI_SYSICONINDEX | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);
        return result == 0 ? 0 : info.iIcon;
    }

    public static nint SmallImageList(string folder)
    {
        var info = default(SHFILEINFO);
        return SHGetFileInfo(folder, 0, ref info, Marshal.SizeOf<SHFILEINFO>(),
            SHGFI_SYSICONINDEX | SHGFI_SMALLICON);
    }
}
