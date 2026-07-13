# shell-icons タスク

## 実装タスク一覧

<!-- ステータス: [ ] 未着手 / [~] 進行中 / [x] 完了 -->

- [ ] Interop に SHGetFileInfoW / GDI 系の宣言と構造体を追加
- [ ] `ShellIconCache`（取得・変換・キャッシュ）の実装
- [ ] `EntryViewModel` に FullPath / Icon / FallbackIconVisibility を追加
- [ ] `FileListView` テンプレートを FontIcon＋Image の重ね表示へ変更
- [ ] test-cases.md の作成と CI グリーン確認・確認依頼の作成

## 依存関係

- Interop 宣言 → ShellIconCache → EntryViewModel → FileListView（この順が前提）
