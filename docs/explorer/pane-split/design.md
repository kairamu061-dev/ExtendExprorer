# pane-split 設計

## 技術選定

親 [design.md](../design.md) の横断方針に従う。分割は `Grid`（2 行 or 2 列）＋ `GridSplitter`（CommunityToolkit.WinUI.Controls.Sizers）で実現する。

## アーキテクチャ

- `LayoutHost`（UserControl）: `LayoutNodeViewModel` の二分木を再帰描画するルート
  - `SplitNodeViewModel` → `Grid`（`*` 比率を `Ratio` にバインド）＋ `GridSplitter` ＋ 子 `LayoutHost` × 2
  - `PaneViewModel` → `PaneView`
- 分割・クローズ時は該当サブツリーのみ再構築
- `GridSplitter` のドラッグ完了時に実比率を `Ratio` へ書き戻す（セッション保存対象のため）

## データ構造

親 design.md の `SplitNodeViewModel` / `PaneViewModel` を使用。

## インターフェース

```csharp
// MainViewModel のコマンド
SplitPaneCommand(PaneViewModel pane, Orientation direction); // 新ペインを返しアクティブ化
ClosePaneCommand(PaneViewModel pane); // 親 SplitNode を兄弟で置換。最後の 1 ペインは tabs 側の規則で処理
ActivatePaneCommand(PaneViewModel pane);
// Ratio の変更は GridSplitter のバインディングで直接反映（0.1–0.9 にクランプ）
```

- アクティブペイン強調は `MainViewModel.ActivePane` との比較を XAML の枠線 Brush に変換して表現

## 依存関係

| ライブラリ / サービス | 用途 |
|-----------------------|------|
| CommunityToolkit.WinUI.Controls.Sizers | GridSplitter |
