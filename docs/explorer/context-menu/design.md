# context-menu 設計

## 技術選定

| 技術 | 用途 | 選定理由 |
|------|------|----------|
| .NET 8 ソース生成 COM（`[GeneratedComInterface]`＋`StrategyBasedComWrappers`） | `IShellFolder` / `IContextMenu`(2/3) の呼び出し | 組み込み COM マーシャリングは AOT 非対応。ソース生成ならビルド時にマーシャリングコードが生成され AOT 安全（BUG-001 の教訓と同方針） |
| `LibraryImport`（P/Invoke ソース生成） | `SHParseDisplayName` / `SHBindToParent` / `CreatePopupMenu` / `TrackPopupMenuEx` 等 | 同上（`DllImport` のランタイムマーシャリングを避ける） |
| Win32 ネイティブメニュー（`TrackPopupMenuEx` + `TPM_RETURNCMD`) | メニュー表示 | シェル拡張項目は HMENU 前提のため XAML の MenuFlyout では表示不可能。OS テーマのメニューがそのまま出る |
| ウィンドウサブクラス化（`SetWindowSubclass`＋`[UnmanagedCallersOnly]`） | `WM_INITMENUPOPUP` / `WM_DRAWITEM` / `WM_MEASUREITEM` を `IContextMenu2/3` へ転送 | 「送る」「プログラムから開く」等の遅延生成サブメニューの中身はこの転送がないと空になる |

## アーキテクチャ

- `Services/ShellContextMenuService`: パス（複数可）とスクリーン座標を受け取り、メニュー表示〜コマンド実行まで担う唯一のクラス。COM/Win32 宣言は `Interop/` 配下に分離
- `FileListView`: `RightTapped` で対象項目の選択＋スクリーン座標算出のみ行い、サービスを呼ぶ（View にシェル知識を持ち込まない）
- HWND は `WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow)` から取得

```
FileListView --RightTapped--> ShellContextMenuService.ShowAsync(hwnd, folderPath, itemName?, screenPt)
                                ├─ itemName あり: SHParseDisplayName → SHBindToParent
                                │   → IShellFolder.GetUIObjectOf(IContextMenu)
                                ├─ itemName なし(背景): IShellFolder.CreateViewObject(IContextMenu)
                                ├─ CreatePopupMenu → QueryContextMenu
                                ├─ SetWindowSubclass（メニュー表示中のみ）→ TrackPopupMenuEx(TPM_RETURNCMD)
                                └─ 戻り値 cmd > 0 → InvokeCommand(cmd - idCmdFirst)
```

## データ構造

シェル API の PIDL / HMENU / HRESULT を扱うのみで、アプリ独自のデータ構造は追加しない。

## インターフェース

```csharp
// ShellContextMenuService（static）
// itemName が null なら folderPath の背景メニュー
static void Show(nint hwnd, string folderPath, string? itemName, Windows.Foundation.Point screenPoint);

// FileListView に追加
RightTapped ハンドラ（項目特定・選択更新・座標変換 → Show 呼び出し）
```

宣言する COM インターフェース（vtable 順を SDK ヘッダに厳密一致させる）:
- `IShellFolder`（`GetUIObjectOf` / `CreateViewObject` まで全メソッド宣言）
- `IContextMenu` / `IContextMenu2` / `IContextMenu3`

## 依存関係

| ライブラリ / サービス | 用途 |
|-----------------------|------|
| shell32.dll / user32.dll / comctl32.dll（OS 標準） | シェル API・メニュー・サブクラス化 |
| （NuGet 追加なし） | |

## リスク・検証ポイント

- ソース生成 COM＋シェル拡張（サードパーティ DLL がプロセス内にロードされる）は AOT 実機での動作確認が必須
- `TPM_RETURNCMD` 中のメッセージポンプはモーダル。UI スレッドをブロックするが、エクスプローラー同様の挙動であり許容
- 失敗時は HRESULT を握りつぶしてメニュー非表示（クラッシュさせないことを最優先）
