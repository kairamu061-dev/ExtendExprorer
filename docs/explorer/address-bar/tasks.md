# address-bar タスク

## 実装タスク一覧

<!-- ステータス: [ ] 未着手 / [~] 進行中 / [x] 完了 -->

- [x] `TabViewModel.TryNavigateAsync`＋`IFileSystemService.ResolveNavigationTargetAsync`（検証込み移動・ファイルは親へ）
- [x] `AddressBar`: パンくず表示（セグメントクリック移動）
- [x] 余白クリックで編集モード（全選択）、Enter 移動 / Esc・LostFocus 復帰
- [x] エラーメッセージ表示（3 秒）
- [x] PaneView へ組み込み（暫定 PathText を置換）
- [~] test-cases.md 記入と CI グリーン確認・確認依頼

## 依存関係

- file-list（`ListAsync` と移動処理）→ 本サブ項目の前提
- tabs（アクティブタブ切替時の表示追従確認）→ 動作確認に必要
