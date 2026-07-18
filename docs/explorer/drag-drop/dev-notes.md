# drag-drop 開発メモ

## 実装上の判断

| 判断内容 | 理由 |
|----------|------|
| OLE DoDragDrop の自前実装ではなく WinUI 標準 D&D | IDataObject/IDropSource の managed 実装（CCW）を避け AOT リスクを抑える。StorageItems 形式でエクスプローラー互換になる |
| ドロップ既定は「移動」・Ctrl でコピー | ユーザ要望が「移動できるようにしたい」であること、エクスプローラーの同一ボリューム既定と一致 |
| 実ファイル操作は Drop 側で IFileOperation を呼ぶ（DataPackageOperation の完了通知に頼らない） | 外部アプリがソースの場合も同じコードパスで処理でき、挙動が予測可能 |
| StorageItem 化は SetDataProvider の遅延解決 | DragItemsStarting が同期ハンドラのため。ドラッグが実際にドロップされるまでファイルに触れない利点もある |

## 発生した問題と対処

| 問題 | 対処 |
|------|------|
| （CI・実機検証待ち） | |

## 設計からの変更点

| 変更内容 | 理由 |
|----------|------|
| なし | |

## 今後の課題

- サブフォルダ行・folder-tree ノードへのドロップ
- Shift/Ctrl によるエクスプローラー完全準拠の効果切替（Shift=強制移動、Ctrl+Shift=ショートカット作成）

## ユーザへの要望

- なし
