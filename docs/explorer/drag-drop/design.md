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
  DragOver → 落ちる先を決める（後述）＋ AcceptedOperation = Ctrl? Copy : Move
  Drop → GetStorageItemsAsync → パス抽出 → ShellFileOperations.Transfer(hwnd, paths, 落ちる先, move)
```

### 落ちる先の決め方（2026-08-09 追加）

ポインタの下に**フォルダの行**があれば**そのフォルダの中へ**、無ければ表示中フォルダへ入れる。

- 行の判定は `ListView.ContainerFromItem` で各行の矩形を出し、ポインタの Y と突き合わせる
  （`ListView` に「この座標の項目」を返す API が無いため）
- **ドラッグ中の項目自身の行は落とし先にしない**（自分の中へは入れられない）
- 落とし先の行は `EntryViewModel.IsDropTarget` で塗る。色は選択色（`#A6D3F3`）より薄い `#CFE8FA` にして、
  選択と見分けられるようにする
- `DragUIOverride.Caption` に入る先のフォルダ名（行の上でなければ表示中フォルダのパス）を出す
- 実行の直前に、**移動先が自分自身か自分の下位フォルダになる項目を除外**する
  （シェルも弾くが、手前で落として無用なダイアログを出さない）

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
