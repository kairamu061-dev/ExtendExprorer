# session タスク

## 実装タスク一覧

<!-- ステータス: [ ] 未着手 / [~] 進行中 / [x] 完了 -->

- [ ] `SessionService`（Load / Save、アトミック書込、破損時 .bak 退避）
- [ ] スナップショット型（SessionFile / LayoutSnapshot / TabSnapshot）と ViewModel ⇔ スナップショット変換
- [ ] `MainViewModel`: 状態変更集約とデバウンス保存
- [ ] 起動時 `RestoreAsync`（パス検証・差替え・既定状態フォールバック）
- [ ] `MainWindow.Closed` の最終保存とウィンドウ bounds 保存
- [ ] 通知バー（InfoBar）
- [ ] test-cases.md 記入と手動確認（削除・破損・不存在パスの 3 系統）

## 依存関係

- 全サブ項目（tabs / pane-split / address-bar / file-list）→ 保存対象の状態が出揃ってから最後に実装
