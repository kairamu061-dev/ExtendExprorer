# session 設計

## 技術選定

親 [design.md](../design.md) の横断方針に従う。永続化は System.Text.Json による JSON ファイル（DB 不使用）。

## アーキテクチャ

- `SessionService`: 読み書きとアトミック書込（一時ファイル→ `File.Move(overwrite)`）を担当
- `MainViewModel` が配下の状態変更を集約し、1 秒デバウンスで `SaveAsync` を呼ぶ
- `MainWindow.Closed` で最終保存（同期的に完了させてから終了）
- 起動時は `App.OnLaunched` → `LoadAsync` → パス検証（`ListAsync`）→ ViewModel 木を構築

## データ構造

ViewModel をそのまま保存せず、シリアライズ用のスナップショット型に写す。

```csharp
public record SessionFile(
    int Version,                 // スキーマ版数 = 1
    WindowBounds? Bounds,        // x, y, width, height
    LayoutSnapshot Layout,
    string ActivePaneId);

// LayoutSnapshot = SplitSnapshot | PaneSnapshot（多相は $type 判別で JSON 化）
public record SplitSnapshot(string Id, string Direction, double Ratio,
    LayoutSnapshot First, LayoutSnapshot Second) : LayoutSnapshot;
public record PaneSnapshot(string Id, List<TabSnapshot> Tabs, string ActiveTabId) : LayoutSnapshot;
public record TabSnapshot(string Id, string Path);   // 履歴は保存しない
```

- 保存先: `%LOCALAPPDATA%\ExtendExprorer\session.json`

## インターフェース

```csharp
public interface ISessionService
{
    Task<SessionFile?> LoadAsync();      // 無し・破損は null（破損時は .bak に退避）
    Task SaveAsync(SessionFile file);
}

// 復元シーケンス（MainViewModel）
RestoreAsync(): LoadAsync → 各 TabSnapshot.Path を検証 → 不存在はホームに差替え＋通知 → ViewModel 木を構築
             失敗時は既定状態（1 ペイン 1 タブ、ホーム）
```

## 依存関係

| ライブラリ / サービス | 用途 |
|-----------------------|------|
| System.Text.Json | JSON シリアライズ |
