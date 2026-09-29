using ExtendExprorer.Models;
using static ExtendExprorer.Interop.NativeMethods;

namespace ExtendExprorer.Services;

/// <summary>OS のシステムイメージリストと、その中の番号（アイコンのインデックス）を引く仕組み。
///
/// <para><b>移行の中心となる判断</b>（<c>docs/win32-migration/design.md</c>）。現行 WinUI 版は
/// 項目ごとに <c>HICON</c> を取り出して <c>WriteableBitmap</c> に変換し、ビットマップを抱えていた。
/// ここでは画像の実体を OS 側に置いたまま、コントロールにイメージリストを<b>借りて</b>渡し、
/// 項目ごとに持つのは <c>int</c> ひとつにする。</para>
///
/// <para><b>引き方は拡張子単位でキャッシュする。</b><c>SHGFI_USEFILEATTRIBUTES</c> を付けると
/// 実在しないパスでも「その拡張子の既定アイコン」を返してくれるので、ディスクに触らずに済む。
/// 1 万件のフォルダでも呼ぶのは拡張子の種類の数だけになる。
/// 例外は <see cref="PerFileExtensions"/>（実行ファイルやショートカットなど、
/// ファイルごとに固有のアイコンを持つもの）で、これだけ実パスで引く。</para></summary>
internal static unsafe class ShellImageList
{
    /// <summary>ファイルごとに固有のアイコンを持つ拡張子。ここだけ実パスで引く
    /// （ディスクに触るので、それ以外は拡張子キャッシュで済ませる）。</summary>
    private static readonly HashSet<string> PerFileExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".exe", ".lnk", ".ico", ".cur", ".ani", ".scr", ".msc", ".cpl", ".url" };

    private static readonly Dictionary<string, int> ByExtension = new(StringComparer.OrdinalIgnoreCase);

    private static nint _handle;
    private static int _folderIndex = -1;
    private static int _fileIndex = -1;

    /// <summary>システムイメージリスト（小アイコン）のハンドル。
    /// <b>破棄してはいけない</b>（OS の共有物なので <c>LVS_SHAREIMAGELISTS</c> で借りる）。</summary>
    internal static nint Handle
    {
        get
        {
            if (_handle == 0)
            {
                var info = default(SHFILEINFOW);
                _handle = QueryIcon("folder", FILE_ATTRIBUTE_DIRECTORY, ref info,
                    SHGFI_SYSICONINDEX | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);
                _folderIndex = _handle != 0 ? info.iIcon : -1;
            }
            return _handle;
        }
    }

    /// <summary>この項目のアイコン番号。取れないときはフォルダ／ファイルの既定に落とす。</summary>
    internal static int IndexOf(string folderPath, Entry entry)
    {
        try
        {
            if (entry.IsDirectory)
            {
                return FolderIndex;
            }
            var extension = System.IO.Path.GetExtension(entry.Name);
            if (extension.Length == 0)
            {
                return FileIndex;
            }
            if (PerFileExtensions.Contains(extension))
            {
                return PerFileIndex(folderPath, entry, extension);
            }
            return ExtensionIndex(extension);
        }
        catch (Exception ex)
        {
            // アイコンが出ないことより、一覧が描けないことの方が困る
            UI.Diagnostics.Report($"ShellImageList.IndexOf({entry.Name})", ex);
            return entry.IsDirectory ? FolderIndex : FileIndex;
        }
    }

    // --- ファイルごとに固有のアイコンを持つもの（.exe など）---
    //
    // ★ ここだけディスクを読む。**しかも一覧が描かれるたびに聞かれる**——
    //   IndexOf を呼んでいるのは LVN_GETDISPINFO（オーナーデータ）で、
    //   スクロール・選択・再描画のたびに同じ行をもう一度聞いてくる。
    //   覚えずにいると、インストーラーだけが入ったフォルダのように
    //   .exe が並ぶ場所で、描くたびに全部の PE を開き直すことになる
    //   （2026-09-28 のご報告・BUG-038）。

    /// <summary>実パスで引いた結果。<b>取れなかったときの落とし先も覚える</b>——
    /// 覚えないと、読めないファイル（ロック中・検疫済み）が
    /// <b>描画のたびに毎回ディスクを叩く</b>ことになり、うまくいく場合より重くなる。</summary>
    private static readonly Dictionary<string, int> ByFile = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>覚えておく上限。越えたら捨てて作り直す。
    /// メモリ要件（30MB 前後）があるので、無制限には持たない。</summary>
    private const int MaxFileCache = 2048;

    /// <summary>直前に書き出した計測がどのフォルダのものか（<see cref="Flush"/> の行に出すだけ）。
    ///
    /// <para><b>再読込の判定にはもう使わない。</b>ここは static の 1 枠しかないのに
    /// <see cref="BeginFolder"/> は<b>ペインごとに</b>呼ばれるので、
    /// 2 ペインで交互に使うと「同じフォルダを読み直した」が成り立たなくなっていた
    /// （A が C:\X → B が C:\Y → A で F5、で A の控えが捨てられない）。
    /// <b>判定は呼び出し側から渡してもらう。</b></para></summary>
    private static string _folder = "";

    // 計測（--diag のときだけ出す）。「アイコンのせいなのか」を数字で切り分けるため
    private static int _diskCount;
    private static int _cacheHits;
    private static long _diskTicks;
    private static long _slowestTicks;
    private static string _slowestName = "";

    /// <summary>フォルダを読み込む直前に呼ぶ。
    /// <b>前のフォルダの計測を書き出してから</b>やり直し、
    /// <b>同じフォルダの再読込なら控えを捨てる</b>（差し替えられたファイルの絵が古いままにならないように）。</summary>
    /// <param name="isReload"><b>同じフォルダをもう一度読み込んだか。</b>
    /// 呼び出し側（<c>FileListViewModel.Load</c>）だけが正しく知っている——
    /// タブの切り替えは「同じパスだが別のタブ」なので、
    /// 選択を引き継ぐかどうか（<c>_keepSelectionOnReset</c>）とは別の判定になる。</param>
    internal static void BeginFolder(string path, bool isReload)
    {
        // ★ ここで前のぶんを出す。読み込みの直後に出そうとすると 0 になる——
        //   LVN_GETDISPINFO は WM_PAINT からも来るので、**山は読み込みを抜けたあと**に立つ。
        //   「0 件」と書いてしまうと、速かったのか数えていなかったのか区別が付かない
        //   （この取り違えは docs/win32-migration/dev-notes.md に何度も出てくる形）
        Flush("移動したので");

        if (isReload)
        {
            DropFolder(path);
        }
        _folder = path;
    }

    /// <summary>そのフォルダぶんの控えを捨てる。</summary>
    private static void DropFolder(string path)
    {
        if (ByFile.Count == 0)
        {
            return;
        }
        var prefix = path.EndsWith('\\') ? path : path + '\\';
        foreach (var key in ByFile.Keys.Where(
                     k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            ByFile.Remove(key);
        }
    }

    /// <summary><b>ディスクを読んだ回数と時間</b>を 1 行残して、数えるのをやり直す。
    ///
    /// <para><b>何もしていなければ書かない。</b>毎回出すと、
    /// 「アイコンが重い」ときだけ目に入る、という読み方ができなくなる。</para></summary>
    private static void Flush(string why)
    {
        if (_diskCount == 0 && _cacheHits == 0)
        {
            return;
        }
        UI.Diagnostics.Write(
            $"[icon] {_folder} {why} 裏で引いた={_diskCount} 件 合計={Ms(_diskTicks)}ms"
            + (_slowestTicks > 0 ? $" 最長={Ms(_slowestTicks)}ms（{_slowestName}）" : "")
            + $" 覚えていた={_cacheHits} 件 控え={ByFile.Count} 件");
        _diskCount = 0;
        _cacheHits = 0;
        _diskTicks = 0;
        _slowestTicks = 0;
        _slowestName = "";
    }

    private static long Ms(long ticks) => ticks * 1000 / System.Diagnostics.Stopwatch.Frequency;

    /// <summary>ファイルごとの絵は<b>ここで待たない</b>。
    ///
    /// <para><b>その場では拡張子の既定アイコンを返し、実パスは裏で引く</b>（2026-09-28・BUG-038 第 2 段）。
    /// この関数を呼んでいるのは <c>LVN_GETDISPINFO</c>——<b>UI スレッドの `WM_PAINT` の中</b>なので、
    /// ここでディスクを待つと<b>一覧が出るまで窓が固まる</b>。
    /// <c>.exe</c> が数百件並ぶフォルダで「移動が極端に遅い」のはこれだった。</para>
    ///
    /// <para><b>2 回目が速いのは OS が覚えているから</b>で、こちらの控えのおかげではない
    /// （利用者の追加報告・2026-09-28）。だから<b>直すべきは初回で、やり方は「待たない」</b>。</para></summary>
    private static int PerFileIndex(string folderPath, Entry entry, string extension)
    {
        var full = System.IO.Path.Combine(folderPath, entry.Name);
        if (ByFile.TryGetValue(full, out var remembered))
        {
            _cacheHits++;
            return remembered;
        }
        // まだ引けていない。**裏に頼んで、いまは拡張子の既定を返す**
        Request(full);
        return ExtensionIndex(extension);
    }

    // --- 実パスを引く 1 本のスレッド（ShellNamespace と同じ作り）---
    //
    // ★ UI スレッドから引かない。ここが BUG-038 第 2 段の本体。
    //   結果は UI スレッドへ戻してから ByFile へ入れる（辞書を 2 つのスレッドで触らない）。

    private static readonly object Gate = new();

    /// <summary>順番待ち。<b>同じパスを二重に頼まない</b>ための集合も持つ。</summary>
    private static readonly Queue<string> Pending = new();
    private static readonly HashSet<string> Queued = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>引き終わったもの（UI スレッドが取りに来る）。</summary>
    private static readonly List<(string Path, int Index, long Ticks)> Done = [];

    private static Thread? _worker;
    private static bool _postPending;

    /// <summary>順番待ちの上限。越えたぶんは捨てる——<b>次に描かれるときにまた頼まれる</b>ので、
    /// 取りこぼしにはならない（<c>LVN_GETDISPINFO</c> は毎回聞いてくる）。</summary>
    private const int MaxPending = 4096;

    /// <summary>絵が引けて、描き直す価値が出た。<b>見えている一覧が自分を無効化する</b>。
    ///
    /// <para>ペインごとに一覧があるので、購読者は複数になる。</para></summary>
    internal static event Action? Resolved;

    private static void Request(string full)
    {
        lock (Gate)
        {
            if (Pending.Count >= MaxPending || !Queued.Add(full))
            {
                return;
            }
            Pending.Enqueue(full);
            if (_worker is null)
            {
                // ShellNamespace と同じ——前面に出ない裏方で、止める仕組みは持たせない
                // （引き終わらないまま終了することがあり、「止まるまで待つ」を入れると
                //   終了が返ってこなくなる）
                _worker = new Thread(Run) { IsBackground = true, Name = "ShellIcons" };
                _worker.Start();
            }
            Monitor.Pulse(Gate);
        }
    }

    private static void Run()
    {
        // ★ ここで一度だけ。以後このスレッドは MTA として振る舞う
        //   （ポンプの無い STA は行き詰まりの罠。ShellNamespace と同じ理由）
        var hr = CoInitializeEx(0, COINIT_MULTITHREADED);
        UI.Diagnostics.Write($"[icon] 引くスレッド開始 CoInitializeEx=0x{hr:X8}");
        while (true)
        {
            string path;
            lock (Gate)
            {
                while (Pending.Count == 0)
                {
                    Monitor.Wait(Gate);
                }
                path = Pending.Dequeue();
            }

            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var index = -1;
            try
            {
                var info = default(SHFILEINFOW);
                if (QueryIcon(path, 0, ref info, SHGFI_SYSICONINDEX | SHGFI_SMALLICON) != 0)
                {
                    index = info.iIcon;
                }
            }
            catch (Exception ex)
            {
                // 引けないことより、スレッドが死ぬことの方が困る
                UI.Diagnostics.Report($"ShellImageList.Run({path})", ex);
            }
            var elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - started;

            bool post;
            lock (Gate)
            {
                Queued.Remove(path);
                Done.Add((path, index, elapsed));
                // ★ 投げるのは 1 回だけ。1 件ごとに投げると、
                //   数百件のフォルダで UI の行列が結果で埋まる
                post = !_postPending;
                _postPending = true;
            }
            if (post && !UI.UiDispatcher.Post(Apply))
            {
                // ★ 起こせなかったら掛け金を戻す。戻さないと、
                //   **このセッションでは二度と絵が反映されない**（下ろすのは Apply だけ）。
                //   行列自体は失われないので、次の 1 件で投げ直せば拾える
                lock (Gate)
                {
                    _postPending = false;
                }
            }
        }
    }

    /// <summary>引けたものを控えに入れて、一覧に描き直してもらう。<b>UI スレッド。</b></summary>
    private static void Apply()
    {
        List<(string Path, int Index, long Ticks)> batch;
        lock (Gate)
        {
            _postPending = false;
            if (Done.Count == 0)
            {
                return;
            }
            batch = [.. Done];
            Done.Clear();
        }

        foreach (var (path, index, ticks) in batch)
        {
            _diskCount++;
            _diskTicks += ticks;
            if (ticks > _slowestTicks)
            {
                _slowestTicks = ticks;
                _slowestName = System.IO.Path.GetFileName(path);
            }
            // 取れなければ拡張子の既定に落とす。★ その結果も覚える（上の ByFile のコメント）
            var resolved = index >= 0
                ? index
                : ExtensionIndex(System.IO.Path.GetExtension(path));
            if (ByFile.Count >= MaxFileCache)
            {
                ByFile.Clear();
            }
            ByFile[path] = resolved;
        }
        Resolved?.Invoke();
    }

    /// <summary>拡張子の既定アイコン（ディスクには触らない）。</summary>
    private static int ExtensionIndex(string extension)
    {
        if (ByExtension.TryGetValue(extension, out var cached))
        {
            return cached;
        }
        var index = QueryByAttributes("x" + extension, FILE_ATTRIBUTE_NORMAL);
        if (index < 0)
        {
            index = FileIndex;
        }
        ByExtension[extension] = index;
        return index;
    }

    /// <summary>実在するパスのアイコン番号（ツリーのルート＝ホーム・ドライブ用）。
    /// ドライブは種類ごとに絵が違い、ホームにも固有の絵があるので、ここだけは実パスで引く。
    /// 数が少なく（ドライブの台数＋1）増えないので、そのまま覚えておく。</summary>
    internal static int IndexOfPath(string path)
    {
        try
        {
            if (ByPath.TryGetValue(path, out var cached))
            {
                return cached;
            }
            var info = default(SHFILEINFOW);
            var index = QueryIcon(path, 0, ref info, SHGFI_SYSICONINDEX | SHGFI_SMALLICON) != 0
                ? info.iIcon
                : FolderIndex;
            // ★ 上限を付ける。doc の「数が少なく増えない」という前提は、
            //   2026-09-18 にタブ帯が呼ぶようになった時点で崩れていた——
            //   **開いたフォルダの種類だけ単調に増える**（30MB 要件のあるアプリで
            //   無制限の辞書はここだけだった）。ByFile と同じ扱いに揃える
            if (ByPath.Count >= MaxFileCache)
            {
                ByPath.Clear();
            }
            ByPath[path] = index;
            return index;
        }
        catch (Exception ex)
        {
            UI.Diagnostics.Report($"ShellImageList.IndexOfPath({path})", ex);
            return FolderIndex;
        }
    }

    private static readonly Dictionary<string, int> ByPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>「PC」の絵。パスが無いので <see cref="IndexOfPath"/> では引けない。
    /// 解析名から PIDL を作って聞く。1 度だけ引いて覚える。</summary>
    internal static int DrivesRoot
    {
        get
        {
            if (_drivesRoot >= 0)
            {
                return _drivesRoot;
            }
            _drivesRoot = FolderIndex;
            try
            {
                if (Interop.NativeMethods.SHParseDisplayName(
                        "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", 0, out var pidl, 0, out _) >= 0
                    && pidl != 0)
                {
                    var info = default(SHFILEINFOW);
                    if (Interop.NativeMethods.SHGetFileInfoPidl(pidl, 0, ref info,
                            (uint)sizeof(SHFILEINFOW), SHGFI_PIDL | SHGFI_SYSICONINDEX | SHGFI_SMALLICON) != 0)
                    {
                        _drivesRoot = info.iIcon;
                    }
                    Interop.NativeMethods.CoTaskMemFree(pidl);
                }
            }
            catch (Exception ex)
            {
                UI.Diagnostics.Report("ShellImageList.DrivesRoot", ex);
            }
            return _drivesRoot;
        }
    }

    private static int _drivesRoot = -1;

    /// <summary>ふつうのフォルダの番号。ツリーの枝はすべてこれを使う。</summary>
    internal static int Folder => FolderIndex;

    private static int FolderIndex
    {
        get
        {
            _ = Handle; // 初回にフォルダの番号も一緒に取れている
            return _folderIndex;
        }
    }

    private static int FileIndex
    {
        get
        {
            if (_fileIndex < 0)
            {
                _fileIndex = QueryByAttributes("file", FILE_ATTRIBUTE_NORMAL);
            }
            return _fileIndex;
        }
    }

    /// <summary>実在しないパスでも「その属性・拡張子の既定アイコン」を返してもらう
    /// （<c>SHGFI_USEFILEATTRIBUTES</c>）。ディスクには触らない。</summary>
    private static int QueryByAttributes(string pseudoPath, uint attributes)
    {
        var info = default(SHFILEINFOW);
        return QueryIcon(pseudoPath, attributes, ref info,
            SHGFI_SYSICONINDEX | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES) != 0
            ? info.iIcon
            : -1;
    }

    private static nint QueryIcon(string path, uint attributes, ref SHFILEINFOW info, uint flags) =>
        SHGetFileInfoW(path, attributes, ref info, (uint)sizeof(SHFILEINFOW), flags);
}
