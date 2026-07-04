# explorer 設計

本機能エリアはサブ項目に分割されている。個別の設計は各サブ項目の design.md を参照。

## サブ項目

- [file-list](./file-list/design.md) — ファイル一覧表示とフォルダナビゲーション
- [tabs](./tabs/design.md) — タブ管理（追加・複製・切替・クローズ）
- [pane-split](./pane-split/design.md) — 画面分割とスプリッター
- [address-bar](./address-bar/design.md) — フォルダパス表示・入力
- [session](./session/design.md) — セッション保存・復元

## 横断的関心事

### 技術選定

| 技術 | 用途 | 選定理由 |
|------|------|----------|
| Electron | アプリ基盤 | Windows 11 ターゲット、Linux devcontainer で開発可能 |
| TypeScript | main / preload / renderer 全実装 | 状態モデルを型で共有できる |
| Vanilla TS + DOM | Renderer UI | 依存を最小に。必要になったらフレームワーク導入を再検討 |

- Main/Renderer 間は `contextBridge` で公開する API のみ（`nodeIntegration: false`, `contextIsolation: true`）

### 共有データ構造（全サブ項目が参照する状態モデル）

```ts
// レイアウトは二分木。leaf がペイン、node が分割
type LayoutNode = PaneLeaf | SplitNode;

interface SplitNode {
  type: "split";
  direction: "horizontal" | "vertical"; // 分割方向
  ratio: number;                        // 先頭側の比率 0–1
  children: [LayoutNode, LayoutNode];
}

interface PaneLeaf {
  type: "pane";
  id: string;
  tabs: Tab[];
  activeTabId: string;
}

interface Tab {
  id: string;
  path: string;        // 現在のフォルダパス
  history: string[];   // 戻る/進む用
  historyIndex: number;
}

interface AppState {
  layout: LayoutNode;
  activePaneId: string;
}
```

- Renderer 側に単一のストア（`AppState`）を置き、各 UI コンポーネントはストアの変更イベントを購読して再描画する
- ID は `crypto.randomUUID()` で採番する

### IPC インターフェース（Main が提供）

| チャンネル | 方向 | 内容 |
|-----------|------|------|
| `fs:list(path)` | R→M | フォルダ内容の列挙。`{ entries: Entry[] } \| { error: string }` を返す |
| `fs:home()` | R→M | ホームディレクトリのパスを返す |
| `session:save(state)` | R→M | AppState を JSON で保存 |
| `session:load()` | R→M | 保存済み AppState を返す（なければ null） |

### ディレクトリ構成（実装）

```
src/
├── main/        # Electron main プロセス（IPC ハンドラ・セッション永続化）
├── preload/     # contextBridge 定義
└── renderer/    # UI（store / pane / tabs / address-bar / file-list）
```

## 依存関係

| ライブラリ / サービス | 用途 |
|-----------------------|------|
| electron | アプリ基盤 |
| electron-builder | Windows 向けパッケージング |
| typescript / esbuild | ビルド |
