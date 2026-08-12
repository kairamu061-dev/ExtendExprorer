using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Win32Aot.Native;

namespace Win32Aot;

/// <summary>メモリ計測用のスパイク: C# / Native AOT ＋ 素の Win32（XAML なし）。
///
/// <para>狙いは「WinUI 3 をやめれば Tablacus 並み（約 30MB）に届くのか、届くなら C# のままで
/// 済むのか」を実測で決めること。比較のため、本体で重いところ ——
/// <b>フォルダの列挙・シェルアイコン・仮想リストの詳細表示</b> —— は同じだけ動かす。</para>
///
/// <para>本体との決定的な違いはアイコンの持ち方。本体は 1 枚ずつ
/// <c>WriteableBitmap</c> に変換して抱えるが、ここでは OS のシステムイメージリストを
/// 共有し、項目ごとに<b>インデックス（int）だけ</b>を持つ（Tablacus 方式）。</para>
///
/// <para>引数にフォルダを渡すとそこを開く（省略時はホーム）。ナビゲーションは実装しない
/// ——「起動直後」と「大きいフォルダ表示時」を測れれば足りるため。</para></summary>
internal static unsafe class Program
{
    private sealed class Row
    {
        public required string Name;
        public required string Size;
        public required string Modified;
        public int IconIndex;
    }

    private static Row[] _rows = [];
    private static nint _list;

    // ListView へ返す文字列の一時バッファ。呼び出しごとに使い回す（GC を増やさない）
    private static readonly char[] TextBuffer = new char[520];

    [STAThread]
    private static int Main(string[] args)
    {
        var folder = args.Length > 0
            ? args[0]
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var icc = new INITCOMMONCONTROLSEX
        {
            dwSize = (uint)sizeof(INITCOMMONCONTROLSEX),
            dwICC = ICC_LISTVIEW_CLASSES,
        };
        InitCommonControlsEx(ref icc);

        var instance = GetModuleHandleW(0);
        var className = "Win32AotSpike";
        fixed (char* pClass = className)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&WndProc,
                hInstance = instance,
                hCursor = LoadCursorW(0, 32512), // IDC_ARROW
                hbrBackground = 6,               // COLOR_WINDOW + 1
                lpszClassName = (nint)pClass,
            };
            if (RegisterClassExW(ref wc) == 0)
            {
                return 1;
            }
        }

        var hwnd = CreateWindowExW(0, className, $"Win32 AOT spike - {folder}",
            WS_OVERLAPPEDWINDOW, CW_USEDEFAULT, CW_USEDEFAULT, 1000, 700, 0, 0, instance, 0);
        if (hwnd == 0)
        {
            return 1;
        }

        _list = CreateWindowExW(0, "SysListView32", null,
            WS_CHILD | WS_VISIBLE | LVS_REPORT | LVS_OWNERDATA | LVS_SHAREIMAGELISTS,
            0, 0, 0, 0, hwnd, 0, instance, 0);
        SendMessageW(_list, LVM_SETEXTENDEDLISTVIEWSTYLE, 0, LVS_EX_FULLROWSELECT | LVS_EX_DOUBLEBUFFER);

        // OS のシステムイメージリストをそのまま借りる（アイコンの実体はアプリ側に持たない）
        var info = default(SHFILEINFOW);
        var imageList = SHGetFileInfoW(folder, 0, ref info, (uint)sizeof(SHFILEINFOW),
            SHGFI_SYSICONINDEX | SHGFI_SMALLICON);
        if (imageList != 0)
        {
            SendMessageW(_list, LVM_SETIMAGELIST, LVSIL_SMALL, imageList);
        }

        AddColumn(0, "名前", 320);
        AddColumn(1, "更新日時", 140);
        AddColumn(2, "サイズ", 100);

        _rows = Enumerate(folder);
        SendMessageW(_list, LVM_SETITEMCOUNT, _rows.Length, 0);

        ShowWindow(hwnd, 1);
        while (GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            TranslateMessage(in msg);
            DispatchMessageW(in msg);
        }
        return 0;
    }

    private static void AddColumn(int index, string text, int width)
    {
        fixed (char* pText = text)
        {
            var column = new LVCOLUMNW
            {
                mask = LVCF_FMT | LVCF_WIDTH | LVCF_TEXT | LVCF_SUBITEM,
                cx = width,
                pszText = (nint)pText,
                iSubItem = index,
            };
            SendMessageW(_list, LVM_INSERTCOLUMNW, index, (nint)(&column));
        }
    }

    /// <summary>フォルダを列挙する。1 行につき持つのは表示用の文字列とアイコンの<b>インデックス</b>だけ。</summary>
    private static Row[] Enumerate(string folder)
    {
        List<Row> rows = [];
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
                rows.Add(new Row
                {
                    Name = entry.Name,
                    Modified = entry.LastWriteTime.ToString("yyyy/MM/dd HH:mm"),
                    Size = isDirectory ? "" : $"{(length + 1023) / 1024:N0} KB",
                    IconIndex = IconIndexOf(entry.FullName, isDirectory),
                });
            }
        }
        catch
        {
            // 読めないフォルダは空で表示（計測用なのでエラー表示は作らない）
        }
        return [.. rows];
    }

    /// <summary>システムイメージリスト上のアイコン番号。ディスクに触れない属性ベースで引く。</summary>
    private static int IconIndexOf(string path, bool isDirectory)
    {
        var info = default(SHFILEINFOW);
        var attributes = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
        var result = SHGetFileInfoW(path, attributes, ref info, (uint)sizeof(SHFILEINFOW),
            SHGFI_SYSICONINDEX | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);
        return result == 0 ? 0 : info.iIcon;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_SIZE:
                MoveWindow(_list, 0, 0, (int)(lParam & 0xFFFF), (int)((lParam >> 16) & 0xFFFF), true);
                return 0;
            case WM_NOTIFY:
                var header = (NMHDR*)lParam;
                if (header->code == LVN_GETDISPINFOW)
                {
                    FillRow((NMLVDISPINFOW*)lParam);
                }
                return 0;
            case WM_DESTROY:
                PostQuitMessage(0);
                return 0;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    /// <summary>仮想リストの表示要求。見えている行のぶんしか呼ばれない。</summary>
    private static void FillRow(NMLVDISPINFOW* info)
    {
        var index = info->item.iItem;
        if ((uint)index >= (uint)_rows.Length)
        {
            return;
        }
        var row = _rows[index];
        if ((info->item.mask & LVIF_TEXT) != 0 && info->item.pszText != 0)
        {
            var text = info->item.iSubItem switch
            {
                1 => row.Modified,
                2 => row.Size,
                _ => row.Name,
            };
            var max = Math.Min(text.Length, info->item.cchTextMax - 1);
            text.AsSpan(0, max).CopyTo(TextBuffer);
            TextBuffer[max] = '\0';
            fixed (char* pText = TextBuffer)
            {
                Buffer.MemoryCopy(pText, (void*)info->item.pszText, (max + 1) * 2, (max + 1) * 2);
            }
        }
        if ((info->item.mask & LVIF_IMAGE) != 0)
        {
            info->item.iImage = row.IconIndex;
        }
    }
}
