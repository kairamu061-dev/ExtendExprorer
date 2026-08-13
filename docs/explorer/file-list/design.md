# file-list 設計

## 技術選定

親 [design.md](../design.md) の横断方針に従う（WinUI 3 / MVVM、サービス層経由の fs アクセス）。

## アーキテクチャ

- `FileListView`（XAML UserControl）: 仮想化 `ListView` の詳細表示。`TabViewModel.Entries` にバインド
- `FileSystemService`: `DirectoryInfo.EnumerateFileSystemInfos()` で列挙し、`Task.Run` で UI スレッドから逃がす
- ナビゲーション（移動・履歴）は `TabViewModel` のコマンドとして実装し、View はバインドするのみ

## データ構造

```csharp
public record Entry(string Name, bool IsDirectory, long Size, DateTime Modified);

public abstract record ListResult;
public sealed record ListOk(IReadOnlyList<Entry> Entries) : ListResult;
public sealed record ListError(ListErrorKind Kind, string Message) : ListResult;
public enum ListErrorKind { NotFound, AccessDenied, Other }

public partial class EntryViewModel : ObservableObject
{
    public Entry Model { get; }
    public string TypeLabel { get; }   // 「フォルダ」/ 拡張子大文字
    public string SizeLabel { get; }   // 「—」/ KB・MB 表記
    public string ModifiedLabel { get; } // YYYY/MM/DD HH:mm
}
```

## インターフェース

```csharp
public interface IFileSystemService
{
    Task<ListResult> ListAsync(string path);
    string HomePath { get; }
}

// TabViewModel のコマンド
NavigateCommand(string path);   // 履歴に追加して移動 → ListAsync → Entries 更新
GoUpCommand();                  // 親フォルダへ（ルートでは CanExecute=false）
GoBackCommand(); GoForwardCommand();
SetSortCommand(SortColumn col); // 同一列で昇順/降順トグル、フォルダ先頭維持
```

## 表示中フォルダの自動再読込（2026-07-25 追加・BUG-008）

シェル操作（D&D・貼り付け・削除）は `IFileOperation` が非同期に完了し、移動元ペインには
そもそも通知が来ない。明示的な `RefreshAsync` では取りこぼすため、`TabViewModel` が
表示中フォルダを監視する。

```csharp
// TabViewModel（抜粋）
public event Action? Navigated;    // 読み込み完了（再読込を含む）。ビューの状態リセット用
public event Action? PathChanged;  // 表示フォルダが実際に変わったときだけ。セッション保存の起点
public void SuspendAutoRefresh();  // リネーム編集中など、作り直されると困る間だけ止める
public void ResumeAutoRefresh();   // 抑制中に届いた変更はここでまとめて反映
public void Dispose();             // タブ・ペインを閉じるとき（監視の解放）
```

- `FileSystemWatcher`（`IncludeSubdirectories=false`）。**監視するのは項目の増減と改名だけ**
  （`NotifyFilter = FileName | DirectoryName`。詳細と理由は後述の「差分更新」節）
- **400ms デバウンスが効くのは全体の読み直しだけ**（`DispatcherQueueTimer`）。
  個別の増減・改名は貯めずにそのまま反映する（下記の差分更新）
- 同じフォルダの再読込では watcher を張り替えない（隙間の変更を落とさないため）
- 再読込では内容が同じ `EntryViewModel` を使い回す（アイコン再取得のちらつき防止）
- `Entries` の差し替え中は `IsLoading` を立てたままにし、ビューが「再読込に伴う一時的な選択解除」と
  ユーザー操作による解除を区別できるようにする。`FileListView` は選択項目を名前で覚えて復元する
- **保存の起点を `Navigated` から `PathChanged` に分離**: 自動再読込のたびに session を保存すると、
  `%LOCALAPPDATA%\ExtendExprorer` を表示しているときに「保存 → 変更通知 → 保存」で回り続ける

## 一覧の追随は差分更新（2026-08-04 変更）

