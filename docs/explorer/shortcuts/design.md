# shortcuts 設計

> ## ⚠ ここに書かれている実装は、**撤去した WinUI 3 版**のものです
>
> 2026-09-12 に UI 基盤を **素の Win32 ＋ Native AOT** へ載せ替えました
> （`src/ExtendExprorer.Win32/`）。**仕様（何を作るか）は変わっていません**が、
> 実現の手段は別物です。
>
> **現行実装の設計判断は
> [win32-migration/dev-notes.md](../../win32-migration/dev-notes.md) にあります。**
> この文書は**そのとき何を作ったかの記録**として残してあります。


## 技術選定

| 技術 | 用途 | 選定理由 |
|------|------|----------|
| `KeyboardAccelerator`（ListView 配下） | Ctrl+C/X/V/Delete | XAML 標準・AOT 安全。フォーカスが一覧にあるときだけ効く |
| CF_HDROP + Preferred DropEffect（Win32 クリップボード API） | コピー/切り取りの書き込み | エクスプローラー互換形式。OLE の IDataObject 実装を書かずに済む |
| `IFileOperation`（ShellFileOperations） | 貼り付け・削除の実行 | BUG-005 基盤を共用。ダイアログ・ごみ箱・自動リネームはシェル任せ |
| `PointerPressed` の `IsXButton1/2Pressed` | サイドボタン | PaneView の既存アクティブ化ハンドラ（handledEventsToo）に追記するだけで全域で拾える |

## アーキテクチャ

- `FileListView`: アクセラレータ 4 本 → `ShellFileOperations.CopyToClipboard / PasteFromClipboard / Delete`
- `PaneView.OnPanePointerPressed`: サイドボタン判定 → `TabViewModel.GoBackAsync / GoForwardAsync`
- `ShellFileOperations.CopyToClipboard`: DROPFILES(20B)+ワイド文字列リストを GlobalAlloc → SetClipboardData

## データ構造

（新規なし。クリップボード形式は Win32 標準の DROPFILES）

## インターフェース

```csharp
// ShellFileOperations（BUG-005 で追加済みの基盤に追加）
static void CopyToClipboard(IReadOnlyList<string> paths, bool cut);
static void Delete(nint hwnd, IReadOnlyList<string> paths);
```

## 依存関係

| ライブラリ / サービス | 用途 |
|-----------------------|------|
| user32/kernel32/shell32/ole32（OS 標準） | クリップボード・IFileOperation |
