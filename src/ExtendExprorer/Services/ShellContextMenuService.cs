using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using ExtendExprorer.Interop;

namespace ExtendExprorer.Services;

/// <summary>Windows シェルのコンテキストメニューを表示・実行する。
/// UI(STA) スレッド専用。シェル API の失敗はすべて握りつぶし、メニューを出さないだけに留める
/// （サードパーティのシェル拡張がプロセス内で動くため、クラッシュさせないことを最優先）。</summary>
internal static unsafe class ShellContextMenuService
{
    private static readonly StrategyBasedComWrappers ComWrappers = new();

    private static readonly Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid IID_IContextMenu = new("000214E4-0000-0000-C000-000000000046");

    // シェルへ渡す ID 範囲(idFirst..0x7FFF)の外に自前項目を置く
    private const uint ShellIdFirst = 1;
    private const uint ShellIdLast = 0x7FFF;
    private const uint PasteCommandId = 0x8001;

    // TrackPopupMenuEx のモーダルポンプ中のみ非 null（UI スレッド 1 本・同時表示 1 個の前提）
    private static IContextMenu2? _menu2;
    private static IContextMenu3? _menu3;

    /// <summary>ファイルを既定アプリで開く（エクスプローラーのダブルクリックと同じ PIDL 経路。BUG-004）。</summary>
    public static void OpenWithDefault(nint hwnd, string path)
    {
        nint pidl = 0;
        try
        {
            if (NativeMethods.SHParseDisplayName(path, 0, out pidl, 0, out _) < 0 || pidl == 0)
            {
                return;
            }
            var info = new ShellExecuteInfoW
            {
                cbSize = sizeof(ShellExecuteInfoW),
                fMask = NativeMethods.SEE_MASK_INVOKEIDLIST, // verb 省略 = 既定 verb
                hwnd = hwnd,
                lpIDList = pidl,
                nShow = NativeMethods.SW_SHOWNORMAL,
            };
            NativeMethods.ShellExecuteExW(ref info);
        }
        catch
        {
            // 起動失敗時のダイアログはシェルに委ねる。アプリは落とさない
        }
        finally
        {
            if (pidl != 0)
            {
                Marshal.FreeCoTaskMem(pidl);
            }
        }
    }

    /// <summary>選択中の項目（1 件以上・同一フォルダ内）に対するメニュー。カーソル位置に表示する。</summary>
    public static void ShowForItems(nint hwnd, string folderPath, IReadOnlyList<string> itemNames)
    {
        if (itemNames.Count == 0)
        {
            return;
        }
        var fullPidls = new List<nint>();
        try
        {
            foreach (var name in itemNames)
            {
                if (NativeMethods.SHParseDisplayName(Path.Combine(folderPath, name), 0, out var pidl, 0, out _) >= 0 &&
                    pidl != 0)
                {
                    fullPidls.Add(pidl);
                }
            }
            if (fullPidls.Count == 0)
            {
                return; // 表示直前に全て削除された等: 何もしない(spec のエラーケース)
            }

            // 全項目は同一フォルダ内なので、親フォルダは先頭項目から 1 つ取れば足りる。
            // 各子 PIDL は対応する絶対 PIDL 内を指すため個別解放は不要
            IShellFolder? parent = null;
            var children = new nint[fullPidls.Count];
            for (var i = 0; i < fullPidls.Count; i++)
            {
                if (NativeMethods.SHBindToParent(fullPidls[i], in IID_IShellFolder, out var parentPtr, out children[i]) < 0)
                {
                    return;
                }
                if (parent is null)
                {
                    try
                    {
                        parent = (IShellFolder)ComWrappers.GetOrCreateObjectForComInstance(parentPtr, CreateObjectFlags.None);
                    }
                    finally
                    {
                        Marshal.Release(parentPtr);
                    }
                }
                else
                {
                    Marshal.Release(parentPtr);
                }
            }

            nint menuPtr;
            int hr;
            fixed (nint* pChildren = children)
            {
                hr = parent!.GetUIObjectOf(hwnd, (uint)children.Length, (nint)pChildren, in IID_IContextMenu, 0, out menuPtr);
            }
            if (hr < 0 || menuPtr == 0)
            {
                return;
            }
            IContextMenu menu;
            try
            {
                menu = (IContextMenu)ComWrappers.GetOrCreateObjectForComInstance(menuPtr, CreateObjectFlags.None);
            }
            finally
            {
                Marshal.Release(menuPtr);
            }
            TrackAndInvoke(hwnd, menu, background: false, folderPath);
        }
        catch
        {
            // シェル拡張由来の失敗を含め、メニュー機能の失敗でアプリを落とさない
        }
        finally
        {
            foreach (var pidl in fullPidls)
            {
                Marshal.FreeCoTaskMem(pidl);
            }
        }
    }

    /// <summary>一覧の空白部分＝表示中フォルダの背景メニュー（新規作成・貼り付け等）。</summary>
    public static void ShowForBackground(nint hwnd, string folderPath)
    {
        nint pidl = 0;
        try
        {
            if (NativeMethods.SHParseDisplayName(folderPath, 0, out pidl, 0, out _) < 0 || pidl == 0)
            {
                return;
            }
            if (BindToParent(pidl, out var childPidl) is not { } parent)
            {
                return;
            }
            var menu = CreateBackgroundMenu(hwnd, parent, childPidl);
            if (menu is null)
            {
                return;
            }
            TrackAndInvoke(hwnd, menu, background: true, folderPath);
        }
        catch
        {
        }
        finally
        {
            if (pidl != 0)
            {
                Marshal.FreeCoTaskMem(pidl);
            }
        }
    }

