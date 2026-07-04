# file-list 設計

## 技術選定

親 [design.md](../design.md) の横断方針に従う（Vanilla TS + DOM、IPC 経由 fs アクセス）。

## アーキテクチャ

- `FileListView`（renderer）: 1 タブに 1 インスタンス。`Tab` の path 変更イベントを受けて `fs:list` を呼び、テーブルを再描画
- `main/fsService.ts`: `fs:list` / `fs:home` ハンドラ。`fs.promises.readdir` + `stat` で列挙

## データ構造

```ts
interface Entry {
  name: string;
  isDirectory: boolean;
  size: number;        // bytes（ディレクトリは 0）
  mtimeMs: number;
}

type ListResult = { entries: Entry[] } | { error: { code: string; message: string } };
```

## インターフェース

```ts
// preload で公開
window.api.fsList(path: string): Promise<ListResult>;
window.api.fsHome(): Promise<string>;

// renderer 内
class FileListView {
  constructor(container: HTMLElement, tab: Tab, store: Store);
  render(): Promise<void>;           // fs:list → テーブル描画
  setSort(column: SortColumn): void; // 同一列で昇順/降順トグル
}
```

- ナビゲーション（移動・履歴）はストアの `Tab` を更新する操作として実装し、`FileListView` は購読側に徹する

## 依存関係

| ライブラリ / サービス | 用途 |
|-----------------------|------|
| なし（Node 標準 fs のみ） | フォルダ列挙 |
