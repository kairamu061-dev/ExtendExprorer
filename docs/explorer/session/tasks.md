# session タスク

## 実装タスク一覧

<!-- ステータス: [ ] 未着手 / [~] 進行中 / [x] 完了 -->

- [ ] main: `sessionStore`（load / save、アトミック書込、破損時 .bak 退避）
- [ ] preload: `sessionSave` / `sessionLoad` 公開
- [ ] renderer: 変更イベントのデバウンス保存
- [ ] renderer: 起動時 `restoreSession`（パス検証・差替え・既定状態）
- [ ] main: `before-quit` の最終保存とウィンドウ bounds 保存
- [ ] renderer: 通知バー
- [ ] test-cases.md 記入と手動確認（削除・破損・不存在パスの 3 系統）

## 依存関係

- 全サブ項目（tabs / pane-split / address-bar / file-list）→ 保存対象の状態が出揃ってから最後に実装
