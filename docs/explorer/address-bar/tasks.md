# address-bar タスク

## 実装タスク一覧

<!-- ステータス: [ ] 未着手 / [~] 進行中 / [x] 完了 -->

- [ ] store: `navigate` アクション（検証込み移動、file-list と共用）
- [ ] renderer: `AddressBarView` 表示状態・編集状態の切替
- [ ] renderer: Enter 確定 / Esc・blur キャンセル
- [ ] renderer: エラーメッセージ表示（3 秒）
- [ ] test-cases.md 記入と手動確認

## 依存関係

- file-list（`fs:list` と移動処理）→ 本サブ項目の前提
- tabs（アクティブタブ切替時の表示追従確認）→ 動作確認に必要
