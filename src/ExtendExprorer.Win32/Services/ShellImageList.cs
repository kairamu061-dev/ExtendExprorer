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

    /// <summary>いま開いているフォルダ。<b>同じフォルダをもう一度読み込んだ（＝再読込）</b>ときだけ
    /// そのフォルダの控えを捨てるための目印。別のフォルダへ移るときは捨てない
    /// （行って戻るのが速い方が嬉しい）。</summary>
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
    internal static void BeginFolder(string path)
    {
        // ★ ここで前のぶんを出す。読み込みの直後に出そうとすると 0 になる——
        //   LVN_GETDISPINFO は WM_PAINT からも来るので、**山は読み込みを抜けたあと**に立つ。
        //   「0 件」と書いてしまうと、速かったのか数えていなかったのか区別が付かない
        //   （この取り違えは docs/win32-migration/dev-notes.md に何度も出てくる形）
        Flush("移動したので");

        if (string.Equals(_folder, path, StringComparison.OrdinalIgnoreCase))
        {
            DropFolder(path);
        }
        _folder = path;
        _reportedSlow = false;
    }

    /// <summary>重いあいだに 1 度だけ出す境目。
    /// <b>ここを越えたらその場で書く</b>——次に移動するまで待たせない。</summary>
    private static readonly long SlowTicks = System.Diagnostics.Stopwatch.Frequency * 300 / 1000;

    private static bool _reportedSlow;

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
            $"[icon] {_folder} {why} 実パスで引いた={_diskCount} 件 合計={Ms(_diskTicks)}ms"
            + (_slowestTicks > 0 ? $" 最長={Ms(_slowestTicks)}ms（{_slowestName}）" : "")
            + $" 覚えていた={_cacheHits} 件 控え={ByFile.Count} 件");
        _diskCount = 0;
        _cacheHits = 0;
        _diskTicks = 0;
        _slowestTicks = 0;
        _slowestName = "";
    }

    private static long Ms(long ticks) => ticks * 1000 / System.Diagnostics.Stopwatch.Frequency;

    private static int PerFileIndex(string folderPath, Entry entry, string extension)
    {
        var full = System.IO.Path.Combine(folderPath, entry.Name);
        if (ByFile.TryGetValue(full, out var remembered))
        {
            _cacheHits++;
            return remembered;
        }

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var info = default(SHFILEINFOW);
        var got = QueryIcon(full, 0, ref info, SHGFI_SYSICONINDEX | SHGFI_SMALLICON) != 0;
        var elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - started;

        _diskCount++;
        _diskTicks += elapsed;
        if (elapsed > _slowestTicks)
        {
            _slowestTicks = elapsed;
            _slowestName = entry.Name;
        }
        // 重いと分かった時点で 1 度書く。移動するまで黙っていると、
        // **待たされている最中に何が起きているか**が分からない
        if (!_reportedSlow && _diskTicks > SlowTicks)
        {
            _reportedSlow = true;
            Flush("途中まで（重い）");
        }

        // 取れなければ拡張子の既定に落とす。★ その結果も覚える（上の ByFile のコメント）
        var index = got ? info.iIcon : ExtensionIndex(extension);
        if (ByFile.Count >= MaxFileCache)
        {
            ByFile.Clear();
        }
        ByFile[full] = index;
        return index;
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
