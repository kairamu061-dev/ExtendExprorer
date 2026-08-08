# バグチケット一覧

## ステート凡例

| ステート | 説明 |
|---------|------|
| Open | 未着手 |
| In Progress | 対応中 |
| Fixed | 修正済み（未検証） |
| Closed | 修正確認済み |

---

## チケット一覧

| ID | タイトル | タグ | ステート |
|----|---------|------|---------|
| [BUG-001](./tickets/BUG-001.md) | Native AOT ビルドが起動時にクラッシュする | build, aot | Closed |
| [BUG-002](./tickets/BUG-002.md) | ペイン/タブ破棄後にメモリ・ハンドル・スレッドが解放されない（リーク） | pane-split, leak | Closed |
| [BUG-003](./tickets/BUG-003.md) | 一覧の背景（空白）右クリックメニューに「貼り付け」が出ない | context-menu | Closed |
| [BUG-004](./tickets/BUG-004.md) | ダブルクリックで既定アプリがあるのに「開く方法」チューザーが出る | file-list | Closed |
| [BUG-005](./tickets/BUG-005.md) | 同フォルダ貼り付けが「- コピー」にならず衝突ダイアログになる | context-menu | Closed |
| [BUG-006](./tickets/BUG-006.md) | 遅いダブルクリックのリネームが不安定・編集ボックス幅が名前に合わない | file-list, rename | Closed |
| [BUG-007](./tickets/BUG-007.md) | フォルダ移動を挟むとインラインリネームが開始しなくなる | file-list, rename | Closed |
| [BUG-008](./tickets/BUG-008.md) | ファイル操作の結果が一覧に反映されない（D&D 移動後に表示が変わらない） | file-list, drag-drop | Fixed |
| [BUG-009](./tickets/BUG-009.md) | カーソルを放置すると「Ctrl+C」というツールチップが出る | file-list, shortcuts | Closed |
| [BUG-010](./tickets/BUG-010.md) | 一覧のフォルダアイコンが使用中にグリフへ劣化し、再起動まで戻らない | shell-icons, file-list | Closed |
| [BUG-011](./tickets/BUG-011.md) | タブにシェルアイコンが表示されない | tabs, shell-icons | Closed |
| [BUG-012](./tickets/BUG-012.md) | リネームを別の行のクリックで確定すると、その行が選択されない | file-list, rename | Closed |
| [BUG-013](./tickets/BUG-013.md) | 正式(Native AOT)ビルドが起動直後にクラッシュする | build, aot | Closed |
| [BUG-014](./tickets/BUG-014.md) | フォルダツリーのアイコンと文字が縦に潰れる | folder-tree, layout | Fixed |
| [BUG-015](./tickets/BUG-015.md) | 起動直後の初回フォルダ表示で、一覧のアイコンが色なしになる | shell-icons, file-list | Fixed |
