# tabs タスク

## 実装タスク一覧

<!-- ステータス: [ ] 未着手 / [~] 進行中 / [x] 完了 -->

- [x] タブ操作の実装（複製・クローズは MainViewModel の `DuplicateActiveTab` / `CloseTab` に集約。設計の PaneViewModel コマンド案から変更 → dev-notes）
- [x] `PaneView`: TabView のバインド（Tabs / ActiveTab、タイトル導出）
- [x] TabView の `AddTabButtonClick`（複製）・`TabCloseRequested`（クローズ）接続
- [x] タブ上限（50）の制御（上限で AddTabButton 非表示）
- [x] test-cases.md 記入と手動確認（E-01〜E-08 全合格、2026-07-05）
- [x] タブ帯のコンパクト化（帯 32px / タブ 28px / 文字 12px。当初 26/24 は日本語名が見切れたため緩和。2026-07-25〜27 ユーザ要望）
- [x] タブアイコンを一覧・ツリーと同じシェルアイコンに変更（2026-07-27 ユーザ要望）
- [x] アイコンの表示方法を IconSource から Header 内の Image に変更（BUG-011。2026-07-30）

### 独自タブ帯への差し替え（2026-08-09 ユーザ要望・段階導入）

**第 1 段（実装済み）**

- [x] `TabWrapPanel`: 折り返しレイアウト（行の下端を揃え、アクティブだけ上に伸ばす）
- [x] `TabStripItem`: タブ 1 枚（アイコン＋名前・ホバー・アクティブ・中クリック・右クリックメニュー）
- [x] `TabStrip`: 並び・選択・「＋」を最後のタブの直後に流す
- [x] `PaneView` の配線を `TabView` から `TabStrip` へ差し替え
- [x] `MainViewModel.CloseOtherTabs`（右クリックメニュー「他のタブを閉じる」）
- [x] 「×」ボタンの廃止（中クリック／右クリックメニューへ移行）

**第 2 段**

- [ ] 同じ帯の中でのドラッグ並べ替え

**第 3 段**

- [ ] 別ペインへのドラッグ移動
- [ ] 移動できない場所で離したら元の位置へ戻す

## 依存関係

- file-list（タブ切替で一覧が再描画されることの確認に必要）→ 本サブ項目の動作確認
- `CloseTabCommand` の最終タブ規則 → pane-split のペインクローズ処理と連携
