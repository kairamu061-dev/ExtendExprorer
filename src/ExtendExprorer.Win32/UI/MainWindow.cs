using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ExtendExprorer.Interop;
using ExtendExprorer.Services;
using ExtendExprorer.ViewModels;
using static ExtendExprorer.Interop.Win32;

namespace ExtendExprorer.UI;

/// <summary>アプリのトップレベルウィンドウ。レイアウトは
/// <c>[ツリー][スプリッタ][ペイン領域]</c> の 3 列で、WinUI 版と同じ構成にする。
///
/// <para>第 1 段ではペイン領域に一覧を 1 つ置く。第 2 段でタブとペイン、第 3 段でツリーが入る。</para>
///
/// <para><b>ウィンドウプロシージャは静的メソッド＋インスタンス表</b>にしている。
/// Native AOT ではマネージドのデリゲートをそのままコールバックに渡せないため、
/// <c>[UnmanagedCallersOnly]</c> の静的関数を登録し、<c>HWND</c> から自分を引き直す。</para>
///
/// <para><b>例外は必ずここで止める。</b><c>[UnmanagedCallersOnly]</c> の外へマネージド例外が
/// 出るとランタイムが fail-fast し、ダイアログもスタックも残らずプロセスが即死する
/// （BUG-013 の症状と見分けが付かない）。捕まえた分は <see cref="Diagnostics"/> に残す。</para></summary>
internal sealed unsafe class MainWindow
{
    private const string ClassName = "ExtendExprorer.MainWindow";

    /// <summary>HWND → インスタンスの対応表。ウィンドウは通常 1 つだが、
    /// 将来増えても破綻しないようにしておく。</summary>
    private static readonly Dictionary<nint, MainWindow> Windows = [];

    private static bool _classRegistered;

    private readonly IFileSystemService _fs;
    private PaneHost? _panes;

    private nint _hwnd;
    private nint _instance;
    private nint _font;
    private uint _dpi = 96;

    /// <summary>ツリーの幅（96dpi 基準）。session から復元する。第 3 段でツリーを入れるまでは
    /// 場所を空けるだけで、実際の描画は無い。</summary>
    internal int TreeWidth { get; set; } = 240;

    internal bool TreeCollapsed { get; set; }

    internal nint Handle => _hwnd;

    /// <summary>ペイン領域。ウィンドウができるまでは作れないので、<see cref="Create"/> で用意する。</summary>
    internal PaneHost Panes => _panes ?? throw new InvalidOperationException("ウィンドウがまだ作られていない");

    internal MainWindow(IFileSystemService fs) => _fs = fs;

    internal void Create(string title)
    {
        _instance = GetModuleHandleW(0);
        RegisterClass(_instance);

        _hwnd = CreateWindowExW(0, ClassName, title,
            WS_OVERLAPPEDWINDOW | WS_CLIPCHILDREN,
            CW_USEDEFAULT, CW_USEDEFAULT, 1100, 750,
            0, 0, _instance, 0);
        if (_hwnd == 0)
        {
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastPInvokeError()}");
        }
        Windows[_hwnd] = this;
        _dpi = GetDpiForWindow(_hwnd);
        CreateUiFont();

        Layout();
        _panes = new PaneHost(_fs, _hwnd, _instance, _font, _dpi);
        // 折り返しで帯の行数が変わると、一覧の位置も変わる
        _panes.LayoutChanged += LayoutChildren;
        _panes.Create(ContentBounds);

