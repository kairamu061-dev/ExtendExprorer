# tabs 設計

## 技術選定

親 [design.md](../design.md) の横断方針に従う。タブ UI は WinUI 3 標準の `TabView` を使用する（追加ボタン「＋」・クローズボタン・ホイールクリッククローズを標準装備）。

## アーキテクチャ

- `PaneView` 内の `TabView` を `PaneViewModel.Tabs` / `ActiveTab` に双方向バインド
- `TabView.AddTabButtonClick` → 複製コマンド、`TabCloseRequested` → クローズコマンド
- タブタイトルは `TabViewModel.Path` から導出（フォルダ名、ルートはパスそのもの）

## データ構造

親 design.md の `PaneViewModel` / `TabViewModel` を使用。本サブ項目固有の追加構造はなし。

## インターフェース

```csharp
// PaneViewModel のコマンド
AddTabCommand(string path, TabViewModel? after = null); // ＋（複製）は ActiveTab.Path で呼ぶ
CloseTabCommand(TabViewModel tab);  // 最終タブ→ペインクローズ / 最終ペイン→ホーム再オープン
// アクティブ切替は TabView.SelectedItem ⇔ ActiveTab のバインドで完結
```

- 最終タブ・最終ペインの規則は `MainViewModel` に委譲する（ペインクローズはレイアウト木の操作のため）

## 依存関係

| ライブラリ / サービス | 用途 |
|-----------------------|------|
| WinUI 3 TabView | タブ UI 本体 |
