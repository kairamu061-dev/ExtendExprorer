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
public sealed class SessionFile
{
    public int Version { get; set; } = 1;
    public WindowBounds? Bounds { get; set; }   // X, Y, Width, Height
    public LayoutSnapshot? Layout { get; set; }
    public double TreeWidth { get; set; }       // ツリーの展開時の幅（0 以下なら既定 240px）
    public bool TreeCollapsed { get; set; }     // 折りたたんだ状態で終了したか
}

/// 多相シリアライズは AOT で扱いが難しいため、継承ではなく Kind で判別する単一型にする
public sealed class LayoutSnapshot
{
    public string Kind { get; set; } = "pane";  // "pane" | "split"

    // Kind == "split"
    public string? Direction { get; set; }      // "Horizontal" | "Vertical"
    public double Ratio { get; set; } = 0.5;
    public LayoutSnapshot? First { get; set; }
    public LayoutSnapshot? Second { get; set; }

    // Kind == "pane"
    public List<TabSnapshot>? Tabs { get; set; }
    public int ActiveTabIndex { get; set; }
    public bool IsActivePane { get; set; }
}

public sealed class TabSnapshot
{
    public string Path { get; set; } = "";      // 履歴は保存しない
}
```

> **設計案からの変更（実装時に確定）**: 当初は `SplitSnapshot` / `PaneSnapshot` の**継承**と
> `ActivePaneId` / `ActiveTabId` による **ID 参照**で設計していたが、
>
> - Native AOT では多相 JSON（`$type` 判別）が扱いにくい → **`Kind` タグ付きの単一型**にした
> - ID を振って参照するより、木の中の**位置（`ActiveTabIndex`）とフラグ（`IsActivePane`）**の方が
>   保存・復元とも単純で、ID の一意性を管理せずに済む
>
> **この形が現行の `session.json` の実際のスキーマ**。UI 基盤を載せ替えても
> **同じ JSON を読み書きできること**が移行の完了条件のひとつ（`docs/win32-migration/`）。

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
