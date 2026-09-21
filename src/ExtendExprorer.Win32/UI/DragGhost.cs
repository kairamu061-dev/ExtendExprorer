using static ExtendExprorer.Interop.Win32;

namespace ExtendExprorer.UI;

/// <summary>ドラッグ中にカーソルへ追従する<b>半透明の影</b>（2026-09-21 のご要望）。
///
/// <para><b>最上位の窓を 1 枚だけ作る。</b>タブ帯の中に描く方式にしないのは、
/// このアプリのタブが<b>別のペインの帯へも移せる</b>ため——帯の中に描くと、
/// ペインをまたいだ瞬間に影が消える。</para>
///
/// <para><b>当たり判定の対象にしない</b>（<see cref="WS_EX_TRANSPARENT"/>）。
/// カーソルの真下に居るので、これが無いと
/// <c>WindowFromPoint</c> が影を拾って、落とし先の帯が見つからなくなる。
/// あわせて帯の探し方そのものも、矩形で引く形に変えてある
/// （<c>TabStripView.StripAt</c>）。</para>
///
/// <para><b>ドラッグの間だけ生きる。</b>掴んだときに作り、離したら壊す。
/// 常駐しないのでメモリ要件には響かない。</para></summary>
internal static class DragGhost
{
    /// <summary>透け具合。0 で完全に透明、255 で不透明。
    /// <b>下が透けて見えつつ、何を持っているかは読める</b>ところ。</summary>
    private const byte Alpha = 180;

    private static nint _hwnd;

    /// <summary>掴んだ位置（影の中での相対座標）。同じ場所がカーソルに付いてくるようにする。</summary>
    private static int _grabX;
    private static int _grabY;

    /// <summary>いま出している影の窓。無ければ 0。
    /// <b>当たり判定から外すために外から見えるようにしてある。</b></summary>
    internal static nint Handle => _hwnd;

    /// <summary>影を出す。<paramref name="draw"/> は、渡した DC の
    /// <c>(0,0)-(width,height)</c> に中身を描く。</summary>
    internal static void Begin(int width, int height, int grabX, int grabY, Action<nint> draw)
    {
        End();
        if (width <= 0 || height <= 0)
        {
            return;
        }
        _grabX = grabX;
        _grabY = grabY;

        // クラスは登録しない。STATIC を借りるだけ（中身は UpdateLayeredWindow で差し替えるので、
        // STATIC 自身の描画は一度も見えない）
        _hwnd = CreateWindowExW(
            WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
            WC_STATIC, null, WS_POPUP,
            0, 0, width, height, 0, 0, GetModuleHandleW(0), 0);
        if (_hwnd == 0)
        {
            return;
        }

        var screen = GetDC(0);
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, width, height);
        var previous = SelectObject(memory, bitmap);
        try
        {
            draw(memory);

            var size = new SIZE { cx = width, cy = height };
            var source = default(POINT);
            var destination = default(POINT);
            // AlphaFormat を 0 にして、画素ごとではなく全体に同じ透け具合をかける
            // （画素ごとの alpha を使うには、あらかじめ乗算した 32bpp の DIB が要る）
            var blend = new BLENDFUNCTION
            {
                BlendOp = AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = Alpha,
                AlphaFormat = 0,
            };
            UpdateLayeredWindow(_hwnd, screen, ref destination, ref size,
                memory, ref source, 0, ref blend, ULW_ALPHA);
        }
        finally
        {
            SelectObject(memory, previous);
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(0, screen);
        }
        ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
    }

    /// <summary>カーソルに追従させる。引数は画面座標。</summary>
    internal static void Move(POINT screen)
    {
        if (_hwnd == 0)
        {
            return;
        }
        SetWindowPos(_hwnd, HWND_TOPMOST, screen.X - _grabX, screen.Y - _grabY, 0, 0,
            SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    /// <summary>影を消す。<b>ドラッグが終わる道すべてから呼ぶこと</b>
    /// （確定・取り消し・キャプチャの取り上げ・帯の破棄）。</summary>
    internal static void End()
    {
        if (_hwnd == 0)
        {
            return;
        }
        DestroyWindow(_hwnd);
        _hwnd = 0;
    }
}
