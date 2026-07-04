# explorer タスク

本機能エリアはサブ項目に分割されている。個別タスクは各サブ項目の tasks.md を参照。

## サブ項目

- [file-list](./file-list/tasks.md)
- [tabs](./tabs/tasks.md)
- [pane-split](./pane-split/tasks.md)
- [address-bar](./address-bar/tasks.md)
- [session](./session/tasks.md)

## 実装タスク一覧（横断タスクのみ）

<!-- ステータス: [ ] 未着手 / [~] 進行中 / [x] 完了 -->

- [x] WinUI 3 プロジェクト雛形作成（src/ExtendExprorer、Windows App SDK / CommunityToolkit 参照、MainWindow）
- [ ] 共有 ViewModel 群の骨格（MainViewModel / PaneViewModel / TabViewModel / LayoutNodeViewModel）
- [ ] サービス層の骨格と DI（IFileSystemService / ISessionService の登録・注入）
- [x] GitHub Actions で Windows ビルド（windows-latest ランナーで `dotnet publish`・成果物アップロード）

## 依存関係

- 横断タスク（雛形・ViewModel・サービス）→ 全サブ項目の前提
- file-list → tabs / address-bar（一覧が動いてから上に載せる）
- tabs / address-bar / pane-split → session（全構成要素の状態を保存するため最後）