    /// <summary>HMENU 構築 → 表示（モーダル）→ 選択コマンド実行。項目/背景メニュー共通の後半部。</summary>
    private static void TrackAndInvoke(nint hwnd, IContextMenu menu, bool background, string folderPath)
    {
        nint hMenu = 0;
        try
        {
            hMenu = NativeMethods.CreatePopupMenu();
            if (hMenu == 0)
            {
                return;
            }
            if (menu.QueryContextMenu(hMenu, 0, ShellIdFirst, ShellIdLast, 0) < 0)
            {
                return;
            }
            if (background)
            {
                // 「貼り付け」はシェルの背景メニューではなく DefView が出す項目のため自前で追加する(BUG-003)
                var canPaste = NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_HDROP);
                NativeMethods.InsertMenuW(hMenu, 0, NativeMethods.MF_BYPOSITION | NativeMethods.MF_SEPARATOR, 0, null);
                NativeMethods.InsertMenuW(hMenu, 0,
                    NativeMethods.MF_BYPOSITION | NativeMethods.MF_STRING | (canPaste ? 0 : NativeMethods.MF_GRAYED),
                    PasteCommandId, "貼り付け(&P)");
            }

            NativeMethods.GetCursorPos(out var pt);

            // 「送る」「プログラムから開く」等の遅延生成サブメニューは、メニュー所有ウィンドウへの
            // WM_INITMENUPOPUP 等を IContextMenu2/3 に転送しないと空になる
            _menu2 = menu as IContextMenu2;
            _menu3 = menu as IContextMenu3;
            var subclassed = NativeMethods.SetWindowSubclass(hwnd, &SubclassProc, 1, 0);

            int cmd;
            try
            {
                cmd = NativeMethods.TrackPopupMenuEx(
                    hMenu,
                    NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_RIGHTBUTTON,
                    pt.X, pt.Y, hwnd, 0);
            }
            finally
            {
                if (subclassed)
                {
                    NativeMethods.RemoveWindowSubclass(hwnd, &SubclassProc, 1);
                }
                _menu2 = null;
                _menu3 = null;
            }

            if (cmd == PasteCommandId && background)
            {
                // BUG-005: IFileOperation ベースの貼り付け
                //（同フォルダは「- コピー」自動リネーム、衝突はシェル標準ダイアログ）
                ShellFileOperations.PasteFromClipboard(hwnd, folderPath);
            }
            else if (cmd >= ShellIdFirst && cmd <= ShellIdLast)
            {
                var info = new InvokeCommandInfoEx
                {
                    cbSize = sizeof(InvokeCommandInfoEx),
                    fMask = NativeMethods.CMIC_MASK_PTINVOKE,
                    hwnd = hwnd,
                    lpVerb = (nint)(cmd - ShellIdFirst), // MAKEINTRESOURCE: 下位ワードがコマンド ID
                    nShow = NativeMethods.SW_SHOWNORMAL,
                    ptInvoke = pt,
                };
                menu.InvokeCommand((nint)(&info));
            }
        }
        finally
        {
            if (hMenu != 0)
            {
                NativeMethods.DestroyMenu(hMenu);
            }
        }
    }

    /// <summary>絶対 PIDL を親フォルダ IShellFolder ＋ 子 PIDL（pidl 内を指すため個別解放不要）に分解する。</summary>
    private static IShellFolder? BindToParent(nint pidl, out nint childPidl)
    {
        if (NativeMethods.SHBindToParent(pidl, in IID_IShellFolder, out var parentPtr, out childPidl) < 0 || parentPtr == 0)
        {
            return null;
        }
        try
        {
            return (IShellFolder)ComWrappers.GetOrCreateObjectForComInstance(parentPtr, CreateObjectFlags.None);
        }
        finally
        {
            Marshal.Release(parentPtr); // ラッパーが自前で AddRef 済み
        }
    }

    private static IContextMenu? CreateBackgroundMenu(nint hwnd, IShellFolder parent, nint childPidl)
    {
        // フォルダ自身の IShellFolder に降りてから CreateViewObject で背景メニューを得る
        if (parent.BindToObject(childPidl, 0, in IID_IShellFolder, out var folderPtr) < 0 || folderPtr == 0)
        {
            return null;
        }
        IShellFolder folder;
        try
        {
            folder = (IShellFolder)ComWrappers.GetOrCreateObjectForComInstance(folderPtr, CreateObjectFlags.None);
        }
        finally
        {
            Marshal.Release(folderPtr);
        }
        if (folder.CreateViewObject(hwnd, in IID_IContextMenu, out var menuPtr) < 0 || menuPtr == 0)
        {
            return null;
        }
        try
        {
            return (IContextMenu)ComWrappers.GetOrCreateObjectForComInstance(menuPtr, CreateObjectFlags.None);
        }
        finally
        {
            Marshal.Release(menuPtr);
        }
    }

    [UnmanagedCallersOnly]
    private static nint SubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam, nuint uIdSubclass, nuint dwRefData)
    {
        if (uMsg is NativeMethods.WM_INITMENUPOPUP or NativeMethods.WM_DRAWITEM
            or NativeMethods.WM_MEASUREITEM or NativeMethods.WM_MENUCHAR)
        {
            try
            {
                if (_menu3 is { } m3)
                {
                    m3.HandleMenuMsg2(uMsg, wParam, lParam, out var result);
                    return uMsg == NativeMethods.WM_MENUCHAR ? result : 0;
                }
                if (_menu2 is { } m2)
                {
                    m2.HandleMenuMsg(uMsg, wParam, lParam);
                    return 0;
                }
            }
            catch
            {
                // 転送失敗はメニュー項目が空になるだけに留める
            }
        }
        return NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }
}
