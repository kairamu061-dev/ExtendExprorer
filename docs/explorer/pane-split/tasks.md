# pane-split タスク

## 実装タスク一覧

<!-- ステータス: [ ] 未着手 / [~] 進行中 / [x] 完了 -->

- [x] `MainViewModel`: `SplitPane` / `ClosePane` / `ActivatePane` と `LayoutChanged` イベント（`CloseTab` の最終タブ規則もペインクローズ対応に更新）
- [x] `LayoutHost`: 二分木の再帰描画（Grid の Star 比率＋SplitterBar＋PaneView）
- [x] `SplitterBar`: ドラッグ処理（0.1–0.9 クランプ・リサイズカーソル・完了時に Ratio 書き戻し）
- [x] アクティブペイン強調（枠線 Brush）とクリック切替
- [x] ペインのツールバーに「縦分割」「横分割」ボタン追加・上限（8）で無効化
- [x] test-cases.md 記入と手動確認（2026-07-12 実機 AOT で E-01〜E-10 確認。E-05 は自動化制約で部分確認＝実機目視の最終確認のみ残る）
- [ ] ペイン破棄時のリソース解放（イベント購読解除・子 View の Dispose）→ [BUG-002](../../../issues/tickets/BUG-002.md) で対応

## 依存関係

- tabs の `CloseTab`（最終タブ規則）→ `ClosePane` 呼び出し元
- 本サブ項目 → session（レイアウト木が保存対象になる）

## 残課題

- **BUG-002（メモリリーク）**: 分割→分割解消の繰り返しでメモリ・ハンドル・スレッドが単調増加。優先度高め。
- **E-05 実機目視**: スプリッターの大幅ドラッグ・10% クランプ・カーソル形状は自動化で未確認。
