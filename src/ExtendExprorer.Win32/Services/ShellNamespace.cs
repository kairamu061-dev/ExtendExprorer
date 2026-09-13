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
    }

    private static readonly StrategyBasedComWrappers ComWrappers = new();

    private static readonly Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");

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
    /// <para><paramref name="parent"/> は<b>借りるだけ</b>で、解放しない（呼び出し側が持ち主）。
    /// <paramref name="done"/> は <b>UI スレッドで</b>呼ばれ、渡された
    /// <see cref="Item.Pidl"/> の持ち主は呼び出し側に移る。</para></summary>
    internal static void EnumerateAsync(nint parent, Action<List<Item>> done)
    {
        lock (Gate)
        {
            Queue.Enqueue((parent, done));
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
            if (folder.EnumObjects(0, SHCONTF_FOLDERS | SHCONTF_NAVIGATION_PANE, out var enumPtr) < 0
                || enumPtr == 0)
            {
                return items;
            }
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
                    nint absolute;
                    if (parent == 0)
                    {
                        absolute = child;
                    }
                    else
                    {
                        absolute = ILCombine(parent, child);
                        CoTaskMemFree(child); // つないだ先にコピーされているので、元は要らない
                        if (absolute == 0)
                        {
                            continue;
                        }
                    }
                    Interlocked.Increment(ref _taken);
                    var item = Describe(absolute, attributes);
                    if (item is null)
                    {
                        Free(absolute);
                        continue;
                    }
                    items.Add(item);
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

        return new Item
        {
            Pidl = pidl,
            Name = name,
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
