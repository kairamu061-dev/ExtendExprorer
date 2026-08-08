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

- `FileSystemWatcher`（`IncludeSubdirectories=false`、FileName / DirectoryName / Attributes / Size / LastWrite）
- 通知はバーストで届くので **400ms デバウンス**。`DispatcherQueueTimer` で UI スレッドに寄せる
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
- **Tab / Shift+Tab** で確定し、次／前の項目のリネームへ移る。差分更新にしたことでリネームしても
  行は動かないので、確定前の行位置を控えておいて隣を決める（2026-08-07 に名前の追い直しから変更）。
  ただし**監視できないフォルダでは確定後に読み直しが走って並びが変わる**ため、控えた位置に同じ項目が
  居ることを確かめ、違っていれば選択中の項目を起点に取り直す（誤った項目のリネームを防ぐ）
- Tab はフォーカス移動キーで、TextBox の `KeyDown` まで届かないことがある。**Tab だけは
  `FileListView` 側に `handledEventsToo: true` で登録したハンドラで拾う**（Enter / Esc は TextBox のまま）

## 依存関係

| ライブラリ / サービス | 用途 |
|-----------------------|------|
| なし（.NET 標準 System.IO のみ） | フォルダ列挙・`FileSystemWatcher` による変更監視 |
