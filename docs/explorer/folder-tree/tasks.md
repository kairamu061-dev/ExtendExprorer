# folder-tree タスク

## 実装タスク一覧

<!-- ステータス: [ ] 未着手 / [~] 進行中 / [x] 完了 -->

- [x] `IFileSystemService.ListDirectoriesAsync` の追加（フォルダのみ・失敗時空リスト）
- [x] `FolderNodeViewModel` の追加
- [x] `FolderTreePanel`（UserControl: ヘッダ／TreeView／折りたたみ）の追加
- [x] `MainViewModel.NavigateActiveTab` の追加と MainWindow での配線
- [x] MainWindow レイアウト変更（左: FolderTreePanel、右: LayoutHost）
- [x] test-cases.md の作成と CI グリーン確認・確認依頼の作成（run 29194702899 / aot・jit 両 success・警告ゼロ。E2E は検証待ち）
- [x] アイコン・行間・選択色を file-list と揃える（シェルアイコン／行高 22px／濃いめの選択色。2026-07-25 ユーザ要望）
- [x] 行高を Height 固定から MinHeight へ変更（日本語名の見切れ対策。2026-07-27 報告）

## 依存関係

- ListDirectoriesAsync → FolderTreePanel（列挙が前提）
- FolderNodeViewModel → FolderTreePanel
- NavigateActiveTab → MainWindow 配線
