using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ExtendExprorer.Interop;
using static ExtendExprorer.Interop.Win32;

namespace ExtendExprorer.UI;

/// <summary>アプリのトップレベルウィンドウ。レイアウトは
/// <c>[ツリー][スプリッタ][ペイン領域]</c> の 3 列で、WinUI 版と同じ構成にする。
///
/// <para>子はまだ置いていない（第 0 段）。第 1 段で一覧、第 2 段でタブとペイン、
/// 第 3 段でツリーが入る。<see cref="Layout"/> だけ先に用意してあるので、
/// 子が増えても配置の考え方は変えずに済む。</para>
///
/// <para><b>ウィンドウプロシージャは静的メソッド＋インスタンス表</b>にしている。
/// Native AOT ではマネージドのデリゲートをそのままコールバックに渡せないため、
/// <c>[UnmanagedCallersOnly]</c> の静的関数を登録し、<c>HWND</c> から自分を引き直す。</para></summary>
internal sealed unsafe class MainWindow
{
    private const string ClassName = "ExtendExprorer.MainWindow";

    /// <summary>HWND → インスタンスの対応表。ウィンドウは通常 1 つだが、
    /// 将来増えても破綻しないようにしておく。</summary>
    private static readonly Dictionary<nint, MainWindow> Windows = [];

    private static bool _classRegistered;

    private nint _hwnd;
    private uint _dpi = 96;

    /// <summary>ツリーの幅（96dpi 基準）。session から復元する。第 3 段でツリーを入れるまでは
    /// 場所を空けるだけで、実際の描画は無い。</summary>
    internal int TreeWidth { get; set; } = 240;

    internal bool TreeCollapsed { get; set; }

    internal nint Handle => _hwnd;

    internal void Create(string title)
    {
        var instance = GetModuleHandleW(0);
        RegisterClass(instance);

        _hwnd = CreateWindowExW(0, ClassName, title,
            WS_OVERLAPPEDWINDOW | WS_CLIPCHILDREN,
            CW_USEDEFAULT, CW_USEDEFAULT, 1100, 750,
            0, 0, instance, 0);
        if (_hwnd == 0)
        {
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastPInvokeError()}");
        }
        Windows[_hwnd] = this;
        _dpi = GetDpiForWindow(_hwnd);
    }

    internal void Show()
    {
        ShowWindow(_hwnd, SW_SHOWNORMAL);
        UpdateWindow(_hwnd);
    }

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
        // WM_CREATE より前のメッセージは表に載る前に来るので、既定処理へ回す
        if (!Windows.TryGetValue(hwnd, out var window))
        {
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }
        return window.HandleMessage(hwnd, msg, wParam, lParam);
    }

    private nint HandleMessage(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_SIZE:
                Layout();
                return 0;

            case WM_DPICHANGED:
                // 上位ワードが新しい DPI。lParam は OS が勧める新しい位置と大きさ
                _dpi = (uint)((wParam >> 16) & 0xFFFF);
                var suggested = (RECT*)lParam;
                MoveWindow(hwnd, suggested->Left, suggested->Top,
                    suggested->Width, suggested->Height, repaint: true);
                return 0;

            case WM_DESTROY:
                Windows.Remove(hwnd);
                PostQuitMessage(0);
                return 0;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
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
        var treeWidth = TreeCollapsed ? 0 : Scale(TreeWidth, _dpi);
        var splitterWidth = TreeCollapsed ? 0 : Scale(SplitterThickness, _dpi);
        var contentLeft = treeWidth + splitterWidth;

        // 第 1〜3 段でここに子ウィンドウの MoveWindow が入る。
        // いまは領域の計算だけを確定させておく（数字の置き場所を 1 か所にするため）
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

    internal RECT TreeBounds { get; private set; }
    internal RECT SplitterBounds { get; private set; }
    internal RECT ContentBounds { get; private set; }

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

    /// <summary>標準のメッセージループ。</summary>
    internal static int RunMessageLoop()
    {
        while (GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            TranslateMessage(in msg);
            DispatchMessageW(in msg);
        }
        return 0;
    }
}
