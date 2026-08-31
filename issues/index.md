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
| [BUG-008](./tickets/BUG-008.md) | ファイル操作の結果が一覧に反映されない（D&D 移動後に表示が変わらない） | file-list, drag-drop | Closed |
| [BUG-009](./tickets/BUG-009.md) | カーソルを放置すると「Ctrl+C」というツールチップが出る | file-list, shortcuts | Closed |
| [BUG-010](./tickets/BUG-010.md) | 一覧のフォルダアイコンが使用中にグリフへ劣化し、再起動まで戻らない | shell-icons, file-list | Closed |
| [BUG-011](./tickets/BUG-011.md) | タブにシェルアイコンが表示されない | tabs, shell-icons | Closed |
| [BUG-012](./tickets/BUG-012.md) | リネームを別の行のクリックで確定すると、その行が選択されない | file-list, rename | Closed |
| [BUG-013](./tickets/BUG-013.md) | 正式(Native AOT)ビルドが起動直後にクラッシュする | build, aot | Closed |
| [BUG-014](./tickets/BUG-014.md) | フォルダツリーのアイコンと文字が縦に潰れる | folder-tree, layout | Closed |
| [BUG-015](./tickets/BUG-015.md) | 起動直後の初回フォルダ表示で、一覧のアイコンが色なしになる | shell-icons, file-list | Closed |
| [BUG-016](./tickets/BUG-016.md) | タブを増やすとアプリがハングする（1 枚追加あたり約 1 秒） | tabs, performance | Fixed（新実装で解消・旧版は据え置き） |
| [BUG-017](./tickets/BUG-017.md) | 一覧の選択が行番号で追従し、削除で別のファイルに移る | win32-migration, file-list | Closed |
| [BUG-018](./tickets/BUG-018.md) | 一覧で頭文字キーを打っても項目に飛ばない | win32-migration, file-list | Closed |
| [BUG-019](./tickets/BUG-019.md) | 隠し・システム属性の行が薄色にならない | win32-migration, file-list | Closed |
| [BUG-020](./tickets/BUG-020.md) | 読めないフォルダが「アクセスが拒否されました」ではなく「空です」になる | win32-migration, file-list | Closed |
| [BUG-021](./tickets/BUG-021.md) | 起動のたびに error.log へ例外が 4 件記録される | win32-migration, folder-tree | Fixed |
| [BUG-022](./tickets/BUG-022.md) | ウィンドウを狭めると一覧が押し出されて消える | win32-migration, folder-tree, layout | Fixed |
| [BUG-023](./tickets/BUG-023.md) | ペインの帯が幅の変化で描き直されない（分割ボタンの跡） | win32-migration, address-bar | Fixed |
| [BUG-024](./tickets/BUG-024.md) | フォルダツリーのホバー強調が出なくなった | win32-migration, folder-tree | Fixed |
| [BUG-025](./tickets/BUG-025.md) | リネームの衝突が置換の確認ではなくエラー 0x80070057 になる | win32-migration, file-list | Fixed |
| [BUG-026](./tickets/BUG-026.md) | コンテキストメニューの「コピー」「切り取り」が効かない | win32-migration, context-menu | Fixed |
| [BUG-027](./tickets/BUG-027.md) | コンテキストメニューに「名前の変更」が出ない | win32-migration, context-menu | Fixed |
| [BUG-028](./tickets/BUG-028.md) | フォルダ名の衝突が確認なしで統合される | win32-migration, file-list | Fixed |
| [BUG-029](./tickets/BUG-029.md) | ドラッグ中にプロセスが落ちる（0xc0000409） | win32-migration, drag-drop | Open |
