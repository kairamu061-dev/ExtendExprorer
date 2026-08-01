# explorer タスク

本機能エリアはサブ項目に分割されている。個別タスクは各サブ項目の tasks.md を参照。

## サブ項目

- [file-list](./file-list/tasks.md)
- [tabs](./tabs/tasks.md)
- [pane-split](./pane-split/tasks.md)
- [address-bar](./address-bar/tasks.md)
- [session](./session/tasks.md)
- [folder-tree](./folder-tree/tasks.md)
- [context-menu](./context-menu/tasks.md)
- [shell-icons](./shell-icons/tasks.md)
- [shortcuts](./shortcuts/tasks.md)
- [drag-drop](./drag-drop/tasks.md)

## 実装タスク一覧（横断タスクのみ）

<!-- ステータス: [ ] 未着手 / [~] 進行中 / [x] 完了 -->

- [x] WinUI 3 プロジェクト雛形作成（src/ExtendExprorer、Windows App SDK / CommunityToolkit 参照、MainWindow）
- [x] 共有 ViewModel 群の骨格（MainViewModel / PaneViewModel / TabViewModel / LayoutNodeViewModel）
- [x] サービス層の骨格と DI（IFileSystemService / ISessionService 実装済み。DI はコンテナを使わず App の合成ルートで注入）
- [x] GitHub Actions で Windows ビルド（windows-latest ランナーで `dotnet publish`・成果物アップロード）
- [x] アイコン整備（ツールバー 5 種＋アプリアイコン。2026-07-27 ユーザ要望）
  - [x] 生成 AI 向けプロンプト作成（`tmp/アイコン生成プロンプト_2026-07-27.md`）
  - [x] ツールバーを暫定で Segoe Fluent Icons のグリフに統一（分割ボタンの文字表記を廃止）
  - [x] 生成画像の受領 → 光学サイズを正規化して `src/ExtendExprorer/Assets/` へ取り込み → `Image` へ差し替え
  - [x] アプリアイコン（.ico マルチサイズ）を `ApplicationIcon` と `AppWindow.SetIcon` に設定
  - [x] 見た目の調整（矢印をソリッド化・分割を自前描画・アプリアイコンのサイズ別最適化。2026-07-30 指摘）
  - [x] 新素材への差し替えとにじみの修正（2026-08-01 指摘）
  - [x] ツールバーをベクター（XAML の Path / Ellipse / Rectangle）へ移行し、ラスタ依存を解消（2026-08-01）
  - [x] ダークテーマ用の白版アイコンは不要になった（ベクター化でブラシを差し替えるだけで済むため）

## 依存関係

- 横断タスク（雛形・ViewModel・サービス）→ 全サブ項目の前提
- file-list → tabs / address-bar（一覧が動いてから上に載せる）
- file-list → folder-tree / context-menu（一覧・ナビゲーションが前提。2026-07-12 のユーザ要望で追加）
- file-list → shell-icons（2026-07-13 のユーザ要望で追加。エクスプローラー風 UI 方針の一部）
- file-list の複数選択・IFileOperation 基盤（BUG-005） → shortcuts / drag-drop（2026-07-19 のユーザ要望で追加）
- tabs / address-bar / pane-split / folder-tree → session（全構成要素の状態を保存するため最後）
