# session 設計

## 技術選定

親 [design.md](../design.md) の横断方針に従う。永続化は JSON ファイル（DB 不使用）。

## アーキテクチャ

- `main/sessionStore.ts`: `session:save` / `session:load` ハンドラ。アトミック書込（一時ファイル→rename）
- renderer 側: ストアの変更イベントを 1 秒デバウンスで `session:save` に流す
- `before-quit` で最終保存（renderer に同期要求 → 保存完了後 quit 続行）

## データ構造

```ts
interface SessionFile {
  version: 1;              // スキーマ版数。将来の移行判定に使う
  windowBounds?: { x: number; y: number; width: number; height: number };
  state: AppState;         // 親 design.md の型。ただし Tab.history は保存しない
}
```

- 保存先: `{userData}/session.json`

## インターフェース

```ts
// preload で公開
window.api.sessionSave(file: SessionFile): Promise<void>;
window.api.sessionLoad(): Promise<SessionFile | null>; // 無し・破損は null

// renderer 起動シーケンス
restoreSession(): Promise<AppState>; // load → パス検証（fs:list）→ 差替え/既定状態の決定
```

## 依存関係

| ライブラリ / サービス | 用途 |
|-----------------------|------|
| なし（Node 標準 fs のみ） | JSON 読み書き |
