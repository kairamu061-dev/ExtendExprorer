using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using ExtendExprorer.Interop;
using ExtendExprorer.UI;
using static ExtendExprorer.Interop.NativeMethods;

namespace ExtendExprorer.Services;

/// <summary>シェルの名前空間（PIDL）を歩く。フォルダツリーの根を
/// エクスプローラーのナビゲーションウィンドウと同じ顔ぶれにするために足した（2026-09-13）。
///
/// <para><b>専用のスレッド 1 本で回す。</b><c>ネットワーク</c> の列挙は返ってこないことがあるので
/// UI スレッドでは回せない。スレッドプールを使わないのは<b>取り消しのため</b>——
/// 40 秒返ってこない列挙を抱えたまま、ツリーが畳まれたりペインが閉じたりする。
/// 順番待ちの列にしておけば、消えたノードの結果を捨てられる。</para>
///
/// <para><b>MTA にする。</b>ポンプの無い STA はデッドロックの温床で、ポンプを書くと
/// それ自体が検証の対象になる。シェルのフォルダは要るときに自分で marshaling する。</para>
///
/// <para><b>スレッドをまたぐのは PIDL と素のデータだけ。</b>インターフェイスのポインタは
/// 渡さない。PIDL は素の記憶なので、どのスレッドで作っても同じように読める。</para></summary>
internal static unsafe class ShellNamespace
{
    /// <summary>ノード 1 つぶんの素のデータ。<b>ここに COM のポインタは入れない。</b></summary>
    internal sealed class Item
    {
        /// <summary>絶対 PIDL。<b>受け取った側が持ち主</b>になる（解放の責任も移る）。</summary>
        internal required nint Pidl { get; init; }

        internal required string Name { get; init; }

        /// <summary>ファイルシステム上のパス。<c>PC</c> や <c>ネットワーク</c> は null。</summary>
        internal required string? Path { get; init; }

        /// <summary>展開できる子がいるか（シェブロンを出すか）。</summary>
        internal required bool HasChildren { get; init; }

        internal required bool IsHidden { get; init; }

        internal required int Icon { get; init; }

        /// <summary>これが「PC」か。<b>名前で見ない</b>（言語で変わる）。
        /// シェルに解析名を聞いて、クラス ID で見分ける。</summary>
        internal required bool IsThisPc { get; init; }

        /// <summary>デスクトップから見た解析名（<c>::{CLSID}</c> か実パス）。
        /// 言語に依らない見分け方。診断にも出す。</summary>
        internal required string? ParsingName { get; init; }
    }

    private static readonly StrategyBasedComWrappers ComWrappers = new();

    private static readonly Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");

    /// <summary>「PC」の解析名。表示名は言語で変わるが、これは変わらない。</summary>
    private const string ThisPcParsingName = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";

    /// <summary>根に出さないもの。<b>デスクトップの子ではあるが、エクスプローラーの
    /// ナビゲーションウィンドウは出していない</b>ものを、解析名で名指しで外す（2026-09-14 実測）。
    ///
    /// <para><b>なぜ名指しか。</b>シェルには <c>System.IsPinnedToNameSpaceTree</c> という
    /// 「ナビゲーションウィンドウに出す」印があるが、<b>こちらの環境では
    /// プロパティキーも、値が無いときの意味も確かめられない</b>。
    /// 外したときに空振りするより、**確実に外れて、外した理由が読める**方を選んだ。
    /// 解析名は言語でも Windows の版でも変わらないので、表示名で見るより堅い。</para>
    ///
    /// <para>根でだけ効かせる。**これらを開けなくするわけではない**
    /// （`PC` の下のドライブ等、別の道からは今までどおり辿れる）。</para></summary>
    private static readonly string[] HiddenAtRoot =
    [
        "::{645FF040-5081-101B-9F08-00AA002F954E}", // ごみ箱
        "::{26EE0668-A00A-44D7-9371-BEB064C98683}", // コントロール パネル（カテゴリ）
        "::{21EC2020-3AEA-1069-A2DD-08002B30309D}", // コントロール パネル（すべての項目）
        "::{031E4825-7B94-4DC3-B131-E946B44C8DD5}", // ライブラリ
    ];

