using ExtendExprorer.Models;
using ExtendExprorer.Services;
using ExtendExprorer.UI;

namespace ExtendExprorer.ViewModels;

public enum SortColumn { Name, Modified, Type, Size }

/// <summary>一覧 1 つ分の状態（表示中のパス・履歴・並び順・項目）。
/// 現行 WinUI 版の <c>TabViewModel</c> の移植だが、XAML への依存を外してある。
///
/// <para><b>通知は素の C# イベント</b>。バインドが無いので、ビュー側はイベントを受けて
/// <c>LVM_SETITEMCOUNT</c> や再描画を出す。<c>ObservableCollection</c> も使わない
/// （オーナーデータの一覧はコレクションを見に来ないため）。</para>
///
/// <para><b>ディスクに触る処理は必ずワーカースレッド</b>で行い、結果は
/// <see cref="UiDispatcher"/> 経由で UI スレッドへ戻す。素の Win32 には同期コンテキストが
/// 無いので <c>await</c> の続きはスレッドプールで走ってしまう。</para></summary>
internal sealed class FileListViewModel : IDisposable
{
    private readonly IFileSystemService _fs;
    private readonly List<string> _history = [];
    private int _historyIndex = -1;
    private readonly List<Entry> _entries = [];

    /// <summary>読込の世代。移動を連打したときに、古い読込の結果を捨てるために使う。</summary>
    private int _loadToken;

    private bool _keepSelectionOnReset;

    private FileSystemWatcher? _watcher;
    private readonly System.Threading.Timer _refreshTimer;
    private bool _refreshScheduled;
    private bool _disposed;

    /// <summary>「PC」を表す合言葉。<b>実在しえない形にしてある</b>——
    /// <c>::</c> は Windows のパスに使えないので、本物のフォルダとぶつからない。
    ///
    /// <para><c>shell:MyComputerFolder</c> のような本物っぽい文字列にしないのは、
    /// <b>パスを扱う API に渡ると通ってしまいそうに見える</b>のが危ないため。
    /// ここは「通らない」ことが分かる形がよい。</para></summary>
    internal const string DrivesPath = "::drives";

    /// <summary>その文字列が「PC」の合言葉か。</summary>
    internal static bool IsDrivesPath(string? path) =>
        string.Equals(path, DrivesPath, StringComparison.Ordinal);

    /// <summary>画面に出すときの名前。合言葉をそのまま見せない。</summary>
    internal static string DisplayPath(string path) =>
        string.Equals(path, DrivesPath, StringComparison.Ordinal) ? "PC" : path;

    /// <summary><b>これから見せる先。</b>アドレス欄・タイトル・履歴・フォルダ別の並び設定はこちら。
    /// <see cref="Load"/> が<b>同期で</b>書き換えるので、中身が届く前から新しい値になっている。
    ///
    /// <para>★ <b>行から実パスを作るのに使ってはいけない</b>（<see cref="EntriesPath"/> を使う）。
    /// 混ぜると二重のパスになり、削除が別のフォルダに当たる（BUG-048）。</para></summary>
    internal string Path { get; private set; } = "";

    /// <summary><b>いま並んでいる行の持ち主。</b><c>_entries</c> と<b>同じ時点で</b>書く
    /// （<see cref="Apply"/> / <see cref="ApplyDrives"/>）。最初の一覧が届くまでは空。
    ///
    /// <para>★ <b>これが <see cref="Path"/> と別に要る理由。</b>列挙は非同期なので、
    /// <c>Path</c> が新しくなってから行が入れ替わるまでに<b>操作が入る</b>。
    /// その隙に「新しいパス＋古い行の名前」を組むと、<c>D:\写真\写真</c> のような
    /// 二重のパスになり、<b><c>Delete</c> が見えていないフォルダの同名ファイルに当たる</b>
    /// （BUG-048。7 か所が同じ組を読んでいた）。
    ///
    /// <b>行・選択・落とし先・右クリック・持ち出しは、すべてこちら。</b></para></summary>
    internal string EntriesPath { get; private set; } = "";

    /// <summary>いま「PC」へ<b>向かっているか</b>（履歴・`CanGoUp` 用）。</summary>
    internal bool IsDrives => IsDrivesPath(Path);

