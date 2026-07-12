# context-menu 開発メモ

## 実装上の判断

| 判断内容 | 理由 |
|----------|------|
| COM は `[GeneratedComInterface]`＋`StrategyBasedComWrappers`、P/Invoke は `LibraryImport` のみ | 組み込み COM マーシャリングは Native AOT 非対応。BUG-001 の教訓（ビルド時生成に寄せる）と同方針 |
| `IShellFolder` は使わないメソッドも全スロット宣言（文字列引数は `nint` で受ける） | vtable 順ずれは即クラッシュ。呼ばないメソッドの型は緩くてよい |
| `InvokeCommand` の引数を構造体マーシャリングではなく `nint`（`&info`）で渡す | ソース生成の構造体マーシャラ生成を避け、blittable 構造体のポインタ渡しで確実にする |
| メニュー位置は `GetCursorPos`（スクリーン座標） | XamlRoot→スクリーンの座標変換（DPI 換算含む）より単純で、右クリック地点と厳密に一致する |
| `SetWindowSubclass`＋`[UnmanagedCallersOnly]` で WM_INITMENUPOPUP / WM_DRAWITEM / WM_MEASUREITEM / WM_MENUCHAR を IContextMenu2/3 へ転送 | 「送る」「プログラムから開く」等の遅延生成サブメニューは転送がないと空になる。デリゲートではなく関数ポインタなので AOT 安全 |
| 失敗（HRESULT 負値・例外）はすべて握りつぶしメニュー非表示 | プロセス内でサードパーティのシェル拡張が動くため、クラッシュさせないことを最優先 |
| HWND は `XamlRoot.ContentIslandEnvironment.AppWindowId` から取得 | View から `App`/`MainWindow` への静的参照を作らない |

## 発生した問題と対処

| 問題 | 対処 |
|------|------|
| （CI・実機検証待ち） | |

## 設計からの変更点

| 変更内容 | 理由 |
|----------|------|
| RightTapped の「座標変換」は不要になった | メニュー位置に `GetCursorPos` を使ったため（上記判断） |

## 今後の課題

- 「名前の変更」はシェルがエクスプローラーのインライン編集前提のため動かない可能性が高い（実機確認後、自前実装を検討）
- 実行後の一覧自動再読込（ファイルシステム監視）
- 複数選択対応（`GetUIObjectOf` は複数 PIDL を受けられるので拡張は容易）
- 自前メニュー項目（「新しいタブで開く」等）の追加

## ユーザへの要望

- なし