    /// <summary>利用者のプロファイルフォルダ。根には出さない
    /// （エクスプローラーも出していない。中身は「ダウンロード」等で個別に出ている）。</summary>
    private static readonly string UserProfile =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>根に出さないものか。<paramref name="parent"/> が 0（＝根）のときだけ効く。</summary>
    private static bool IsHiddenAtRoot(Item item)
    {
        if (item.ParsingName is { Length: > 0 } parsing)
        {
            foreach (var clsid in HiddenAtRoot)
            {
                if (parsing.Equals(clsid, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        return item.Path is { Length: > 0 } path
            && UserProfile.Length > 0
            && path.Equals(UserProfile, StringComparison.OrdinalIgnoreCase);
    }

    // --- PIDL の出入りを数える（漏れを数字で見るため） ---

    private static int _taken;
    private static int _freed;

    /// <summary>いま持っている PIDL の数と、これまでに解放した数。
    /// <b><c>Ctrl+Shift+G</c> はマネージドしか数えないので、ここは自分で数える。</b></summary>
    internal static (int Held, int Freed) PidlCounters => (Volatile.Read(ref _taken) - Volatile.Read(ref _freed), Volatile.Read(ref _freed));

    /// <summary>PIDL を 1 つ返す。<b>ノードを捨てる経路すべてから呼ぶこと。</b></summary>
    internal static void Free(nint pidl)
    {
        if (pidl == 0)
        {
            return;
        }
        CoTaskMemFree(pidl);
        Interlocked.Increment(ref _freed);
    }

    // --- 順番待ちの列と、それを回す 1 本のスレッド ---

    private static readonly object Gate = new();
    private static readonly Queue<(nint Parent, Action<List<Item>> Done)> Queue = new();
    private static Thread? _worker;

    /// <summary><paramref name="parent"/> の子を列挙する。<c>0</c> ならデスクトップ（＝根）。
    ///
    /// <para>★ <paramref name="parent"/> は<b>その場で複製して渡す</b>。借りたまま渡すと、
    /// <b>列挙している最中に持ち主が解放しうる</b>——ツリーを壊すのは <c>WM_DESTROY</c> だが、
    /// このスレッドはそのあとも少しの間動いている。解放済みの PIDL をシェルに渡すと、
    /// 読めないところで落ちる（BUG-029 で踏んだのと同じ種類の壊れ方）。</para>
    ///
    /// <para><paramref name="done"/> は <b>UI スレッドで</b>呼ばれ、渡された
    /// <see cref="Item.Pidl"/> の持ち主は呼び出し側に移る。</para></summary>
    internal static void EnumerateAsync(nint parent, Action<List<Item>> done)
    {
        // 根（0）は複製する相手が無い。そのまま 0 を渡す
        var owned = parent == 0 ? 0 : ILClone(parent);
        if (parent != 0 && owned == 0)
        {
            done([]);
            return;
        }
        lock (Gate)
        {
            Queue.Enqueue((owned, done));
            if (_worker is null)
            {
                // 前面に出ない裏方。プロセスの終わりに道連れでよい（IsBackground）。
                // **止める仕組みは持たせない。**列挙が返ってこないまま終了することがあり、
                // そのときに「止まるまで待つ」を入れると終了が返ってこなくなる
                _worker = new Thread(Run) { IsBackground = true, Name = "ShellNamespace" };
                _worker.Start();
            }
            Monitor.Pulse(Gate);
        }
    }

    private static void Run()
    {
        // ★ ここで一度だけ。以後このスレッドは MTA として振る舞う
        var hr = CoInitializeEx(0, COINIT_MULTITHREADED);
        Diagnostics.Write($"[tree] 列挙スレッド開始 CoInitializeEx=0x{hr:X8}");
        while (true)
        {
            (nint Parent, Action<List<Item>> Done) work;
            lock (Gate)
            {
                while (Queue.Count == 0)
                {
                    Monitor.Wait(Gate);
                }
                work = Queue.Dequeue();
            }
            List<Item> items;
            try
            {
                items = Enumerate(work.Parent);
            }
            catch (Exception ex)
            {
                // 読めない・返ってこないは「子なし」として扱う（ダイアログは出さない）
                Diagnostics.Report("ShellNamespace.Enumerate", ex);
                items = [];
            }
            finally
            {
                // 複製した親はここまで。ノードが持つ PIDL とは別勘定なので
                // Free() ではなく直に返す
                if (work.Parent != 0)
                {
                    CoTaskMemFree(work.Parent);
                }
            }
            // 受け取る側が消えていたら、ここで PIDL を捨てる責任が残る。
            // それは呼び出し側（UiDispatcher の中）で判断する
            UiDispatcher.Post(() => work.Done(items));
        }
    }

    /// <summary>実際の列挙。<b>このスレッドで結び直す</b>ので、
    /// インターフェイスのポインタは外へ出ない。</summary>
    private static List<Item> Enumerate(nint parent)
    {
        var items = new List<Item>();
        if (SHGetDesktopFolder(out var desktopPtr) < 0 || desktopPtr == 0)
        {
            return items;
        }
        var folderPtr = desktopPtr;
        var boundExtra = false;
        try
        {
            if (parent != 0)
            {
                // 根そのもの（parent == 0）以外は、デスクトップからその PIDL へ結び直す
                if (SHBindToObject(desktopPtr, parent, 0, in IID_IShellFolder, out var childPtr) < 0
                    || childPtr == 0)
                {
                    return items;
                }
                folderPtr = childPtr;
                boundExtra = true;
            }

            var folder = (IShellFolder)ComWrappers.GetOrCreateObjectForComInstance(folderPtr, CreateObjectFlags.None);
            // ★ 隠し・システムも出す。一覧（ファイルシステムを直接読む側）が出して
            //   薄色にしているので、付けないと同じアプリの中で食い違う
            var flags = SHCONTF_FOLDERS | SHCONTF_NAVIGATION_PANE
                | SHCONTF_INCLUDEHIDDEN | SHCONTF_INCLUDESUPERHIDDEN;
            if (folder.EnumObjects(0, flags, out var enumPtr) < 0 || enumPtr == 0)
            {
                return items;
            }
            // ★ 並べ替えのために、相対 PIDL を列挙が終わるまで持っておく。
            //   CompareIDs は「親のフォルダ＋相対 PIDL 2 つ」でしか呼べない
            var pending = new List<(nint Relative, nint Absolute, uint Attributes)>();
            try
            {
                var enumerator = (IEnumIDList)ComWrappers.GetOrCreateObjectForComInstance(enumPtr, CreateObjectFlags.None);
                nint child;
                while (enumerator.Next(1, (nint)(&child), out var fetched) == 0 && fetched == 1)
                {
                    // ★ 属性は、親のフォルダに相対 PIDL で聞く。
                    //   SHGetFileInfo の SHGFI_ATTRIBUTES は「既定の一式」しか返さず、
                    //   SFGAO_HASSUBFOLDER（高い）が入る保証が無い。
                    //   親が手元にあるうちに、欲しいものだけ名指しで聞いておく
                    var attributes = SFGAO_HASSUBFOLDER | SFGAO_FILESYSTEM | SFGAO_HIDDEN;
                    if (folder.GetAttributesOf(1, (nint)(&child), ref attributes) < 0)
                    {
                        attributes = 0;
                    }

                    // ★ デスクトップから見た相対 PIDL は、そのまま絶対 PIDL。
                    //   根のときだけは、つなぐ相手も作る必要も無い
                    var absolute = parent == 0 ? child : ILCombine(parent, child);
                    if (absolute == 0)
                    {
                        CoTaskMemFree(child);
                        continue;
                    }
                    Interlocked.Increment(ref _taken);
                    pending.Add((child, absolute, attributes));
                }

                // ★ 並びはシェルに決めさせる。列挙が返ってきた順のままだと、
                //   エクスプローラーとまるで違う並びになる（2026-09-14 実測）。
                //   自前で名前順に並べないのは、シェルの並び（PC や
                //   ネットワークの位置）を再現できないため
                pending.Sort((a, b) =>
                {
                    var hr = folder.CompareIDs(0, a.Relative, b.Relative);
                    // 下位 16bit が比較の結果（符号付き）。失敗したら順を変えない
                    return hr < 0 ? 0 : (short)(hr & 0xFFFF);
                });

                foreach (var (relative, absolute, attributes) in pending)
                {
                    var item = Describe(absolute, attributes);
                    // 根に出さないものは、ここで捨てる（PIDL も返す）
                    if (item is null || (parent == 0 && IsHiddenAtRoot(item)))
                    {
                        if (item is not null)
                        {
                            Diagnostics.Write($"[tree] 根に出さない {item.Name} {item.ParsingName}");
                        }
                        Free(absolute);
                    }
                    else
                    {
                        items.Add(item);
                    }
                    // 相対 PIDL は絶対 PIDL の中にコピー済み。根のときは同じものなので返さない
                    if (parent != 0)
                    {
                        CoTaskMemFree(relative);
                    }
                }
            }
            finally
            {
                Marshal.Release(enumPtr);
            }
        }
        finally
        {
            if (boundExtra)
            {
                Marshal.Release(folderPtr);
            }
            Marshal.Release(desktopPtr);
        }
        return items;
    }

    /// <summary>絶対 PIDL 1 つから、ツリーに要るものをそろえる。
    /// 属性は親に聞いたものを受け取る（ここでは聞き直さない）。</summary>
    private static Item? Describe(nint pidl, uint attributes)
    {
        var name = NameOf(pidl, SIGDN_NORMALDISPLAY);
        if (name is null)
        {
            return null;
        }

        // アイコンの番号だけ。SHGFI_ATTRIBUTES は付けない（属性は親に聞いてある）
        var info = default(SHFILEINFOW);
        var image = SHGetFileInfoPidl(pidl, 0, ref info, (uint)sizeof(SHFILEINFOW),
            SHGFI_PIDL | SHGFI_SYSICONINDEX | SHGFI_SMALLICON);

        // パスは「取れるかどうか」がそのまま「実体があるか」の判定になる。
        // ただし取れても開けないものがあるので（コントロールパネルの一部）、
        // 実際に開ける場所かどうかは呼び出し側が Directory.Exists で見る
        var path = (attributes & SFGAO_FILESYSTEM) != 0
            ? NameOf(pidl, SIGDN_FILESYSPATH)
            : null;

        // ★ 「PC」だけは特別扱いするので、解析名（言語に依らない）で見分ける
        var parsing = NameOf(pidl, SIGDN_DESKTOPABSOLUTEPARSING);

        return new Item
        {
            Pidl = pidl,
            Name = name,
            ParsingName = parsing,
            IsThisPc = parsing is not null
                && parsing.Equals(ThisPcParsingName, StringComparison.OrdinalIgnoreCase),
            Path = path,
            HasChildren = (attributes & SFGAO_HASSUBFOLDER) != 0,
            IsHidden = (attributes & SFGAO_HIDDEN) != 0,
            // 取れなかったときはフォルダの絵に落とす。
            // この番号は Create のときに UI スレッドで温めてある
            Icon = image != 0 ? info.iIcon : ShellImageList.Folder,
        };
    }

    private static string? NameOf(nint pidl, uint kind)
    {
        if (SHGetNameFromIDList(pidl, kind, out var buffer) < 0 || buffer == 0)
        {
            return null;
        }
        try
        {
            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            CoTaskMemFree(buffer);
        }
    }
}
