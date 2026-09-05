using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using ExtendExprorer.Interop;
using static ExtendExprorer.Interop.Win32;

namespace ExtendExprorer.Services;

/// <summary>Windows シェルのコンテキストメニューを出して、選ばれたコマンドを実行する。
/// 現行 WinUI 版からそのまま移した。UI（STA）スレッド専用。
///
/// <para><b>失敗はすべて握りつぶし、「メニューが出ない」だけに留める。</b>
/// サードパーティのシェル拡張が自分のプロセスの中で動くので、
/// 何よりもアプリを落とさないことを優先する。</para></summary>
internal static unsafe class ShellContextMenuService
{
    private static readonly StrategyBasedComWrappers ComWrappers = new();

    private static readonly Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid IID_IContextMenu = new("000214E4-0000-0000-C000-000000000046");

    // シェルへ渡す ID の範囲（idFirst..0x7FFF）の外に、自前の項目を置く
    private const uint ShellIdFirst = 1;
    private const uint ShellIdLast = 0x7FFF;
    private const uint PasteCommandId = 0x8001;

    // メニューを出している間だけ非 null（UI スレッドは 1 本・同時に 1 つしか出ない前提）
    private static IContextMenu2? _menu2;
    private static IContextMenu3? _menu3;

    /// <summary>ファイルを既定のアプリで開く。<b>パス文字列ではなく PIDL で渡す</b>のが要点で、
    /// 文字列で <c>ShellExecute</c> すると既定のアプリがあっても「開く方法」を
    /// 聞かれることがある（旧版 BUG-004）。</summary>
    internal static void OpenWithDefault(nint hwnd, string path)
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
                fMask = NativeMethods.SEE_MASK_INVOKEIDLIST, // verb を省く＝既定の動作
                hwnd = hwnd,
                lpIDList = pidl,
                nShow = SW_SHOWNORMAL,
            };
            NativeMethods.ShellExecuteExW(ref info);
        }
        catch (Exception ex)
        {
            UI.Diagnostics.Report($"ShellContextMenu.OpenWithDefault({path})", ex);
        }
        finally
        {
            if (pidl != 0)
            {
                Marshal.FreeCoTaskMem(pidl);
            }
        }
    }

    /// <summary>選択中の項目（1 件以上・同じフォルダの中）のメニュー。</summary>
    /// <param name="renameRequested">メニューの「名前の変更」が選ばれたときに呼ぶ。
    /// シェルに実行させても、こちらには一覧の編集を始める術が伝わらないので、
    /// 動詞を見て<b>自前のインライン編集へ振り替える</b>。</param>
    internal static void ShowForItems(nint hwnd, string folderPath, IReadOnlyList<string> itemNames,
        Action? renameRequested = null)
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
                if (NativeMethods.SHParseDisplayName(System.IO.Path.Combine(folderPath, name),
                        0, out var pidl, 0, out _) >= 0 && pidl != 0)
                {
                    fullPidls.Add(pidl);
                }
            }
            if (fullPidls.Count == 0)
            {
                return; // 出す直前に全部消えた等。何もしない
            }

            // 全部が同じフォルダの中なので、親は先頭の 1 つから取れば足りる。
            // 子 PIDL は絶対 PIDL の中を指しているので、個別に解放してはいけない
            IShellFolder? parent = null;
            var children = new nint[fullPidls.Count];
            for (var i = 0; i < fullPidls.Count; i++)
            {
                if (NativeMethods.SHBindToParent(fullPidls[i], in IID_IShellFolder,
                        out var parentPtr, out children[i]) < 0)
                {
                    return;
                }
                if (parent is null)
                {
                    try
                    {
                        parent = (IShellFolder)ComWrappers.GetOrCreateObjectForComInstance(
                            parentPtr, CreateObjectFlags.None);
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
                hr = parent!.GetUIObjectOf(hwnd, (uint)children.Length, (nint)pChildren,
                    in IID_IContextMenu, 0, out menuPtr);
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
            TrackAndInvoke(hwnd, menu, background: false, folderPath, renameRequested, newItemRequested: null);
        }
        catch (Exception ex)
        {
            UI.Diagnostics.Report("ShellContextMenu.ShowForItems", ex);
        }
        finally
        {
            foreach (var pidl in fullPidls)
            {
                Marshal.FreeCoTaskMem(pidl);
            }
        }
    }

    /// <summary>一覧の空白＝表示中フォルダの背景メニュー（新規作成・貼り付け等）。</summary>
    /// <param name="newItemRequested">「新規作成」の下の項目が選ばれたときに呼ぶ。
    /// エクスプローラーは作った直後に名前の編集を始めるので、
    /// <b>一覧の側にその合図を渡す</b>（作られるのは監視の通知が来てから）。</param>
    internal static void ShowForBackground(nint hwnd, string folderPath, Action? newItemRequested = null)
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
            TrackAndInvoke(hwnd, menu, background: true, folderPath, renameRequested: null, newItemRequested);
        }
        catch (Exception ex)
        {
            UI.Diagnostics.Report("ShellContextMenu.ShowForBackground", ex);
        }
        finally
        {
            if (pidl != 0)
            {
                Marshal.FreeCoTaskMem(pidl);
            }
        }
    }

    /// <summary>メニューを組む → 出す（モーダル）→ 選ばれたコマンドを実行する。</summary>
    private static void TrackAndInvoke(nint hwnd, IContextMenu menu, bool background, string folderPath,
        Action? renameRequested, Action? newItemRequested)
    {
        nint hMenu = 0;
        try
        {
            hMenu = CreatePopupMenu();
            if (hMenu == 0)
            {
                return;
            }
            // CMF_CANRENAME を渡さないと、シェルは「名前の変更」を足さない。
            // 実行はこちらの一覧の編集に振り替える（下の GetVerb）
            var flags = NativeMethods.CMF_NORMAL | (background ? 0 : NativeMethods.CMF_CANRENAME);
            if (menu.QueryContextMenu(hMenu, 0, ShellIdFirst, ShellIdLast, flags) < 0)
            {
                return;
            }
            if (background)
            {
                // 背景の「貼り付け」はシェルではなくエクスプローラーの一覧が出している項目なので、
                // こちらで足す（旧版 BUG-003）
                var canPaste = NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_HDROP);
                NativeMethods.InsertMenuW(hMenu, 0, NativeMethods.MF_BYPOSITION | MF_SEPARATOR, 0, null);
                NativeMethods.InsertMenuW(hMenu, 0,
                    NativeMethods.MF_BYPOSITION | MF_STRING | (canPaste ? 0 : MF_GRAYED),
                    PasteCommandId, "貼り付け(&P)");
            }

            GetCursorPos(out var pt);

            // 「送る」「プログラムから開く」などは、開いたときに中身を作る。
            // メニューの持ち主に来るメッセージを IContextMenu2/3 へ転送しないと空になる
            _menu2 = menu as IContextMenu2;
            _menu3 = menu as IContextMenu3;
            var subclassed = NativeMethods.SetWindowSubclass(hwnd, &SubclassProc, 1, 0);

            int cmd;
            try
            {
                cmd = NativeMethods.TrackPopupMenuEx(hMenu, TPM_RETURNCMD | TPM_RIGHTBUTTON,
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
                // 同じフォルダなら「〜 - コピー」、衝突はシェルのダイアログ（旧版 BUG-005）
                ShellFileOperations.PasteFromClipboard(hwnd, folderPath);
            }
            else if (cmd >= ShellIdFirst && cmd <= ShellIdLast)
            {
                var id = (uint)(cmd - ShellIdFirst);
                // 「新規作成」の下から選ばれたなら、作られた項目の編集を始めてもらう。
                // 実行する前に見ておく（実行後はメニューが壊されている）
                var isNewItem = newItemRequested is not null && IsUnderNewSubmenu(hMenu, (uint)cmd);
                if (renameRequested is not null &&
                    string.Equals(GetVerb(menu, id), "rename", StringComparison.OrdinalIgnoreCase))
                {
                    // シェルに任せても、一覧の編集を始めるのはこちらの仕事
                    renameRequested();
                    return;
                }
                var info = new InvokeCommandInfoEx
                {
                    cbSize = sizeof(InvokeCommandInfoEx),
                    fMask = NativeMethods.CMIC_MASK_PTINVOKE,
                    hwnd = hwnd,
                    lpVerb = (nint)(cmd - ShellIdFirst), // 下位ワードがコマンド ID
                    nShow = SW_SHOWNORMAL,
                    ptInvoke = pt,
                };
                menu.InvokeCommand((nint)(&info));
                if (isNewItem)
                {
                    newItemRequested!();
                }
            }
        }
        finally
        {
            if (hMenu != 0)
            {
                DestroyMenu(hMenu);
            }
        }
    }

    /// <summary>そのコマンド ID が「新規作成」の部分メニューの中のものか。
    ///
    /// <para><b>動詞では判定できない。</b>「新規作成」の下の項目はシェルの拡張が
    /// 開いたときに作るもので、こちらから見える動詞が無い。そこで
    /// <b>どの部分メニューに属しているか</b>で見る。中身は
    /// <c>WM_INITMENUPOPUP</c> の転送で既に埋まっている。</para>
    ///
    /// <para>見出しの文字で当てる（日本語なら「新規作成(&amp;W)」・英語なら "New"）。
    /// <b>当たらなければ何もしない</b>——編集が始まらないだけで、作成自体は成功する。</para></summary>
    private static bool IsUnderNewSubmenu(nint hMenu, uint cmd)
    {
        try
        {
            var count = NativeMethods.GetMenuItemCount(hMenu);
            for (var i = 0; i < count; i++)
            {
                var sub = NativeMethods.GetSubMenu(hMenu, i);
                if (sub == 0 || !IsNewCaption(hMenu, i))
                {
                    continue;
                }
                return ContainsCommand(sub, cmd);
            }
        }
        catch (Exception ex)
        {
            UI.Diagnostics.Report("ShellContextMenu.IsUnderNewSubmenu", ex);
        }
        return false;
    }

    private static bool IsNewCaption(nint hMenu, int position)
    {
        var buffer = stackalloc char[64];
        var length = NativeMethods.GetMenuStringW(hMenu, (uint)position, (nint)buffer, 64,
            NativeMethods.MF_BYPOSITION);
        if (length <= 0)
        {
            return false;
        }
        var text = new string(buffer, 0, length).Replace("&", "");
        return text.StartsWith("新規作成", StringComparison.Ordinal)
            || text.StartsWith("New", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>その部分メニュー（入れ子も含む）に、このコマンド ID があるか。</summary>
    private static bool ContainsCommand(nint hMenu, uint cmd)
    {
        var count = NativeMethods.GetMenuItemCount(hMenu);
        for (var i = 0; i < count; i++)
        {
            if (NativeMethods.GetMenuItemID(hMenu, i) == cmd)
            {
                return true;
            }
            var sub = NativeMethods.GetSubMenu(hMenu, i);
            if (sub != 0 && ContainsCommand(sub, cmd))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>そのコマンドの「動詞」（<c>rename</c> 等）。答えない拡張もあるので null 許容。</summary>
    private static string? GetVerb(IContextMenu menu, uint id)
    {
        try
        {
            var buffer = stackalloc char[64];
            buffer[0] = '\0';
            if (menu.GetCommandString(id, NativeMethods.GCS_VERBW, 0, (nint)buffer, 64) < 0)
            {
                return null;
            }
            return new string(buffer);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>絶対 PIDL を「親フォルダ ＋ 子 PIDL」に分ける。
    /// 子 PIDL は親の中を指しているので、個別に解放しない。</summary>
    private static IShellFolder? BindToParent(nint pidl, out nint childPidl)
    {
        if (NativeMethods.SHBindToParent(pidl, in IID_IShellFolder, out var parentPtr, out childPidl) < 0
            || parentPtr == 0)
        {
            return null;
        }
        try
        {
            return (IShellFolder)ComWrappers.GetOrCreateObjectForComInstance(parentPtr, CreateObjectFlags.None);
        }
        finally
        {
            Marshal.Release(parentPtr); // ラッパーが自分で参照を足している
        }
    }

    private static IContextMenu? CreateBackgroundMenu(nint hwnd, IShellFolder parent, nint childPidl)
    {
        // そのフォルダ自身へ降りてから、表示側のオブジェクトとして背景メニューを取る
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
    private static nint SubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam,
        nuint uIdSubclass, nuint dwRefData)
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
                // 転送に失敗しても、そのサブメニューが空になるだけに留める
            }
        }
        return NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }
}
