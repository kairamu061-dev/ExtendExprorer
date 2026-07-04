# file-list タスク

## 実装タスク一覧

<!-- ステータス: [ ] 未着手 / [~] 進行中 / [x] 完了 -->

- [ ] main: `fs:list` / `fs:home` ハンドラ実装（エラーコード変換含む）
- [ ] preload: `fsList` / `fsHome` 公開
- [ ] renderer: `FileListView` テーブル描画（書式: 日時・サイズ・種類）
- [ ] renderer: ソート（ヘッダクリック、フォルダ先頭維持）
- [ ] renderer: ダブルクリック移動・上へ・戻る/進む（Tab 履歴更新）
- [ ] renderer: エラー表示
- [ ] test-cases.md 記入と手動確認

## 依存関係

- 横断タスク（雛形・ストア・IPC 土台）→ 本サブ項目全タスクの前提
- `fs:list` ハンドラ → FileListView 描画
