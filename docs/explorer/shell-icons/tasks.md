# shell-icons タスク

## 実装タスク一覧

<!-- ステータス: [ ] 未着手 / [~] 進行中 / [x] 完了 -->

- [x] Interop に SHGetFileInfoW / GDI 系の宣言と構造体を追加
- [x] `ShellIconCache`（取得・変換・キャッシュ）の実装
- [x] `EntryViewModel` に FullPath / Icon / FallbackIconVisibility を追加
- [x] `FileListView` テンプレートを FontIcon＋Image の重ね表示へ変更
- [x] test-cases.md の作成と CI グリーン確認・確認依頼の作成（run 29258584864 / aot・jit 両 success・警告ゼロ。E2E は検証待ち）
- [x] 失敗をキャッシュしない構造へ変更（一度の失敗で全フォルダがグリフ化する問題。BUG-010。2026-07-30）
- [x] 取得の 1 回リトライを追加（一時的な失敗の吸収。2026-07-30）

## 依存関係

- Interop 宣言 → ShellIconCache → EntryViewModel → FileListView（この順が前提）
