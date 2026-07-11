# pane-split 設計

## 技術選定

親 [design.md](../design.md) の横断方針に従う。分割は `Grid`（2 行 or 2 列）＋ 自作の `SplitterBar`（Border 派生）で実現する。
（当初案の CommunityToolkit Sizers の GridSplitter は不採用: AOT 互換の追加リスクと依存追加を避けるため。dev-notes 参照）

## アーキテクチャ

- `LayoutHost`（Views）: `MainViewModel.Layout` の二分木を再帰描画するルートビュー
  - `SplitNodeViewModel` → `Grid`（Star 比率 = Ratio）＋ `SplitterBar` ＋ 子要素 × 2（再帰）
  - `PaneViewModel` → `PaneView`（タブ複製・クローズ・分割・活性化のイベントを MainViewModel へ接続）
- 構造変更（分割・ペインクローズ）は `MainViewModel.LayoutChanged` イベントで木全体を再構築（初期版は単純さ優先）。Ratio 変更では再構築しない
- `SplitterBar`: SplitNode ごとに 1 本。PointerPressed でキャプチャ → PointerMoved 中は Grid の Star 値を直接更新 → PointerReleased で `SplitNodeViewModel.Ratio` へ書き戻す（セッション保存対象のため）
- 方向の定義: `SplitDirection.Vertical` = 縦のスプリッターで左右分割、`Horizontal` = 横のスプリッターで上下分割

## データ構造

親 design.md の `SplitNodeViewModel` / `PaneViewModel` を使用。

## インターフェース

```csharp
// MainViewModel
public void SplitPane(PaneViewModel pane, SplitDirection direction); // 新ペインを作りアクティブ化
public void ClosePane(PaneViewModel pane); // 親 SplitNode を兄弟で置換。最後の 1 ペインは CloseTab 側の規則で処理
public void ActivatePane(PaneViewModel pane);
public event Action? LayoutChanged;        // 構造変更を LayoutHost へ通知
public const int MaxPanes = 8;
// Ratio の変更は SplitterBar からの直接代入(0.1–0.9 にクランプ)
```

- アクティブペイン強調は `PaneView.SetActive(bool)`（枠線 Brush 切替）。`LayoutHost` が `ActivePane` の変更を購読して呼び分ける

## 依存関係

| ライブラリ / サービス | 用途 |
|-----------------------|------|
| なし（Toolkit Sizers は不採用） | — |
