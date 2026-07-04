# pane-split 設計

## 技術選定

親 [design.md](../design.md) の横断方針に従う。レイアウトは CSS flexbox（`flex-grow` に ratio を反映）。

## アーキテクチャ

- `LayoutView`（renderer）: `AppState.layout` の二分木を再帰描画するルートコンポーネント。分割・クローズ時は木全体を差分なしで再構築（初期版は単純さ優先）
- `SplitterHandle`: SplitNode ごとに 1 本。pointerdown → pointermove で ratio を更新（描画は CSS 反映のみ、ストアへの保存は pointerup 時）

## データ構造

親 design.md の `LayoutNode` / `SplitNode` / `PaneLeaf` を使用。

## インターフェース

```ts
// store のアクション
store.splitPane(paneId: string, direction: "horizontal" | "vertical"): PaneLeaf; // 戻り値は新ペイン
store.closePane(paneId: string): void;   // 親 SplitNode を兄弟で置換。最後の 1 ペインは closeTab 側の規則で処理
store.setRatio(splitNodeId: string, ratio: number): void; // 0.1–0.9 にクランプ
store.activatePane(paneId: string): void;
```

- `SplitNode` にも `id` を持たせる（setRatio の対象特定用）。親 design.md の型に `id: string` を追加する

## 依存関係

| ライブラリ / サービス | 用途 |
|-----------------------|------|
| なし | — |