        // ワーカースレッドからの通知の宛先を決める。溜まっていた分（ウィンドウができる前に
        // 終わった初回読込など）はここで掃き出される
        UiDispatcher.Attach(_hwnd);
    }

    internal void Show()
    {
        LayoutChildren();
        ShowWindow(_hwnd, SW_SHOWNORMAL);
        UpdateWindow(_hwnd);
        Panes.Active.Focus();

        if (Diagnostics.Enabled)
        {
            // 一度描き終わってから数える（起動直後だと通知がまだ来ていない）
            _diagTimer = new System.Threading.Timer(
                _ => UiDispatcher.Post(() => Panes.Active.FileList.WriteDiagnostics()),
                null, 3000, System.Threading.Timeout.Infinite);
        }
    }

    private System.Threading.Timer? _diagTimer;

    private static void RegisterClass(nint instance)
    {
        if (_classRegistered)
        {
            return;
        }
        fixed (char* className = ClassName)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&WndProc,
                hInstance = instance,
                hCursor = LoadCursorW(0, IDC_ARROW),
                hbrBackground = COLOR_WINDOW + 1,
                lpszClassName = (nint)className,
            };
            if (RegisterClassExW(ref wc) == 0)
            {
                throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastPInvokeError()}");
            }
        }
        _classRegistered = true;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            // WM_CREATE より前のメッセージは表に載る前に来るので、既定処理へ回す
            if (!Windows.TryGetValue(hwnd, out var window))
            {
                return DefWindowProcW(hwnd, msg, wParam, lParam);
            }
            return window.HandleMessage(hwnd, msg, wParam, lParam);
        }
        catch (Exception ex)
        {
            // ここで止めないとプロセスが即死する。落とすより、その回の処理を諦める方がよい
            Diagnostics.Report($"WndProc(0x{msg:X4})", ex);
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }
    }

    private nint HandleMessage(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case UiDispatcher.WM_DISPATCH:
                UiDispatcher.Drain();
                return 0;

            case WM_SIZE:
                LayoutChildren();
                return 0;

            case WM_SETFOCUS:
                // 親に来たフォーカスは一覧へ渡す（キーボード操作の起点をそろえる）
                Panes.Active.Focus();
                return 0;

            case WM_CTLCOLORSTATIC:
                // エラー文の板を、一覧と同じ背景・薄い文字色で描かせる
                // （既定のままだとボタン面の灰色になって箱が浮いて見える）
                foreach (var pane in Panes.Panes)
                {
                    if (lParam == pane.FileList.MessageHandle)
                    {
                        SetTextColor(wParam, FileListView.DimmedTextColor);
                        SetBkColor(wParam, GetSysColor(COLOR_WINDOW));
                        return GetSysColorBrush(COLOR_WINDOW);
                    }
                }
                break;

            case WM_NOTIFY:
                // 分割していると一覧が複数ある。どれ宛てかは各自に判定させる
                foreach (var pane in Panes.Panes)
                {
                    if (pane.FileList.TryHandleNotify((ListView.NMHDR*)lParam, out var result))
                    {
                        return result;
                    }
                }
                break;

            case WM_APPCOMMAND:
                // マウスの「戻る」「進む」。子で処理されなかった分が親へ送り上がってくる
                var command = (short)((lParam >> 16) & 0x0FFF);
                if (command == APPCOMMAND_BROWSER_BACKWARD)
                {
                    ActiveList.GoBack();
                    return 1;
                }
                if (command == APPCOMMAND_BROWSER_FORWARD)
                {
                    ActiveList.GoForward();
                    return 1;
                }
                break;

            case WM_DPICHANGED:
                // 上位ワードが新しい DPI。lParam は OS が勧める新しい位置と大きさ
                _dpi = (uint)((wParam >> 16) & 0xFFFF);
                CreateUiFont();
                var suggested = (RECT*)lParam;
                MoveWindow(hwnd, suggested->Left, suggested->Top,
                    suggested->Width, suggested->Height, repaint: true);
                return 0;

            case WM_DESTROY:
                Windows.Remove(hwnd);
                _diagTimer?.Dispose();
                _panes?.Dispose();
                DestroyUiFont();
                PostQuitMessage(0);
                return 0;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    /// <summary>Alt+←／→／↑ での移動。一覧にフォーカスがある状態では
    /// <c>WM_SYSKEYDOWN</c> が親まで上がってこないため、メッセージループで先に見る。</summary>
    private bool OnNavigationKey(int key)
    {
        if ((GetKeyState(VK_MENU) & 0x8000) == 0)
        {
            if ((GetKeyState(VK_CONTROL) & 0x8000) != 0)
            {
                return OnTabKey(key);
            }
            // Backspace は「戻る」。エクスプローラーは Windows 7 以降この割り当てで、
            // 「上へ」は Alt+↑（2026-08-15 に実機で確認）
            // 第 4 段でインライン リネームを入れたら、編集中は横取りしないこと
            if (key == VK_BACK)
            {
                ActiveList.GoBack();
                return true;
            }
            return false;
        }
        switch (key)
        {
            case VK_LEFT:
                ActiveList.GoBack();
                return true;
            case VK_RIGHT:
                ActiveList.GoForward();
                return true;
            case VK_UP:
                ActiveList.GoUp();
                return true;
        }
        return false;
    }

    /// <summary>手前のペインの一覧。キー操作の宛先。</summary>
    private FileListViewModel ActiveList => Panes.Active.Model.FileList;

    /// <summary>Ctrl+T 新しいタブ／Ctrl+W 閉じる／Ctrl+Tab 次のタブ（Shift で前）／
    /// Ctrl+Shift+H 左右に分割／Ctrl+Shift+V 上下に分割。</summary>
    private bool OnTabKey(int key)
    {
        var pane = Panes.Active.Model;
        var count = pane.Tabs.Count;
        var shift = (GetKeyState(VK_SHIFT) & 0x8000) != 0;
        switch (key)
        {
            case VK_H when shift:
                Panes.Split(SplitDirection.Vertical);   // 左右に並べる
                return true;
            case VK_V when shift:
                Panes.Split(SplitDirection.Horizontal); // 上下に並べる
                return true;
            case VK_T:
                // いま見ているフォルダをもう 1 枚開く
                pane.AddTab(ActiveList.Path);
                return true;
            case VK_W:
                pane.CloseTab(pane.ActiveIndex);
                return true;
            case VK_TAB when count > 1:
                pane.Activate((pane.ActiveIndex + (shift ? -1 : 1) + count) % count);
                return true;
        }
        return false;
    }

    private void UpdateTitle()
    {
        if (_hwnd == 0)
        {
            return;
        }
        var path = ActiveList.Path;
        SetWindowTextW(_hwnd, string.IsNullOrEmpty(path) ? "ExtendExprorer" : $"{path} - ExtendExprorer");
    }

    /// <summary>一覧やツリーに設定する UI フォント。設定しないと comctl32 の既定
    /// （古いビットマップフォント）のままになり、エクスプローラーと並べたときに明らかに違って見える。</summary>
    private void CreateUiFont()
    {
        var metrics = new NONCLIENTMETRICSW { cbSize = (uint)sizeof(NONCLIENTMETRICSW) };
        var ok = false;
        try
        {
            // PerMonitorV2 では画面ごとに大きさが違うので、DPI 指定版を優先する
            ok = SystemParametersInfoForDpi(SPI_GETNONCLIENTMETRICS, metrics.cbSize, ref metrics, 0, _dpi);
        }
        catch (EntryPointNotFoundException)
        {
            // Windows 10 1607 より前には無い。素の版に落とす
        }
        if (!ok)
        {
            metrics = new NONCLIENTMETRICSW { cbSize = (uint)sizeof(NONCLIENTMETRICSW) };
            if (!SystemParametersInfoW(SPI_GETNONCLIENTMETRICS, metrics.cbSize, ref metrics, 0))
            {
                return;
            }
        }
        var font = CreateFontIndirectW(ref metrics.lfMessageFont);
        if (font == 0)
        {
            return;
        }
        DestroyUiFont();
        _font = font;
        _panes?.SetFont(_font, _dpi);
    }

    private void DestroyUiFont()
    {
        if (_font != 0)
        {
            DeleteObject(_font);
            _font = 0;
        }
    }

    /// <summary>子の配置。<c>[ツリー][スプリッタ][ペイン領域]</c> の 3 列。
    /// 折りたたみ中はツリーとスプリッタの幅を 0 にする（WinUI 版は 28px の名残が出ていたので、
    /// こちらは最初から完全に畳む前提で組む）。</summary>
    private void Layout()
    {
        if (!GetClientRect(_hwnd, out var client))
        {
            return;
        }
        // ツリーは第 3 段で入る。それまで場所だけ空けておくと、確認のたびに
        // 左端に何も無い帯が出て紛らわしいので、実装が入るまでは幅 0 で詰める
        var showTree = TreeImplemented && !TreeCollapsed;
        var treeWidth = showTree ? Scale(TreeWidth, _dpi) : 0;
        var splitterWidth = showTree ? Scale(SplitterThickness, _dpi) : 0;
        var contentLeft = treeWidth + splitterWidth;

        // 第 2〜3 段でここにタブ・ツリーの MoveWindow が入る。
        // 数字の置き場所を 1 か所にまとめておく
        TreeBounds = new RECT { Left = 0, Top = 0, Right = treeWidth, Bottom = client.Height };
        SplitterBounds = new RECT
        {
            Left = treeWidth,
            Top = 0,
            Right = contentLeft,
            Bottom = client.Height,
        };
        ContentBounds = new RECT
        {
            Left = contentLeft,
            Top = 0,
            Right = client.Width,
            Bottom = client.Height,
        };

    }

    /// <summary>スプリッタの太さ（96dpi 基準）。WinUI 版と同じ 5px。</summary>
    internal const int SplitterThickness = 5;

    /// <summary>フォルダツリーが実装済みか。第 3 段で <c>true</c> にする。</summary>
    private const bool TreeImplemented = false;

    internal RECT TreeBounds { get; private set; }
    internal RECT SplitterBounds { get; private set; }
    internal RECT ContentBounds { get; private set; }

    /// <summary>領域を計算し直して子を並べる。タブ帯の行数が変わったときと、
    /// ウィンドウの大きさが変わったときに呼ぶ。</summary>
    private void LayoutChildren()
    {
        Layout();
        _panes?.SetBounds(ContentBounds);
    }

    /// <summary>共通コントロール（ListView / TreeView / ツールバー）を使う宣言。
    /// マニフェストで comctl32 v6 を指定したうえで、これも呼ぶ必要がある。</summary>
    internal static void InitCommonControls()
    {
        var icc = new INITCOMMONCONTROLSEX
        {
            dwSize = (uint)sizeof(INITCOMMONCONTROLSEX),
            dwICC = ICC_LISTVIEW_CLASSES | ICC_TREEVIEW_CLASSES | ICC_BAR_CLASSES,
        };
        InitCommonControlsEx(ref icc);
    }

    /// <summary>標準のメッセージループ。移動のキー操作だけ、配送前に横取りする。</summary>
    internal static int RunMessageLoop()
    {
        while (GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            if (PreTranslate(ref msg))
            {
                continue;
            }
            TranslateMessage(in msg);
            DispatchMessageW(in msg);
        }
        return 0;
    }

    private static bool PreTranslate(ref MSG msg)
    {
        if (msg.message is not (WM_KEYDOWN or WM_SYSKEYDOWN))
        {
            return false;
        }
        try
        {
            var root = GetAncestor(msg.hwnd, GA_ROOT);
            return Windows.TryGetValue(root, out var window) && window.OnNavigationKey((int)msg.wParam);
        }
        catch (Exception ex)
        {
            Diagnostics.Report("PreTranslate", ex);
            return false;
        }
    }
}
