# address-bar 設計

## 技術選定

親 [design.md](../design.md) の横断方針に従う。入力欄は WinUI 3 の `TextBox`（初期版はサジェストなしのため AutoSuggestBox は不使用）。

## アーキテクチャ

- `AddressBarView`（UserControl）: `PaneViewModel.ActiveTab.Path` にバインドして表示。編集中はローカル状態、Enter で確定
- パス検証は `TabViewModel.NavigateCommand` 内の `IFileSystemService.ListAsync` が兼ねる（成功時の列挙結果はそのまま一覧に反映）

## データ構造

固有の永続データなし。編集中文字列は View のローカル状態（確定まで ViewModel に書き戻さない）。

## インターフェース

```csharp
// AddressBarView のイベント処理
KeyDown(Enter) → ActiveTab.NavigateCommand(inputText)
KeyDown(Esc) / LostFocus → 表示を ActiveTab.Path に戻す
GotFocus → SelectAll()

// NavigateCommand の結果（ListErrorKind）に応じてエラーメッセージを 3 秒表示
```

## 依存関係

| ライブラリ / サービス | 用途 |
|-----------------------|------|
| なし | — |
