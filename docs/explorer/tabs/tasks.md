# tabs タスク

## 実装タスク一覧

<!-- ステータス: [ ] 未着手 / [~] 進行中 / [x] 完了 -->

- [ ] `PaneViewModel`: `AddTabCommand` / `CloseTabCommand`（最終タブ・最終ペインの規則は MainViewModel へ委譲）
- [ ] `PaneView`: TabView のバインド（Tabs / ActiveTab、タイトル導出）
- [ ] TabView の `AddTabButtonClick`（複製）・`TabCloseRequested`（クローズ）接続
- [ ] タブ上限（50）の制御（AddTabButton 無効化）
- [ ] test-cases.md 記入と手動確認

## 依存関係

- file-list（タブ切替で一覧が再描画されることの確認に必要）→ 本サブ項目の動作確認
- `CloseTabCommand` の最終タブ規則 → pane-split のペインクローズ処理と連携
