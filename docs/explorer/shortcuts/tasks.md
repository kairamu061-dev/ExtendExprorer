# shortcuts タスク

## 実装タスク一覧

<!-- ステータス: [ ] 未着手 / [~] 進行中 / [x] 完了 -->

- [x] `ShellFileOperations.CopyToClipboard`（CF_HDROP 書き込み）/ `Delete` の実装
- [x] FileListView に KeyboardAccelerator 4 本（Ctrl+C/X/V, Delete）を配線
- [x] PaneView にマウスサイドボタン（戻る/進む）を配線
- [x] test-cases.md の作成と CI グリーン確認・確認依頼の作成（run 29652308444 / aot・jit 両 success・警告ゼロ。E2E は検証待ち）

## 依存関係

- ShellFileOperations（BUG-005 基盤）→ アクセラレータ配線