    /// <summary><b>遷移中か</b>——「行が差し替わる予定がある」状態（2026-10-23 のご要望）。
    /// <see cref="Load"/> で true、<see cref="Apply"/> / <see cref="ApplyDrives"/> で false。
    ///
    /// <para>★ <b>終わりは「読み込み完了」ではなく「差し替え完了」。</b>
    /// 列挙が終わった時点ではまだ古い行が並んでいるので、そこで解除すると
    /// <b>利用者が見ている行と、クリックが指す行が食い違う</b>時間が残る
    /// （3000 フォルダの `sub01` ではその隙間が広い・2026-10-23 の実測）。</para>
    ///
    /// <para>★ <b>アイコンの読み込みはここに入れない。</b>入れると BUG-045 の
    /// 「アイコンが約 42 秒出ない」あいだクリックが丸ごと死ぬ。
    /// 行の確定はアイコンより先に終わる（2001 行が 10ms・3000 フォルダで世代が 100ms 以内に進む）
    /// ——<b>待つのは行だけ</b>。</para>
    ///
    /// <para>★ <b>届かないネットワークのパスでは、ここが数十秒 true になりうる</b>
    /// （列挙そのものが返ってこないため・BUG-043 の家族）。そのあいだ一覧の
    /// <b>ダブルクリックだけ</b>効かなくなるが、戻る・アドレス欄・ツリー・タブは効く。
    /// <c>--diag</c> に経過を出すので、起きていれば数字で分かる。</para></summary>
    internal bool Navigating { get; private set; }

    private long _navigateAt;

    /// <summary>遷移が始まってからの経過（ミリ秒）。遷移中でなければ 0。
    /// <b>無視した操作と一緒に記録する</b>ための数字で、判定には使わない。</summary>
    internal long NavigatingMs => Navigating
        ? (System.Diagnostics.Stopwatch.GetTimestamp() - _navigateAt)
            * 1000 / System.Diagnostics.Stopwatch.Frequency
        : 0;

    /// <summary><b>並んでいる行がドライブの一覧か。</b>行を解釈する側はこちらを見る。
    /// <see cref="IsDrives"/> は <see cref="Path"/> から導くので、
    /// 「PC」へ移った瞬間に true になる——まだ普通のフォルダの行が並んでいても。</summary>
    internal bool EntriesAreDrives => IsDrivesPath(EntriesPath);

    private readonly List<DriveRow> _drives = [];

    /// <summary><see cref="Entries"/> と<b>同じ並び</b>のドライブの情報。
    /// 「PC」を開いていないときは空。</summary>
    internal IReadOnlyList<DriveRow> Drives => _drives;

    /// <summary>その行が指す場所。ふつうは「いまのフォルダ＋名前」だが、
    /// 「PC」のときは行ごとにドライブの根を持っている。
    ///
    /// <para><b>行の指す先を聞くのはここ 1 か所にする。</b>
    /// <c>Entry</c> に「本当の行き先」を足すと、ドライブ以外の全部が
    /// 「いつも null」の枝を抱えることになる。</para></summary>
    internal string? PathOf(int index)
    {
        if ((uint)index >= (uint)_entries.Count)
        {
            return null;
        }
        if (EntriesAreDrives)
        {
            return (uint)index < (uint)_drives.Count ? _drives[index].Root : null;
        }
        // ★ EntriesPath。Path を使うと、遷移中の 2 回目のダブルクリックで
        //   「新しいパス＋古い行の名前」になる（BUG-048）
        if (EntriesPath.Length == 0)
        {
            return null; // 最初の一覧が届いていない。行が無いので本来ここへは来ない
        }
        return System.IO.Path.Combine(EntriesPath, _entries[index].Name);
    }
    internal string? ErrorMessage { get; private set; }
    /// <summary>このフォルダで、フォルダを先頭にまとめるか。
    /// 既定はフォルダ先頭だが、ダウンロードだけ混合（2026-09-18 のご要望）。</summary>
    internal bool FoldersFirst { get; private set; } = true;

