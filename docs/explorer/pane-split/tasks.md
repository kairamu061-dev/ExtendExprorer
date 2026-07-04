# pane-split タスク

## 実装タスク一覧

<!-- ステータス: [ ] 未着手 / [~] 進行中 / [x] 完了 -->

- [ ] `MainViewModel`: `SplitPaneCommand` / `ClosePaneCommand` / `ActivatePaneCommand`
- [ ] `LayoutHost`: 二分木の再帰描画（Grid 比率バインド＋GridSplitter＋PaneView）
- [ ] GridSplitter ドラッグ完了時の Ratio 書き戻し（0.1–0.9 クランプ）
- [ ] アクティブペイン強調（枠線 Brush）とクリック切替
- [ ] ツールバーに「縦分割」「横分割」ボタン追加・上限（8）制御
- [ ] test-cases.md 記入と手動確認

## 依存関係

- tabs の `CloseTabCommand`（最終タブ規則）→ `ClosePaneCommand` 呼び出し元
- 本サブ項目 → session（レイアウト木が保存対象になる）
