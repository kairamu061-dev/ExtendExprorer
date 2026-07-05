# tabs タスク

## 実装タスク一覧

<!-- ステータス: [ ] 未着手 / [~] 進行中 / [x] 完了 -->

- [x] タブ操作の実装（複製・クローズは MainViewModel の `DuplicateActiveTab` / `CloseTab` に集約。設計の PaneViewModel コマンド案から変更 → dev-notes）
- [x] `PaneView`: TabView のバインド（Tabs / ActiveTab、タイトル導出）
- [x] TabView の `AddTabButtonClick`（複製）・`TabCloseRequested`（クローズ）接続
- [x] タブ上限（50）の制御（上限で AddTabButton 非表示）
- [~] test-cases.md 記入と手動確認（記入済み。手動確認はユーザ環境で実施待ち）

## 依存関係

- file-list（タブ切替で一覧が再描画されることの確認に必要）→ 本サブ項目の動作確認
- `CloseTabCommand` の最終タブ規則 → pane-split のペインクローズ処理と連携
