# address-bar タスク

## 実装タスク一覧

<!-- ステータス: [ ] 未着手 / [~] 進行中 / [x] 完了 -->

- [ ] `TabViewModel.NavigateCommand` の検証込み移動（file-list と共用）
- [ ] `AddressBarView`: 表示・編集の切替（GotFocus 全選択、確定まで ViewModel に書き戻さない）
- [ ] Enter 確定 / Esc・LostFocus キャンセル
- [ ] エラーメッセージ表示（3 秒）
- [ ] test-cases.md 記入と手動確認

## 依存関係

- file-list（`ListAsync` と移動処理）→ 本サブ項目の前提
- tabs（アクティブタブ切替時の表示追従確認）→ 動作確認に必要
