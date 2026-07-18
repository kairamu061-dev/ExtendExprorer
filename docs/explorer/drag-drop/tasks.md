# drag-drop タスク

## 実装タスク一覧

<!-- ステータス: [ ] 未着手 / [~] 進行中 / [x] 完了 -->

- [x] ドラッグ元（DragItemsStarting + StorageItems 遅延プロバイダ）
- [x] ドロップ先（DragOver/Drop → ShellFileOperations.Transfer）
- [x] test-cases.md の作成と CI グリーン確認・確認依頼の作成（run 29652308444 / aot・jit 両 success・警告ゼロ。E2E は検証待ち）

## 依存関係

- ShellFileOperations（BUG-005 基盤）→ ドロップ処理
- file-list の複数選択 → 複数項目ドラッグ
