# folder-tree 設計

## 技術選定

| 技術 | 用途 | 選定理由 |
|------|------|----------|
| WinUI 3 `TreeView`（ItemsSource データバインディング） | 階層表示 | 標準コントロールで仮想化・遅延展開（`HasUnrealizedChildren`＋`Expanding`）をサポート。`{x:Bind}` で AOT 互換 |
| `DriveInfo.GetDrives()` | ルートのドライブ列挙 | BCL のみで完結 |
| `IFileSystemService.ListDirectoriesAsync` | サブフォルダ列挙 | 既存サービスに追加。Task.Run＋例外→空リストのパターンを踏襲 |

## アーキテクチャ

- `FolderTreePanel`（UserControl）: ヘッダ＋TreeView＋折りたたみ状態。MainWindow 直下（LayoutHost の左）に 1 つ
- `FolderNodeViewModel`: ツリー 1 ノード。子の遅延読込フラグを持つ
- 移動は `FolderTreePanel.FolderInvoked(string path)` イベント → MainWindow が `MainViewModel.NavigateActiveTab(path)` へ配線（View から MainViewModel への直接参照を作らない、PaneView と同じイベント委譲方式）

```
MainWindow
 ├─ FolderTreePanel ──FolderInvoked──▶ MainViewModel.NavigateActiveTab
 └─ LayoutHost（既存）
```

## データ構造

```csharp
sealed class FolderNodeViewModel
{
    string Name;                     // 表示名（ドライブは "C:\"、ホームは "ホーム"）
    string Path;                     // フルパス
    bool IsHiddenOrSystem;           // 薄灰色表示用（RowOpacity 0.55/1.0）
    bool HasUnrealizedChildren;      // 未列挙なら true（TreeViewItem にバインド）
    ObservableCollection<FolderNodeViewModel> Children;
}
```

## インターフェース

```csharp
// FileSystemService へ追加（フォルダのみ・名前昇順）
Task<IReadOnlyList<Entry>> ListDirectoriesAsync(string path); // 失敗時は空リスト

// FolderTreePanel
event Action<string>? FolderInvoked;   // ノード Invoke（クリック）

// MainViewModel へ追加
void NavigateActiveTab(string path);   // ActivePane.ActiveTab.NavigateAsync へ委譲
```

- 遅延展開: `TreeView.Expanding` で `args.Item` の `FolderNodeViewModel` を取り、未列挙なら
  `ListDirectoriesAsync` → `Children` へ反映 → `HasUnrealizedChildren = false`
- XAML は `ItemTemplate`（`x:DataType="FolderNodeViewModel"`、ルート要素 `TreeViewItem` に
  `ItemsSource="{x:Bind Children}"` / `HasUnrealizedChildren="{x:Bind HasUnrealizedChildren, Mode=OneWay}"`）

## 依存関係

| ライブラリ / サービス | 用途 |
|-----------------------|------|
| （追加なし。既存の WinUI 3 / BCL のみ） | |
