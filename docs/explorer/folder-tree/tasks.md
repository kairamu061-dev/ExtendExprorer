# folder-tree タスク

## 実装タスク一覧

<!-- ステータス: [ ] 未着手 / [~] 進行中 / [x] 完了 -->

- [x] `IFileSystemService.ListDirectoriesAsync` の追加（フォルダのみ・失敗時空リスト）
- [x] `FolderNodeViewModel` の追加
- [x] `FolderTreePanel`（UserControl: ヘッダ／TreeView／折りたたみ）の追加
- [x] `MainViewModel.NavigateActiveTab` の追加と MainWindow での配線
- [x] MainWindow レイアウト変更（左: FolderTreePanel、右: LayoutHost）
- [~] test-cases.md の作成と CI グリーン確認・確認依頼の作成

## 依存関係

- ListDirectoriesAsync → FolderTreePanel（列挙が前提）
- FolderNodeViewModel → FolderTreePanel
- NavigateActiveTab → MainWindow 配線
