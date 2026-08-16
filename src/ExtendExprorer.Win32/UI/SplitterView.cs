using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static ExtendExprorer.Interop.Win32;

namespace ExtendExprorer.UI;

/// <summary>ペインの間の細い仕切り。ドラッグで分割比を変える。
///
/// <para>マウスを捕まえている間だけ動かす（捕まえないと、素早く動かしたときに
/// ポインタが仕切りの外へ出て取りこぼす）。</para></summary>
internal sealed unsafe class SplitterView
{
    private const string ClassName = "ExtendExprorer.Splitter";

    /// <summary>仕切りの太さ（96dpi 基準）。WinUI 版と同じ 5px。</summary>
    internal const int Thickness = 5;

    private static readonly Dictionary<nint, SplitterView> Splitters = [];
    private static bool _classRegistered;

    private readonly bool _vertical;
    private nint _hwnd;
    private bool _dragging;

    /// <summary>ドラッグ中の位置（画面座標）。親がこれを見て比を計算する。</summary>
    internal event Action<POINT>? Dragged;

    internal nint Handle => _hwnd;

    /// <summary><paramref name="vertical"/> が true なら縦の仕切り（左右に分ける）。</summary>
    internal SplitterView(bool vertical) => _vertical = vertical;

    internal void Create(nint parent, nint instance, RECT bounds)
    {
        RegisterClass(instance);
        _hwnd = CreateWindowExW(0, ClassName, null, WS_CHILD | WS_VISIBLE,
            bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            parent, 0, instance, 0);
        if (_hwnd == 0)
        {
            throw new InvalidOperationException($"CreateWindowEx({ClassName}) failed: {Marshal.GetLastPInvokeError()}");
        }
        Splitters[_hwnd] = this;
    }

    internal void SetBounds(RECT bounds)
    {
        if (_hwnd != 0)
        {
            MoveWindow(_hwnd, bounds.Left, bounds.Top, bounds.Width, bounds.Height, repaint: true);
        }
    }

    internal void Destroy()
    {
        if (_hwnd != 0)
        {
            DestroyWindow(_hwnd);
            _hwnd = 0;
        }
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
                hCursor = 0, // WM_SETCURSOR で向きに応じて出し分ける
                hbrBackground = COLOR_BTNFACE + 1,
                lpszClassName = (nint)className,
            };
            if (RegisterClassExW(ref wc) == 0)
            {
                throw new InvalidOperationException($"RegisterClassEx({ClassName}) failed: {Marshal.GetLastPInvokeError()}");
            }
        }
        _classRegistered = true;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            if (!Splitters.TryGetValue(hwnd, out var splitter))
            {
                return DefWindowProcW(hwnd, msg, wParam, lParam);
            }
            return splitter.HandleMessage(hwnd, msg, wParam, lParam);
        }
        catch (Exception ex)
        {
            Diagnostics.Report($"Splitter.WndProc(0x{msg:X4})", ex);
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }
    }

    private nint HandleMessage(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_SETCURSOR:
                SetCursor(LoadCursorW(0, _vertical ? IDC_SIZEWE : IDC_SIZENS));
                return 1;

            case WM_LBUTTONDOWN:
                _dragging = true;
                SetCapture(hwnd);
                return 0;

            case WM_MOUSEMOVE:
                if (_dragging)
                {
                    var point = PointOf(lParam);
                    ClientToScreen(hwnd, ref point);
                    Dragged?.Invoke(point);
                }
                return 0;

            case WM_LBUTTONUP:
                if (_dragging)
                {
                    _dragging = false;
                    ReleaseCapture();
                }
                return 0;

            case WM_CAPTURECHANGED:
                _dragging = false;
                return 0;

            case WM_DESTROY:
                Splitters.Remove(hwnd);
                return 0;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
