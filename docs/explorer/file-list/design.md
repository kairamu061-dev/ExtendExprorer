# file-list 設計

## 技術選定

親 [design.md](../design.md) の横断方針に従う（WinUI 3 / MVVM、サービス層経由の fs アクセス）。

## アーキテクチャ

- `FileListView`（XAML UserControl）: 仮想化 `ListView` の詳細表示。`TabViewModel.Entries` にバインド
- `FileSystemService`: `DirectoryInfo.EnumerateFileSystemInfos()` で列挙し、`Task.Run` で UI スレッドから逃がす
- ナビゲーション（移動・履歴）は `TabViewModel` のコマンドとして実装し、View はバインドするのみ

## データ構造

```csharp
public record Entry(string Name, bool IsDirectory, long Size, DateTime Modified);

public abstract record ListResult;
public sealed record ListOk(IReadOnlyList<Entry> Entries) : ListResult;
public sealed record ListError(ListErrorKind Kind, string Message) : ListResult;
public enum ListErrorKind { NotFound, AccessDenied, Other }

public partial class EntryViewModel : ObservableObject
{
    public Entry Model { get; }
    public string TypeLabel { get; }   // 「フォルダ」/ 拡張子大文字
    public string SizeLabel { get; }   // 「—」/ KB・MB 表記
    public string ModifiedLabel { get; } // YYYY/MM/DD HH:mm
}
```

## インターフェース

```csharp
public interface IFileSystemService
{
    Task<ListResult> ListAsync(string path);
    string HomePath { get; }
}

// TabViewModel のコマンド
NavigateCommand(string path);   // 履歴に追加して移動 → ListAsync → Entries 更新
GoUpCommand();                  // 親フォルダへ（ルートでは CanExecute=false）
GoBackCommand(); GoForwardCommand();
SetSortCommand(SortColumn col); // 同一列で昇順/降順トグル、フォルダ先頭維持
```

## 依存関係

| ライブラリ / サービス | 用途 |
|-----------------------|------|
| なし（.NET 標準 System.IO のみ） | フォルダ列挙 |
