# プロジェクト概要

## 目的・背景

Windows 11 標準のエクスプローラーはタブ機能を持つが、画面分割（複数ペイン同時表示）や柔軟なセッション復元ができない。
本プロジェクト「ExtendExprorer」は、Tablacus Explorer のようなマルチタブ・マルチペイン型のファイルエクスプローラーを Windows 11 向けに提供し、複数フォルダを同時に見ながらのファイル整理作業を効率化する。

動作の軽さとメモリ使用量の節約を重視し、Web ランタイムを使わないネイティブ実装（WinUI 3）を採用する。

## スコープ

### 作るもの

- タブ型ファイルエクスプローラー（1 ペインに複数タブ）
- 画面分割（複数ペインの同時表示）
- 各ペインのフォルダパス（アドレスバー）表示
- アプリ終了時の状態保存と、次回起動時の復元（タブ・ペイン構成・各タブのパス）
- 「＋」ボタンによるタブの複製（現在のタブと同じパスで新規タブを開く）
- フォルダ内容の一覧表示と基本的なナビゲーション（ダブルクリックで移動、上へ、戻る/進む）

### 作らないもの

- ファイルのコピー/移動/削除/リネームなどの編集操作（初期スコープ外、将来検討）
- ネットワークドライブ・クラウドストレージの特別対応
- アドオン/プラグイン機構
- Windows 以外の OS への対応

## 技術スタック概要

| 技術 | 用途 | 選定理由 |
|------|------|----------|
| WinUI 3 (Windows App SDK) | UI フレームワーク | ネイティブ描画で軽量・省メモリ。仮想化 ListView で大量ファイルに強く、Windows 11 の外観に一致 |
| C# / .NET 8 | アプリ実装全般 | WinUI 3 の標準言語。MVVM との親和性が高い |
| CommunityToolkit.Mvvm | MVVM 基盤 | ObservableObject / RelayCommand 等の定型コード削減 |
| CommunityToolkit.WinUI.Controls.Sizers | ペイン分割 | GridSplitter によるペインサイズ調整 |
| System.Text.Json | セッション永続化 | 追加依存なしの JSON シリアライズ |

選定の経緯: 当初 Electron を検討したが、軽量・省メモリ方針（ユーザ指示）により WinUI 3 に変更（2026-07-04）。比較検討した候補は Tauri / Wails / WinUI 3 / Electron。

## 全体アーキテクチャ

単一プロセスの MVVM 構成。

```
[View]       MainWindow / PaneView / FileListView / AddressBar (XAML)
   │ データバインディング
[ViewModel]  MainViewModel（レイアウト木）/ PaneViewModel / TabViewModel
   │
[Service]    IFileSystemService（フォルダ列挙）/ ISessionService（状態保存・復元）
   │
[OS]         .NET File API / %LOCALAPPDATA%\ExtendExprorer\session.json
```

## 制約

- 実行ターゲット・開発環境ともに Windows 11（WinUI 3 は Linux 上で開発・ビルド・実行できない）
  - 本 devcontainer（Linux）ではドキュメント作成・コードレビューのみ可能。ビルドと動作確認には Windows + .NET 8 SDK + Visual Studio 2022（または `dotnet build` + Windows App SDK）が必要
- 初期版はファイルシステムへの読み取りアクセスのみ（編集操作を持たない）
- 配布は当面 unpackaged（exe 直置き）とし、MSIX 化は将来検討

## UI デザイン方針

### コンセプト

Tablacus Explorer 風の高密度・実用重視 UI。装飾より情報量と操作効率を優先する。
（参考スクリーンショット: 4 ペイン分割、各ペインにタブバー＋アドレスバー＋ファイル一覧）

見た目・操作感は **Windows 標準のエクスプローラーに似せる**（2026-07-13 ユーザ要望）:
ファイル/フォルダのアイコンはシェルの実アイコン、右クリックはシェルのコンテキストメニュー、
ダブルクリックは既定アプリで開く、左側にフォルダツリー、といった標準の作法に合わせていく。

### カラーパレット

WinUI 3 の標準テーマリソース（ライト/ダーク自動追従）を基本とし、固定色は最小限にする。

| 用途 | カラーコード | 説明 |
|------|-------------|------|
| プライマリ | SystemAccentColor | Windows 設定のアクセント色に追従（選択・アクティブタブ） |
| セカンダリ | SubtleFillColorSecondary | 選択行・ホバーの背景 |
| 背景 | LayerFillColorDefault | ペイン背景 |
| テキスト | TextFillColorPrimary | 基本テキスト |
| エラー | SystemFillColorCritical | エラー表示 |

### タイポグラフィ

- フォント: Segoe UI Variable（WinUI 既定）
- 基本サイズ 12–13px 相当（ファイル一覧は高密度表示）

### コンポーネント方針

- タブは WinUI 標準の TabView（追加ボタン内蔵）を使用
- ファイル一覧は仮想化された ListView の詳細表示（名前・更新日時・種類・サイズ）
- ペイン境界は GridSplitter でドラッグ可能
