# pane-split タスク

## 実装タスク一覧

<!-- ステータス: [ ] 未着手 / [~] 進行中 / [x] 完了 -->

- [ ] store: `splitPane` / `closePane` / `setRatio` / `activatePane` アクション
- [ ] renderer: `LayoutView` 二分木の再帰描画（flexbox）
- [ ] renderer: スプリッターのドラッグ処理（クランプ・カーソル）
- [ ] renderer: アクティブペイン強調とクリック切替
- [ ] ツールバーに「縦分割」「横分割」ボタン追加・上限制御
- [ ] test-cases.md 記入と手動確認

## 依存関係

- tabs の `closeTab`（最終タブ規則）→ `closePane` 呼び出し元
- 本サブ項目 → session（レイアウト木が保存対象になる）