全体を読み直すと `ApplySort` が走り、追加やリネームのたびに行が動いてしまう。
エクスプローラー系のツールと同じく、変わった行だけを触る。

| 監視イベント | 反映 |
|---|---|
| Created | `Entries` の**末尾に足す**（並べ替えない） |
| Deleted | その行だけ消す |
| Renamed | **同じ位置のまま** `EntryViewModel` を差し替える |
| Error（取りこぼし） | 全体を読み直す |

- 監視の `NotifyFilter` は `FileName | DirectoryName` のみ。**サイズ・更新日時の変化は見ない**
  （ホームのように常時書き込みのあるフォルダで勝手に更新がかかり続けるため）
- 全体の読み直しは「移動・明示的な再読込（列ヘッダのソート含む）・取りこぼし」だけ
- リネーム編集中（`SuspendAutoRefresh`）に届いた変更は**順番どおり貯めて**、確定後にそのまま適用する。
  読み直すと並べ替えが走って行が動くため。貯まりすぎた場合（200 件）だけ読み直しに切り替える
- リネーム確定は監視の Renamed に任せる（位置が保たれる）。監視できないパスでのみ `RefreshAsync`

## インライン リネームの状態管理（BUG-006 / BUG-007）

`FileListView` が持つ状態は 3 つだけで、**どの経路で編集が終わっても必ず全部落とす**。

| フィールド | 意味 |
|-----------|------|
| `_renameCandidate` | 直前のタップ完了時点で単独選択だった項目（次のタップが 2 回目かの判定） |
| `_pendingRename` | ダブルクリック判定待ち（`GetDoubleClickTime()+100ms`）の項目 |
| `_renamingEntry` | 編集中の項目。**非 null の間はタップを無視する** |

- `TabViewModel.Navigated` を購読し、移動・再読込のたびに `ResetRenameState()` を実行する。
  同一タブ内の移動では `ViewModel` セッターが呼ばれないため、ここを通さないと `_renamingEntry` が
  残って以後すべてのタップが無視される（BUG-007 の原因）
- `CommitRename` は `box.DataContext` ではなく `_renamingEntry` を正とする。ListView のコンテナ再利用で
  DataContext が別項目に差し替わっていても、状態は必ず後始末し、実際のリネームは実行しない
- 編集ボックス外の `PointerPressed` で確定する（TextBox 内のクリックはハンドラまで届かない）
- **Tab / Shift+Tab** で確定し、次／前の項目のリネームへ移る。**移る先は確定より前に決めておく**
  （`NeighborOf`）。確定するとリネームした行の VM は監視の通知で差し替わり、選択も外れるため、
  あとから位置や選択で辿ると見失う。**隣の行は差し替わらない**ので参照のまま持ち回せる
- 確定から次の編集開始まで**ディスパッチを挟まない**。シェルのリネームは同期で終わっており、
  待つ理由がない。挟むと、その間に届く監視の通知しだいで静かに諦めることがあった（2026-08-10）
- 選択の移動を編集開始より**先**に行う。実機で動かないときに「Tab がハンドラまで届いているか」を
  選択が動いたかどうかで切り分けられるようにするため
- Tab はフォーカス移動キーで、TextBox の `KeyDown` まで届かないことがある。**トンネリングの
  `PreviewKeyDown`** で拾う（Enter / Esc は TextBox のまま）
- **`KeyDown` には登録しない。** 保険のつもりで両方に登録すると Tab 1 回でハンドラが 2 回走る
  （`handledEventsToo` なので `Handled` にしても後段は止まらない）。1 回目が次の行の編集を開始して
  `_renamingEntry` を入れ直すため 2 回目の条件も成立し、開いたばかりの編集を確定して更に 1 行進む
  ＝「編集ボックスが開かない・選択が 2 行進む」になる（2026-08-13 報告）

## 依存関係

| ライブラリ / サービス | 用途 |
|-----------------------|------|
| なし（.NET 標準 System.IO のみ） | フォルダ列挙・`FileSystemWatcher` による変更監視 |