    /// <summary>その切り替え。<b>フォルダごとに覚える</b>（session に残る）。</summary>
    internal void ToggleFoldersFirst()
    {
        if (EntriesAreDrives || EntriesPath.Length == 0)
        {
            return; // 「PC」には並べ替えが無い
        }
        FoldersFirst = !FoldersFirst;
        // ★ EntriesPath。見ているフォルダの設定を書く（BUG-048）
        Services.FolderSortSettings.Set(EntriesPath, FoldersFirst);
        EntriesChanging?.Invoke();
        Sort();
        EntriesReset?.Invoke(true);
        StateChanged?.Invoke();
    }

    internal SortColumn SortColumn { get; private set; } = SortColumn.Name;
    internal bool SortAscending { get; private set; } = true;

    internal IReadOnlyList<Entry> Entries => _entries;

    internal bool CanGoBack => _historyIndex > 0;
    internal bool CanGoForward => _historyIndex >= 0 && _historyIndex < _history.Count - 1;
    /// <summary>「PC」には親が無い（エクスプローラーと同じ）。</summary>
    internal bool CanGoUp => !IsDrives && System.IO.Path.GetDirectoryName(Path) is not null;

    /// <summary><b>行番号がずれる変更を加える直前</b>に発火する。
    ///
    /// <para>ビュー側が選択を名前で控えるための合図。オーナーデータの一覧は選択を<b>行番号</b>で
    /// 持っているので、変更が済んだ後では「その行番号に今いる項目」＝別のファイルの名前しか
    /// 分からない（BUG-017 の原因はこれだった）。控えるのは必ず変更の<b>前</b>でなければならない。</para></summary>
    internal event Action? EntriesChanging;

    /// <summary>一覧が全面的に入れ替わった（移動・再読込・並べ替え）。
    /// 引数は<b>選択を引き継ぐか</b>。並べ替え・同じフォルダの読み直しでは引き継ぎ、
    /// 別のフォルダへ移動したときは引き継がない（同名のファイルが選ばれてしまうため）。</summary>
    internal event Action<bool>? EntriesReset;

    /// <summary>末尾に 1 件増えた。既存の行番号は動かないので、選択は付け直さなくてよい。</summary>
    internal event Action<int>? EntryAdded;

    /// <summary>1 件消えた（その行以降の行番号がずれる）。</summary>
    internal event Action<int>? EntryRemoved;

    /// <summary>1 件だけ内容が変わった（リネーム。位置は動かない）。</summary>
    internal event Action<int>? EntryUpdated;

    /// <summary>パス・エラー・履歴の可否が変わった。</summary>
    internal event Action? StateChanged;

