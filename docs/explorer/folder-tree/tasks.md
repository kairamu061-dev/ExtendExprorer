# folder-tree タスク

## 実装タスク一覧

<!-- ステータス: [ ] 未着手 / [~] 進行中 / [x] 完了 -->

- [x] `IFileSystemService.ListDirectoriesAsync` の追加（フォルダのみ・失敗時空リスト）
- [x] `FolderNodeViewModel` の追加
- [x] `FolderTreePanel`（UserControl: ヘッダ／TreeView／折りたたみ）の追加
- [x] `MainViewModel.NavigateActiveTab` の追加と MainWindow での配線
- [x] MainWindow レイアウト変更（左: FolderTreePanel、右: LayoutHost）
- [x] test-cases.md の作成と CI グリーン確認・確認依頼の作成（run 29194702899 / aot・jit 両 success・警告ゼロ。E2E は検証待ち）
- [x] アイコン・行間・選択色を file-list と揃える（シェルアイコン／行高 22px／濃いめの選択色。2026-07-25 ユーザ要望）
- [x] 行高を Height 固定から MinHeight へ変更（日本語名の見切れ対策。2026-07-27 報告）

## 追加分（2026-09-13: 根をシェルの名前空間へ）

ユーザー要望「左のフォルダビューを、PC・ネットワーク・ダウンロードがある階層にしたい」。
**範囲はユーザー判断で「シェルの名前空間そのまま（ツリーのみ・一覧ペインには触らない）」。**

- [x] `Services/ShellNamespace.cs` — シェルの名前空間を列挙する
      - 根は `SHGetDesktopFolder` ＋ `SHCONTF_FOLDERS | SHCONTF_NAVIGATION_PANE`
      - 子は `BindToObject(IShellFolder)` して同じ旗で列挙
      - 返すのは**絶対 PIDL と素のデータだけ**（名前・パス・旗・アイコン番号）。
        **インターフェイスのポインタはスレッドをまたがせない**
      - **MTA の専用スレッド 1 本**で回す。`ネットワーク` の列挙はブロックしうるので
        UI スレッドでは回せない。STA にしないのは、**ポンプの無い STA が
        デッドロックの温床**で、ポンプを書くとそれ自体が検証対象になるため
- [x] `FolderTreeView` のノードを PIDL 持ちにする
      - `Path` は **null を許す**（`PC` `ネットワーク` にはパスが無い）
      - シェブロンは `SFGAO_HASSUBFOLDER` で決める（今までは常に出して開いてから消していた）
      - 薄色は `SFGAO_HIDDEN`
      - クリックは `SFGAO_FILESYSTEM` **かつ** `Directory.Exists` のときだけ移動。
        それ以外は展開する
- [x] **PIDL の解放**。ノードごとに持つ実体ができるので、捨てる経路すべてで解放する
      （`Destroy` / 項目の削除）。`Ctrl+Shift+G` はマネージドしか数えないので**捕まらない**
- [x] **`[tree] PIDL 保持=N 解放=M`** を診断に出す（漏れを数字で見る。機能と同じコミットで入れる）
- [x] **`[tree] 根` に根の顔ぶれをそのまま出す**（名前と旗）。
      `SHCONTF_NAVIGATION_PANE` が本当にエクスプローラーと同じ集合を返すかは、
      こちらでは確かめられない。**初回の確認で最初に見るのはここ**
- [x] spec.md の更新／test-cases.md への追加

**2026-09-17 完了。**実機確認は 2 回（`1e83ede` / `6f98e5b`）。
途中で出たもの: 根の顔ぶれが期待と違った（`SHCONTF_NAVIGATION_PANE` は
ナビゲーションウィンドウの集合を返さない）・隠しフォルダが落ちた・並び順が列挙順だった。
**3 つとも列挙の引き方の話**で、1 回の修正でまとめて片付いた。

## 依存関係

- ListDirectoriesAsync → FolderTreePanel（列挙が前提）
- FolderNodeViewModel → FolderTreePanel
- NavigateActiveTab → MainWindow 配線
