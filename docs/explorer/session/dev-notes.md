# session 開発メモ

## 実装上の判断

| 判断内容 | 理由 |
|----------|------|
| JSON は System.Text.Json の**ソース生成コンテキスト**（`SessionJsonContext`）経由のみ | 組み込みのリフレクション JSON は Native AOT 非対応（BUG-001 と同方針）。`JsonSerializerIsReflectionEnabledByDefault=false` で誤用も検出 |
| レイアウトスナップショットを継承の多相でなく **Kind タグ付き単一型** に | 多相 JSON（`$type`）は AOT で扱いが難しい。`Kind="pane"/"split"` で判別する単一 `LayoutSnapshot` が確実 |
| 状態変更を `MainViewModel.SessionDirty` 1 本に集約し、生成ヘルパー（NewTab/NewPane/NewSplit）で購読を張る | タブ移動・タブ増減・アクティブ切替・分割比率・構造変更を漏れなく拾う。購読対象が破棄されれば購読も消える（生存 this を参照するだけでリークしない） |
| 復元中は `_restoring` フラグで RaiseDirty を抑制 | 復元時の大量の NavigateAsync が保存を誘発しないように |
| 保存は 1 秒デバウンス（変更・ウィンドウ移動/リサイズ）＋ Closed で同期保存 | 連続操作でのディスク書込を抑えつつ、終了時は確実に最終状態を残す |
| ウィンドウ位置/サイズは `AppWindow.Position/Size` と `MoveAndResize` | WinUI 3 desktop 標準。復元不能座標（画面外等）は握って既定位置のまま |
| 起動保存の有効化を `_sessionReady` で遅延 | 起動時のアクティブ化に伴う AppWindow.Changed や復元中の変化を保存対象から除外 |
| アトミック書込（`.tmp`→`File.Move(overwrite)`）＋破損時 `.bak` 退避 | 書込中クラッシュでの破損を防ぎ、壊れた場合も既定起動＋原本退避で復旧余地を残す |

## 発生した問題と対処

| 問題 | 対処 |
|------|------|
| （CI・実機検証待ち） | |

## 設計からの変更点

| 変更内容 | 理由 |
|----------|------|
| `SplitSnapshot`/`PaneSnapshot` の継承多相 → `LayoutSnapshot` 単一型（Kind 判別） | AOT で多相 JSON を避けるため（design.md のスキーマは意図を維持しつつ実装を単純化） |
| `ISessionService` に `SaveSync` を追加 | 終了時（Closed）に確実に書き切るための同期保存 |

## 今後の課題

- タブごとの履歴（戻る/進む）の復元（初期版はパスのみ）
- ソート順・選択行・スクロール位置の復元
- folder-tree の折りたたみ状態の保存

## ユーザへの要望

- なし
