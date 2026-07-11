# pane-split タスク

## 実装タスク一覧

<!-- ステータス: [ ] 未着手 / [~] 進行中 / [x] 完了 -->

- [x] `MainViewModel`: `SplitPane` / `ClosePane` / `ActivatePane` と `LayoutChanged` イベント（`CloseTab` の最終タブ規則もペインクローズ対応に更新）
- [x] `LayoutHost`: 二分木の再帰描画（Grid の Star 比率＋SplitterBar＋PaneView）
- [x] `SplitterBar`: ドラッグ処理（0.1–0.9 クランプ・リサイズカーソル・完了時に Ratio 書き戻し）
- [x] アクティブペイン強調（枠線 Brush）とクリック切替
- [x] ペインのツールバーに「縦分割」「横分割」ボタン追加・上限（8）で無効化
- [~] test-cases.md 記入と手動確認（記入済み。手動確認はユーザ環境で実施待ち）

## 依存関係

- tabs の `CloseTab`（最終タブ規則）→ `ClosePane` 呼び出し元
- 本サブ項目 → session（レイアウト木が保存対象になる）
