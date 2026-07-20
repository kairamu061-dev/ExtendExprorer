# session タスク

## 実装タスク一覧

<!-- ステータス: [ ] 未着手 / [~] 進行中 / [x] 完了 -->

- [x] `SessionService`（Load / Save / SaveSync、アトミック書込、破損時 .bak 退避）
- [x] スナップショット型（SessionFile / LayoutSnapshot / TabSnapshot / WindowBounds）と AOT 対応 JSON コンテキスト
- [x] `MainViewModel`: 状態変更集約（SessionDirty）とスナップショット化・復元
- [x] 起動時 `RestoreAsync`（パス検証・差替え・既定状態フォールバック）
- [x] `MainWindow`: デバウンス保存・Closed 最終保存・ウィンドウ bounds 保存/復元
- [x] 通知バー（InfoBar・10 秒自動クローズ）
- [x] test-cases.md 記入・CI グリーン確認（run 29745106204 / aot・jit 両 success・警告ゼロ）・確認依頼（E2E は実機検証待ち）

## 依存関係

- 全サブ項目（tabs / pane-split / address-bar / file-list）→ 保存対象の状態が出揃ってから最後に実装
