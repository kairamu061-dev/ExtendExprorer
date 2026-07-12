# context-menu タスク

## 実装タスク一覧

<!-- ステータス: [ ] 未着手 / [~] 進行中 / [x] 完了 -->

- [ ] `Interop/` に COM インターフェース（IShellFolder / IContextMenu / 2 / 3）と P/Invoke 宣言を追加
- [ ] `ShellContextMenuService`（項目メニュー表示・実行）の実装
- [ ] 背景メニュー（CreateViewObject 経由）の実装
- [ ] IContextMenu2/3 メッセージ転送（SetWindowSubclass）の実装
- [ ] `FileListView` の RightTapped 配線（項目選択・座標変換）
- [ ] test-cases.md の作成と CI グリーン確認・確認依頼の作成

## 依存関係

- Interop 宣言 → ShellContextMenuService（宣言が前提）
- ShellContextMenuService → FileListView 配線
- 項目メニュー → 背景メニュー（GetUIObjectOf の動作確認後に CreateViewObject へ広げる）
