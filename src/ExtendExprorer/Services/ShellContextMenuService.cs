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

    // TrackPopupMenuEx のモーダルポンプ中のみ非 null（UI スレッド 1 本・同時表示 1 個の前提）
    private static IContextMenu2? _menu2;
    private static IContextMenu3? _menu3;

    /// <summary>一覧項目に対するメニュー。カーソル位置に表示する。</summary>
    public static void ShowForItem(nint hwnd, string itemPath) => Show(hwnd, itemPath, background: false);

    /// <summary>一覧の空白部分＝表示中フォルダの背景メニュー（新規作成・貼り付け等）。</summary>
    public static void ShowForBackground(nint hwnd, string folderPath) => Show(hwnd, folderPath, background: true);

    private static void Show(nint hwnd, string path, bool background)
    {
        nint pidl = 0;
        nint hMenu = 0;
        try
        {
            if (NativeMethods.SHParseDisplayName(path, 0, out pidl, 0, out _) < 0 || pidl == 0)
            {
                return; // 表示直前に削除された等: 何もしない(spec のエラーケース)
            }
            if (BindToParent(pidl, out var childPidl) is not { } parent)
            {
                return;
            }

            var menu = background
                ? CreateBackgroundMenu(hwnd, parent, childPidl)
                : CreateItemMenu(hwnd, parent, childPidl);
            if (menu is null)
            {
                return;
            }

            hMenu = NativeMethods.CreatePopupMenu();
            if (hMenu == 0)
            {
                return;
            }
            const uint idFirst = 1;
            if (menu.QueryContextMenu(hMenu, 0, idFirst, 0x7FFF, 0) < 0)
            {
                return;
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

            if (cmd >= idFirst)
            {
                var info = new InvokeCommandInfoEx
                {
                    cbSize = sizeof(InvokeCommandInfoEx),
                    fMask = NativeMethods.CMIC_MASK_PTINVOKE,
                    hwnd = hwnd,
                    lpVerb = (nint)(cmd - idFirst), // MAKEINTRESOURCE: 下位ワードがコマンド ID
                    nShow = NativeMethods.SW_SHOWNORMAL,
                    ptInvoke = pt,
                };
                menu.InvokeCommand((nint)(&info));
            }
        }
        catch
        {
            // シェル拡張由来の失敗を含め、メニュー機能の失敗でアプリを落とさない
        }
        finally
        {
            if (hMenu != 0)
            {
                NativeMethods.DestroyMenu(hMenu);
            }
            if (pidl != 0)
            {
                Marshal.FreeCoTaskMem(pidl);
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

    private static IContextMenu? CreateItemMenu(nint hwnd, IShellFolder parent, nint childPidl)
    {
        if (parent.GetUIObjectOf(hwnd, 1, in childPidl, in IID_IContextMenu, 0, out var ptr) < 0 || ptr == 0)
        {
            return null;
        }
        try
        {
            return (IContextMenu)ComWrappers.GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.None);
        }
        finally
        {
            Marshal.Release(ptr);
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
