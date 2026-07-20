# address-bar 設計

## 技術選定

| 技術 | 用途 | 選定理由 |
|------|------|----------|
| WinUI 3 `UserControl`（AddressBar） | パンくず＋編集の 2 状態を 1 コントロールに集約 | ペイン内に 1 つ、状態切替を内部で完結。コードビハインド構築で AOT 安全 |
| フラットな `Button`（セグメント） | 各フォルダ名 | クリックで移動。動的セグメント数のためコードビハインドで生成 |
| `TextBox` | 編集モード | 全選択・Enter/Esc/LostFocus のハンドリング |

## アーキテクチャ

- `AddressBar`（UserControl）: パンくず表示（`Segments` パネルにセグメント Button＋区切りを動的生成）と
  編集用 `TextBox`（`Editor`）を重ね、`_editing` フラグで排他表示
- 移動判定・実行は View に持たせず `Func<string, Task<bool>>? NavigateRequested` に委譲。
  PaneView が `_observedTab.TryNavigateAsync` を割り当てる（PaneView と同じイベント委譲方式）
- パスの反映は PaneView が `SetPath(tab.Path)` を呼ぶ（タブ切替・移動・履歴で `UpdateToolbar` 経由）

```
PaneView.UpdateToolbar → AddressBar.SetPath(path) → BuildBreadcrumb
AddressBar セグメントClick / Editor Enter → NavigateRequested(path)
   → (PaneView) TabViewModel.TryNavigateAsync → IFileSystemService.ResolveNavigationTargetAsync
   → 移動成功で Path 変更 → PropertyChanged → UpdateToolbar → SetPath（パンくず更新）
```

## データ構造

固有の永続データなし。編集中文字列は `Editor.Text`（確定まで ViewModel に書き戻さない）。
セグメントは表示名と累積フルパス（Button.Tag）のペア。

## インターフェース

```csharp
// AddressBar
public void SetPath(string path);                          // 表示更新（パンくず組み直し）
public Func<string, Task<bool>>? NavigateRequested;         // 移動要求（true=移動した / false=無効パス）

// TabViewModel
public Task<bool> TryNavigateAsync(string input);           // 検証込み移動

// IFileSystemService
Task<string?> ResolveNavigationTargetAsync(string input);   // dir=そのまま / file=親 / 無効=null
```

- Enter: `NavigateRequested(text)` が true なら編集終了（移動で SetPath 再構築）、false なら「パスが見つかりません」3 秒表示＋編集継続
- Esc / Enter 以外の LostFocus: 編集破棄しパンくずへ復帰

## 依存関係

| ライブラリ / サービス | 用途 |
|-----------------------|------|
| IFileSystemService（ResolveNavigationTargetAsync） | パス存在検証・ファイル→親解決 |
