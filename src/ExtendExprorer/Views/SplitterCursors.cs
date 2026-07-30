using Microsoft.UI.Input;

namespace ExtendExprorer.Views;

/// <summary>スプリッター用のカーソル。生成のたびに <see cref="InputSystemCursor.Create"/> すると
/// カーソルリソースが解放されず、分割のたびにハンドルが増える(BUG-002)ため、
/// プロセス全体で縦横 1 個ずつを共有する。</summary>
internal static class SplitterCursors
{
    public static readonly InputSystemCursor WestEast =
        InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);

    public static readonly InputSystemCursor NorthSouth =
        InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);
}
