# tabs 設計

## 技術選定

親 [design.md](../design.md) の横断方針に従う。

## アーキテクチャ

- `TabBarView`（renderer）: 1 ペインに 1 インスタンス。`PaneLeaf.tabs` / `activeTabId` の変更を購読して再描画
- タブ操作はすべてストアのアクション経由（View は DOM イベント → アクション呼び出しのみ）

## データ構造

親 design.md の `Tab` / `PaneLeaf` を使用。本サブ項目固有の追加構造はなし。

## インターフェース

```ts
// store のアクション
store.addTab(paneId: string, path: string, afterTabId?: string): Tab; // ＋（複製）は path=アクティブタブの path で呼ぶ
store.closeTab(paneId: string, tabId: string): void;  // 最終タブ→ペインクローズ / 最終ペイン→ホーム再オープン
store.activateTab(paneId: string, tabId: string): void;
```

## 依存関係

| ライブラリ / サービス | 用途 |
|-----------------------|------|
| なし | — |