    internal FileListViewModel(IFileSystemService fs)
    {
        UI.LiveObjects.Track(this, "FileListViewModel");
        _fs = fs;
        // 取りこぼし時の全体読み直しのデバウンス（WinUI 版の DispatcherQueueTimer にあたる）
        _refreshTimer = new System.Threading.Timer(_ => UiDispatcher.Post(RunPendingRefresh),
            null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
    }

    // --- 移動 ---

    /// <summary>履歴に積んで移動する（ダブルクリック・上へ・アドレスバー）。</summary>
    internal void Navigate(string newPath)
    {
        // ★ 同じパスを連続して積まない。遷移中にもう一度ダブルクリックすると
        //   ここへ同じパスで 2 回来る（BUG-048）。積むと「戻る」が 1 回空振りする
        if (_historyIndex >= 0 && _historyIndex < _history.Count
            && string.Equals(_history[_historyIndex], newPath, StringComparison.OrdinalIgnoreCase))
        {
            Load(newPath);
            return;
        }
        if (_historyIndex < _history.Count - 1)
        {
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        }
        _history.Add(newPath);
        _historyIndex = _history.Count - 1;
        Load(newPath);
    }

    internal void GoBack()
    {
        if (CanGoBack)
        {
            _historyIndex--;
            Load(_history[_historyIndex]);
        }
    }

    internal void GoForward()
    {
        if (CanGoForward)
        {
            _historyIndex++;
            Load(_history[_historyIndex]);
        }
    }

    internal void GoUp()
    {
        if (System.IO.Path.GetDirectoryName(Path) is { } parent)
        {
            Navigate(parent);
        }
    }

    /// <summary>表示中フォルダの再読込。並び順は保つ。
    ///
    /// <para><b>「読み直し」はここだけ</b>（<c>F5</c> と、監視の取りこぼしからの全体読み直し）。
    /// アイコンの控えを捨てるのもここだけ——<see cref="Load"/> の <c>isReload</c> 参照。</para></summary>
    internal void Refresh() => Load(Path, resetSort: false, isReload: true);

    // --- タブの切り替え ---
    //
    // ペインは一覧を 1 つだけ持ち、タブを切り替えるたびにここへ載せ替える。
    // タブ側が覚えているのはパス・履歴・並び順だけで、項目の配列は常に 1 枚ぶんしか存在しない
    // （BUG-016 の構造的対処・docs/win32-migration/design.md）。

    /// <summary>いまの状態を、離れるタブに書き戻す。</summary>
    internal void SaveTo(TabModel tab)
    {
        tab.Path = Path;
        tab.History.Clear();
        tab.History.AddRange(_history);
        tab.HistoryIndex = _historyIndex;
        tab.SortColumn = SortColumn;
        tab.SortAscending = SortAscending;
    }

    /// <summary>切り替え先のタブの状態を載せて読み直す。</summary>
    internal void SwitchTo(TabModel tab)
    {
        // 進行中の読み込みを先に無効化する。切り替え前のタブの結果が後から届いて
        // 新しいタブの一覧を上書きするのを防ぐ
        _loadToken++;
        // ★ 無効にした読み込みの Apply はもう来ない。ここで下ろさないと
        //   Navigating が true のまま残り、**一覧が永久に開けなくなる**
        //   （下の Load が成功すれば立て直す）
        Navigating = false;

        _history.Clear();
        _history.AddRange(tab.History);
        _historyIndex = tab.HistoryIndex;
        SortColumn = tab.SortColumn;
        SortAscending = tab.SortAscending;

        if (string.IsNullOrEmpty(tab.Path))
        {
            return;
        }
        // 並び順はタブが覚えているものを使う。選択は引き継がない（別のタブのものだから）
        Load(tab.Path, resetSort: false, keepSelection: false);
    }

    /// <param name="isReload"><b>同じフォルダを読み直したか</b>（<c>F5</c>・監視からの全体読み直し）。
    /// アイコンの控えを捨てるかどうかに使う。
    ///
    /// <para>★ <b>「パスが同じか」で判定してはいけない</b>（2026-10-01 の確認で踏んだ）。
    /// <b>同じパスの別タブへ切り替える</b>のもパスは同じなので、
    /// それを読み直しとみなすと<b>タブを行き来するたびに全部引き直す</b>ことになる
    /// （実測で `裏で引いた=10` が毎回出ていた）。
    /// <b>読み直しは、呼び出し側が「読み直しだ」と言ったときだけ。</b></para></param>
    private void Load(string targetPath, bool resetSort = true, bool? keepSelection = null,
        bool isReload = false)
    {
        if (_disposed || string.IsNullOrEmpty(targetPath))
        {
            return;
        }
        // 同じフォルダの読み直し（追随の取りこぼし・明示的な再読込）なら選択を引き継ぐ。
        // タブの切り替えでは、同じフォルダを開いていても引き継がない（別のタブの選択が移る）
        // ★ この 2 つは別物。どちらも「パスが同じか」では決まらない。
        //   isReload（引数）= 読み直しか。**F5 と監視からの全体読み直しだけ**
        //   _keepSelectionOnReset = 選択を引き継ぐか。パスが同じなら引き継ぐ
        //     （タブの切り替えは同じパスでも false を渡してくるので、そちらが勝つ）
        _keepSelectionOnReset = keepSelection
            ?? string.Equals(Path, targetPath, StringComparison.OrdinalIgnoreCase);
        Path = targetPath;
        // ★ FoldersFirst はここでは触らない（BUG-048）。並べ替えの設定は
        //   **並んでいる行**のものなので、行と同じ時点（Apply）で引き直す。
        //   ここで書くと、遷移中に見出しを右クリックしたとき
        //   「A の行を見ながら B の設定を書き換える」ことになる
        ErrorMessage = null;
        if (resetSort)
        {
            // 並び順はフォルダ単位（移動したら既定の名前昇順に戻す）
            SortColumn = SortColumn.Name;
            SortAscending = true;
        }
        var token = ++_loadToken;
        // ★ ここから「行が差し替わる予定がある」。解除は Apply / ApplyDrives の中
        Navigating = true;
        _navigateAt = System.Diagnostics.Stopwatch.GetTimestamp();
        StateChanged?.Invoke();

        if (string.Equals(targetPath, DrivesPath, StringComparison.Ordinal))
        {
            _ = LoadDrivesAsync(token);
            return;
        }
        _ = LoadCoreAsync(targetPath, token, isReload);
    }

    /// <summary>「PC」の中身。<b>監視は張らない</b>（ドライブの抜き差しは
    /// <c>FileSystemWatcher</c> の守備範囲ではない。F5 で読み直す）。</summary>
    private async Task LoadDrivesAsync(int token)
    {
        try
        {
            var rows = await _fs.ListDrivesAsync().ConfigureAwait(false);
            UiDispatcher.Post(() => ApplyDrives(token, rows));
        }
        catch (Exception ex)
        {
            Diagnostics.Report("LoadDrivesAsync", ex);
        }
    }

    private void ApplyDrives(int token, IReadOnlyList<DriveRow> rows)
    {
        if (_disposed || token != _loadToken)
        {
            return;
        }
        Navigating = false;   // ★ Apply と同じ（差し替えの開始が遷移の終わり）
        EntriesChanging?.Invoke();
        StopWatching();
        _entries.Clear();
        _drives.Clear();
        // ★ 行と同じ時点で持ち主を書く。これより前でも後でもいけない（BUG-048）
        EntriesPath = Path;
        foreach (var row in rows)
        {
            _drives.Add(row);
            // 行そのものは今までどおり Entry。選択・当たり判定・アイコンが
            // 全部これに乗っているので、ここだけ別の型にはしない
            _entries.Add(new Entry(row.Label, IsDirectory: true, row.Total, default, IsHiddenOrSystem: false));
        }
        // ★ 並べ替えない。ドライブ文字の順＝エクスプローラーと同じ並び
        UI.Diagnostics.Write($"[list] PC を開いた ドライブ={_entries.Count} 件");
        // 「PC」では実パスで引かない（IndexOfPath を使う）が、
        // ここも通しておかないと前のフォルダの計測が出るのが 1 回遅れる。
        // 捨てるものが無いので isReload は false でよい
        Services.ShellImageList.BeginFolder(Path, isReload: false);
        EntriesReset?.Invoke(_keepSelectionOnReset);
        StateChanged?.Invoke();
    }

    private async Task LoadCoreAsync(string targetPath, int token, bool isReload)
    {
        try
        {
            // ★ 列挙にかかった時間も出す。アイコンの時間（[icon] の行）と並べないと、
            //   「遅い」の内訳が分からない（2026-09-28・BUG-038）
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var result = await _fs.ListAsync(targetPath).ConfigureAwait(false);
            var elapsed = (System.Diagnostics.Stopwatch.GetTimestamp() - started)
                * 1000 / System.Diagnostics.Stopwatch.Frequency;
            UiDispatcher.Post(() => Apply(targetPath, token, result, elapsed, isReload));
        }
        catch (Exception ex)
        {
            Diagnostics.Report($"LoadCoreAsync({targetPath})", ex);
        }
    }

    private void Apply(string targetPath, int token, ListResult result, long enumerateMs, bool isReload)
    {
        // 読み込んでいる間に別のフォルダへ移っていたら捨てる
        if (_disposed || token != _loadToken)
        {
            return;
        }
        // ★ 差し替えが始まる。ここで遷移は終わり（EntriesReset より前に下ろす——
        //   選択の付け直しは「遷移中の操作」ではない）
        Navigating = false;
        // 選択の控えは、一覧を触る前に取ってもらう
        EntriesChanging?.Invoke();
        _entries.Clear();
        // 「PC」から普通のフォルダへ移ったときに、古い容量が残らないようにする
        _drives.Clear();
        // ★ 行と同じ時点で持ち主を書く。これより前でも後でもいけない（BUG-048）。
        //   読めなかった場合も書く——行が 0 件なので、誰も解釈できない
        EntriesPath = targetPath;
        // ★ 行と同じ時点で引き直す。下の Sort() がこれを見るので、この行より前に置くこと
        FoldersFirst = Services.FolderSortSettings.FoldersFirst(targetPath);
        switch (result)
        {
            case ListOk ok:
                _entries.AddRange(ok.Entries);
                Sort();
                StartWatching(targetPath);
                break;
            case ListError err:
                StopWatching();
                ErrorMessage = err.Kind switch
                {
                    ListErrorKind.AccessDenied => "アクセスが拒否されました",
                    ListErrorKind.NotFound => "パスが見つかりません",
                    _ => $"読み込みに失敗しました: {err.Message}",
                };
                break;
        }
        UI.Diagnostics.Write($"[list] 読み込み結果 {Path} 件数={_entries.Count} "
            + $"列挙={enumerateMs}ms エラー={ErrorMessage ?? "なし"}");
        // ★ アイコンの計測をやり直す。EntriesReset の中で LVM_SETITEMCOUNT が
        //   LVN_GETDISPINFO を呼び返すので、数えるのはそこから
        Services.ShellImageList.BeginFolder(Path, isReload);
        EntriesReset?.Invoke(_keepSelectionOnReset);
        StateChanged?.Invoke();
    }

    // --- 並べ替え ---

    internal void SetSort(SortColumn column)
    {
        // ★ 「PC」では並べ替えない。列がドライブ用に入れ替わっていて、
        //   SortColumn（名前・更新日時・種類・サイズ）とは別物になっている。
        //   ドライブ文字の順がエクスプローラーの並びでもある
        if (EntriesAreDrives)
        {
            return;
        }
        if (SortColumn == column)
        {
            SortAscending = !SortAscending;
        }
        else
        {
            SortColumn = column;
            SortAscending = true;
        }
        EntriesChanging?.Invoke();
        Sort();
        // 並べ替えは同じ項目が並び替わるだけなので、選択は引き継ぐ
        EntriesReset?.Invoke(true);
        StateChanged?.Invoke();
    }

    private void Sort()
    {
        var column = SortColumn;
        var ascending = SortAscending;
        _entries.Sort((a, b) =>
        {
            // フォルダ先頭は並び順によらず維持する（file-list 仕様）。
            // ★ フォルダごとに切れる（ダウンロードは既定で切ってある）
            if (FoldersFirst && a.IsDirectory != b.IsDirectory)
            {
                return a.IsDirectory ? -1 : 1;
            }
            // 名前は自然順（`File2` < `File10`）。エクスプローラーと並び順を揃える
            var byName = NaturalStringComparer.Instance.Compare(a.Name, b.Name);
            var result = column switch
            {
                SortColumn.Modified => a.Modified.CompareTo(b.Modified),
                SortColumn.Type => NaturalStringComparer.Instance.Compare(
                    EntryFormat.TypeLabel(a), EntryFormat.TypeLabel(b)),
                SortColumn.Size => a.Size.CompareTo(b.Size),
                _ => byName,
            };
            if (result == 0)
            {
                // 値が同じときは名前で決着させる。List.Sort は安定ではないので、
                // 決着を付けないと同値の行の順が読み込みのたびに変わる
                return byName;
            }
            return ascending ? result : -result;
        });
    }

    // --- 表示中フォルダの追随（差分更新） ---
    //
    // 一覧全体を読み直すと並べ替えが走り、追加やリネームのたびに行が動いて落ち着かない
    // （2026-08-04 ユーザ要望）。エクスプローラー系のツールと同じく、
    //   * 追加   → 末尾に足すだけ（並べ替えない）
    //   * 削除   → その行を消すだけ
    //   * リネーム → 同じ位置のまま名前を差し替える
    // とし、全体の読み直しは「移動・明示的な再読込・通知の取りこぼし」だけに限定する。
    // 内容の変化（サイズ・更新日時）は監視対象から外す。ホームのように常時書き込みが
    // 起きるフォルダで、勝手に更新がかかり続けるのを防ぐため。

    private void StartWatching(string path)
    {
        if (_disposed || string.IsNullOrEmpty(path))
        {
            StopWatching();
            return;
        }
        // 再読込のたびに張り替えると、その隙間の変更を取りこぼす。同じフォルダなら使い回す
        if (_watcher is { } current && string.Equals(current.Path, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        StopWatching();
        try
        {
            var watcher = new FileSystemWatcher(path)
            {
                // 項目の増減・改名と、属性の変化のみ。サイズ・更新日時では動かさない。
                // 属性を見るのは、隠し・システムの薄色表示を実態に合わせるため
                // （行は動かさないので、並べ替えが走る心配は無い）
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Attributes,
                IncludeSubdirectories = false,
            };
            // 通知はワーカースレッドで届く。項目情報の取得（ディスクアクセス）はここで済ませ、
            // UI スレッドでは一覧の書き換えだけを行う。
            // e.Name は理屈上 null になりうる（パスが長すぎる等）。名前が分からない通知は
            // 個別には扱えないので、全体の読み直しに倒す
            watcher.Created += (_, e) =>
            {
                if (e.Name is not { } name)
                {
                    ApplyWatched(ScheduleFullRefresh);
                    return;
                }
                var entry = ReadEntry(path, name);
                ApplyWatched(() => AddEntry(path, entry));
            };
            watcher.Deleted += (_, e) =>
            {
                if (e.Name is not { } name)
                {
                    ApplyWatched(ScheduleFullRefresh);
                    return;
                }
                ApplyWatched(() => RemoveEntry(path, name));
            };
            watcher.Renamed += (_, e) =>
            {
                if (e.Name is not { } name || e.OldName is not { } oldName)
                {
                    ApplyWatched(ScheduleFullRefresh);
                    return;
                }
                var entry = ReadEntry(path, name);
                ApplyWatched(() => RenameEntry(path, oldName, entry));
            };
            watcher.Changed += (_, e) =>
            {
                if (e.Name is not { } name)
                {
                    return; // 取りこぼしても薄色かどうかがずれるだけなので、読み直しはしない
                }
                var entry = ReadEntry(path, name);
                ApplyWatched(() => UpdateAttributes(path, name, entry));
            };
            // バッファ溢れ等で個別通知を落としたときは、まとめて読み直せば整合する
            watcher.Error += (_, _) => ApplyWatched(ScheduleFullRefresh);
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch
        {
            // 監視できないパス（ネットワーク・権限なし・直後に消えた等）は自動更新なしで続行する
            _watcher = null;
        }
    }

    /// <summary>フォルダ監視が動いているか。監視できないパスでは、操作後に自分で読み直す必要がある。</summary>
    internal bool IsWatching => _watcher is not null;

    // --- 自動追随の一時停止（インライン リネームの間） ---
    //
    // 編集中に一覧が作り直されると、編集ボックスごと消える。止めている間の変更は
    // 貯めておき、戻したときにまとめて適用する（貯めすぎるくらいなら読み直した方が安い）。

    private int _autoRefreshSuspendCount;
    private bool _refreshPending;
    private readonly List<Action> _deferred = [];
    private const int MaxDeferredChanges = 200;

    internal void SuspendAutoRefresh() => _autoRefreshSuspendCount++;

    internal void ResumeAutoRefresh()
    {
        if (_autoRefreshSuspendCount == 0 || --_autoRefreshSuspendCount > 0)
        {
            return;
        }
        if (_refreshPending)
        {
            _refreshPending = false;
            _deferred.Clear();
            ScheduleFullRefresh();
            return;
        }
        var pending = _deferred.ToArray();
        _deferred.Clear();
        foreach (var action in pending)
        {
            action();
        }
    }

    /// <summary>監視からの変更を UI スレッドで適用する。止めている間は貯める。
    /// 監視の通知はすべてここを通すこと（直接 <see cref="UiDispatcher.Post"/> しない）。</summary>
    private void ApplyWatched(Action action) => UiDispatcher.Post(() =>
    {
        if (_disposed)
        {
            return;
        }
        if (_autoRefreshSuspendCount > 0)
        {
            if (_deferred.Count >= MaxDeferredChanges)
            {
                _refreshPending = true;
            }
            else
            {
                _deferred.Add(action);
            }
            return;
        }
        action();
    });

    private void StopWatching()
    {
        if (_watcher is { } watcher)
        {
            _watcher = null;
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            catch
            {
                // 破棄時の失敗は無視（監視は既に止まっている）
            }
        }
    }

    /// <summary>通知が届いた時点と、それを反映する時点で表示フォルダが変わっていないか。
    ///
    /// <para>★ <b>比べるのは <see cref="EntriesPath"/>。</b>ここが守るのは
    /// 「<c>_entries</c> を触ってよいか」で、<c>_entries</c> の持ち主は
    /// <c>EntriesPath</c> である。<c>Path</c> と比べると、遷移中に届いた
    /// <b>まだ並んでいるフォルダ</b>の通知を取りこぼす（BUG-048 と同じ組）。</para></summary>
    private bool StillShowing(string folder) =>
        !_disposed && string.Equals(folder, EntriesPath, StringComparison.OrdinalIgnoreCase);

    private void AddEntry(string folder, Entry? entry)
    {
        if (entry is null || !StillShowing(folder) || IndexOf(entry.Name) >= 0)
        {
            return;
        }
        _entries.Add(entry);
        EntryAdded?.Invoke(_entries.Count - 1);
    }

    private void RemoveEntry(string folder, string name)
    {
        if (!StillShowing(folder))
        {
            return;
        }
        var index = IndexOf(name);
        if (index < 0)
        {
            return;
        }
        // 削除は以降の行番号をずらすので、控えは消す前に取ってもらう
        EntriesChanging?.Invoke();
        _entries.RemoveAt(index);
        EntryRemoved?.Invoke(index);
    }

    /// <summary>リネームされた項目を同じ位置のまま差し替える（行を動かさない）。</summary>
    private void RenameEntry(string folder, string oldName, Entry? entry)
    {
        if (!StillShowing(folder))
        {
            return;
        }
        var index = IndexOf(oldName);
        if (index < 0)
        {
            AddEntry(folder, entry); // 元が一覧に無いなら追加として扱う
            return;
        }
        if (entry is null)
        {
            RemoveEntry(folder, oldName); // 改名先が読めない（移動された等）
            return;
        }
        _entries[index] = entry;
        EntryUpdated?.Invoke(index);
    }

    /// <summary>属性が変わった項目を、同じ位置のまま差し替える（薄色表示を実態に合わせる）。
    /// <b>薄色かどうかが変わったときだけ</b>反映する。ファイルへの書き込みでも
    /// アーカイブ属性が立って通知が来るので、そのたびに描き直さないための足切り。</summary>
    private void UpdateAttributes(string folder, string name, Entry? entry)
    {
        if (entry is null || !StillShowing(folder))
        {
            return;
        }
        var index = IndexOf(name);
        if (index < 0 || _entries[index].IsHiddenOrSystem == entry.IsHiddenOrSystem)
        {
            return;
        }
        _entries[index] = entry;
        EntryUpdated?.Invoke(index);
    }

    private int IndexOf(string name)
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            if (string.Equals(_entries[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>1 項目分の情報を取り直す。取れなければ null（消えた・アクセス不可）。
    /// 監視スレッドから呼ぶ（ディスクアクセスを UI スレッドに持ち込まない）。</summary>
    private static Entry? ReadEntry(string folder, string name)
    {
        try
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }
            var full = System.IO.Path.Combine(folder, name);
            if (Directory.Exists(full))
            {
                var info = new DirectoryInfo(full);
                return new Entry(info.Name, true, 0L, info.LastWriteTime, IsHiddenOrSystem(info.Attributes));
            }
            if (File.Exists(full))
            {
                var info = new FileInfo(full);
                return new Entry(info.Name, false, info.Length, info.LastWriteTime, IsHiddenOrSystem(info.Attributes));
            }
        }
        catch
        {
            // 取得できないものは一覧に出さない
        }
        return null;
    }

    private static bool IsHiddenOrSystem(FileAttributes attributes) =>
        (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;

    /// <summary>取りこぼし時などの全体読み直し（デバウンス付き）。UI スレッドから呼ぶ。</summary>
    private void ScheduleFullRefresh()
    {
        if (_disposed || _refreshScheduled)
        {
            return;
        }
        _refreshScheduled = true;
        _refreshTimer.Change(400, System.Threading.Timeout.Infinite);
    }

    private void RunPendingRefresh()
    {
        _refreshScheduled = false;
        if (!_disposed && !string.IsNullOrEmpty(Path))
        {
            Refresh();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _refreshTimer.Dispose();
        StopWatching();
    }
}
