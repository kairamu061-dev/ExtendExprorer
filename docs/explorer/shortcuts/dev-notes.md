# shortcuts 開発メモ

## 実装上の判断

| 判断内容 | 理由 |
|----------|------|
| クリップボード書き込みは OLE ではなく Win32 API（GlobalAlloc＋SetClipboardData） | IDataObject の managed 実装（CCW）を避け、AOT リスクと実装量を抑える。CF_HDROP＋Preferred DropEffect だけでエクスプローラーと相互運用できる |
| コピーの DropEffect は 5（COPY\|LINK）、切り取りは 2（MOVE） | エクスプローラーが書き込む値に一致させる |
| Delete は確認ダイアログをアプリで出さない | FOF_ALLOWUNDO 付き IFileOperation でシェルの標準確認・ごみ箱動作に任せる |
| サイドボタンは PaneView の既存 PointerPressed（handledEventsToo）で判定 | 一覧・ツールバーどこでクリックしても効く。専用ハンドラ追加が不要 |

## 発生した問題と対処

| 問題 | 対処 |
|------|------|
| （CI・実機検証待ち） | |

## 設計からの変更点

| 変更内容 | 理由 |
|----------|------|
| なし | |

## 今後の課題

- F2 リネーム（シェルメニューにも「名前の変更」が無いため自前実装が必要。IFileOperation::RenameItem）
- 切り取り中項目の半透明表示

## ユーザへの要望

- なし
