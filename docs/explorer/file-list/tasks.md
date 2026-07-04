# file-list タスク

## 実装タスク一覧

<!-- ステータス: [ ] 未着手 / [~] 進行中 / [x] 完了 -->

- [ ] `FileSystemService.ListAsync` 実装（例外→ListError 変換、UI スレッド外実行）
- [ ] `EntryViewModel` の表示用書式（日時・サイズ・種類ラベル）
- [ ] `FileListView`（仮想化 ListView の詳細表示 XAML）
- [ ] ソート（ヘッダクリック、フォルダ先頭維持）
- [ ] `TabViewModel` のナビゲーションコマンド（ダブルクリック移動・上へ・戻る/進む・履歴）
- [ ] エラー表示（一覧領域のメッセージ）
- [ ] test-cases.md 記入と手動確認

## 依存関係

- 横断タスク（雛形・ViewModel・サービス骨格）→ 本サブ項目全タスクの前提
- `FileSystemService` → `FileListView` 描画
