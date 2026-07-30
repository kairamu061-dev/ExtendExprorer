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

## 保存項目の追加（2026-07-30）

`SessionFile` にフォルダツリーの表示状態を追加した。

| 項目 | 型 | 内容 |
|------|----|------|
| `TreeWidth` | `double` | ツリーの展開時の幅（px）。0 以下・項目なしなら既定 240px |
| `TreeCollapsed` | `bool` | 折りたたんだ状態で終了したか |

- **View だけが知る状態**なので `MainViewModel.CaptureSession` には含めず、`MainWindow` が
  付け足す（`Bounds` と同じ扱い）。復元も `MainWindow` が `FolderTreePanel.RestoreLayout` を呼ぶ
- 項目が無い既存の `session.json` は既定値で開くため、`Version` は 1 のまま据え置き
- 保存契機はドラッグ確定・折りたたみ切替のみ（ドラッグ中は保存しない）
