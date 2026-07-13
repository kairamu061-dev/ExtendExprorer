# file-list タスク

## 実装タスク一覧

<!-- ステータス: [ ] 未着手 / [~] 進行中 / [x] 完了 -->

- [x] `FileSystemService.ListAsync` 実装（例外→ListError 変換、UI スレッド外実行）
- [x] `EntryViewModel` の表示用書式（日時・サイズ・種類ラベル）
- [x] `FileListView`（仮想化 ListView の詳細表示 XAML）
- [x] ソート（ヘッダクリック、フォルダ先頭維持）
- [x] `TabViewModel` のナビゲーションコマンド（ダブルクリック移動・上へ・戻る/進む・履歴）
- [x] ファイルのダブルクリックで既定アプリ起動（`ShellExecuteW`。2026-07-13 ユーザ要望）
- [x] エラー表示（一覧領域のメッセージ）
- [x] test-cases.md 記入と手動確認（E-01〜E-10 全合格、2026-07-05）

## 依存関係

- 横断タスク（雛形・ViewModel・サービス骨格）→ 本サブ項目全タスクの前提
- `FileSystemService` → `FileListView` 描画
