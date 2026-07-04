# address-bar 設計

## 技術選定

親 [design.md](../design.md) の横断方針に従う。

## アーキテクチャ

- `AddressBarView`（renderer）: 1 ペインに 1 インスタンス。`PaneLeaf.activeTabId` と対象 `Tab.path` の変更を購読して表示更新
- パス検証は移動前に `fs:list` を 1 回呼ぶことで兼ねる（成功時の列挙結果はそのまま file-list が利用）

## データ構造

固有の永続データなし。編集中文字列は View のローカル状態。

## インターフェース

```ts
class AddressBarView {
  constructor(container: HTMLElement, paneId: string, store: Store);
}

// store のアクション（file-list と共用）
store.navigate(paneId: string, tabId: string, path: string): Promise<NavigateResult>;
// NavigateResult = "ok" | "not-found" | "denied"
```

## 依存関係

| ライブラリ / サービス | 用途 |
|-----------------------|------|
| なし | — |
