# drag-drop 設計

## 技術選定

| 技術 | 用途 | 選定理由 |
|------|------|----------|
| WinUI `CanDragItems`+`DragItemsStarting` | ドラッグ元 | 標準 API。`SetDataProvider(StorageItems)` の遅延解決で同期ハンドラから async の StorageItem 取得ができる |
| WinUI `AllowDrop`+`DragOver`/`Drop` | ドロップ先 | 標準 API。`DataView.GetStorageItemsAsync()` で外部からのドロップも同形式で受けられる |
| `IFileOperation`（ShellFileOperations.Transfer） | 実ファイル操作 | BUG-005 基盤を共用。同フォルダ移動の無視・進捗/衝突ダイアログをシェルに任せる |

## アーキテクチャ

```
FileListView(ドラッグ元)
  DragItemsStarting → DataPackage.SetDataProvider(StorageItems, 遅延で StorageFile/Folder 化)
FileListView(ドロップ先)
  DragOver → AcceptedOperation = Ctrl? Copy : Move
  Drop → GetStorageItemsAsync → パス抽出 → ShellFileOperations.Transfer(hwnd, paths, 表示中フォルダ, move)
```

## データ構造

（新規なし。DataPackage の StandardDataFormats.StorageItems）

## インターフェース

```csharp
// ShellFileOperations（既存）
static void Transfer(nint hwnd, IReadOnlyList<string> sources, string destinationFolder, bool move);
```

## 依存関係

| ライブラリ / サービス | 用途 |
|-----------------------|------|
| Windows.Storage / Windows.ApplicationModel.DataTransfer（WinRT 標準） | D&D データ形式 |

## リスク・検証ポイント

- unpackaged アプリでの `StorageFile.GetFileFromPathAsync` の動作（権限周り）は実機確認が必要
- ドロップ既定が「移動」のため、誤ドロップ時はエクスプローラーの Ctrl+Z で戻せる（FOF_ALLOWUNDO）
