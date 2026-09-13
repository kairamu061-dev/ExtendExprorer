using ExtendExprorer.Interop;
using ExtendExprorer.Models;

namespace ExtendExprorer.Services;

public sealed class FileSystemService : IFileSystemService
{
    public string HomePath { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>列挙の条件を<b>明示的に</b>指定する。仕様に関わる既定値には頼らない。
    ///
    /// <para><c>AttributesToSkip = 0</c> … 隠し・システム属性の項目も必ず並べる
    /// （この一覧では常に表示して薄色で区別する仕様）。</para>
    ///
    /// <para><c>IgnoreInaccessible = false</c> … 読めない項目を黙って飛ばさない。ただし
    /// <b>これが効くのは下位の項目だけ</b>で、開こうとしたフォルダ自身が読めない場合は
    /// 例外にならず 0 件が返る。そちらは <see cref="NativeMethods.IsEnumerationDenied"/> で
    /// 別途確かめている（BUG-020）。</para></summary>
    private static readonly EnumerationOptions ListOptions = new()
    {
        IgnoreInaccessible = false,
        AttributesToSkip = 0,
        RecurseSubdirectories = false,
    };

    public Task<ListResult> ListAsync(string path) => Task.Run<ListResult>(() =>
    {
        try
        {
            var dir = new DirectoryInfo(path);
            if (!dir.Exists)
            {
                return new ListError(ListErrorKind.NotFound, path);
            }

            UI.Diagnostics.Write($"[list] {path}");

            var entries = new List<Entry>();
            foreach (var info in dir.EnumerateFileSystemInfos("*", ListOptions))
            {
                var isDir = info is DirectoryInfo;
                var size = info is FileInfo file ? file.Length : 0L;
                var attributes = info.Attributes;
                var hiddenOrSystem = (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;
                entries.Add(new Entry(info.Name, isDir, size, info.LastWriteTime, hiddenOrSystem));
                if (entries.Count <= 30)
                {
                    UI.Diagnostics.Write($"  [{entries.Count - 1}] {info.Name} attrs={attributes} hiddenOrSystem={hiddenOrSystem}");
                }
            }

            // 0 件のときだけ、本当に空なのか読めなかったのかを Win32 の戻り値で確かめる。
            // .NET の列挙は、対象フォルダ自身が読めないとき例外ではなく 0 件を返してくる
            // （`IgnoreInaccessible` は下位の項目にしか効かない・BUG-020）
            if (entries.Count == 0)
            {
                UI.Diagnostics.Write("  列挙 0 件。読めないだけではないか確かめる:");
                if (NativeMethods.IsEnumerationDenied(path))
                {
                    UI.Diagnostics.Write("  → アクセス拒否として扱う");
                    return new ListError(ListErrorKind.AccessDenied, path);
                }
                UI.Diagnostics.Write("  → 空フォルダとして扱う");
            }
            UI.Diagnostics.Write($"  合計 {entries.Count} 件");
            return (ListResult)new ListOk(entries);
        }
        catch (UnauthorizedAccessException)
        {
            UI.Diagnostics.Write("  UnauthorizedAccessException → アクセス拒否");
            return new ListError(ListErrorKind.AccessDenied, path);
        }
        catch (DirectoryNotFoundException)
        {
            UI.Diagnostics.Write("  DirectoryNotFoundException → パスが見つかりません");
            return new ListError(ListErrorKind.NotFound, path);
        }
        catch (Exception ex)
        {
            UI.Diagnostics.Write($"  {ex.GetType().Name}: {ex.Message}");
            return new ListError(ListErrorKind.Other, ex.Message);
        }
    });

    /// <summary>ドライブと容量。<b>準備できていないドライブも出す</b>
    /// （エクスプローラーと同じ。空の光学ドライブが消えると、かえって分かりにくい）。
    /// 容量は読めたときだけ入れ、読めなければ 0 にして「—」で出す。</summary>
    public Task<IReadOnlyList<DriveRow>> ListDrivesAsync() => Task.Run<IReadOnlyList<DriveRow>>(() =>
    {
        var rows = new List<DriveRow>();
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch
        {
            return rows;
        }
        foreach (var drive in drives)
        {
            string label;
            var typeName = TypeNameOf(drive);
            long total = 0;
            long free = 0;
            try
            {
                // IsReady が false のときに VolumeLabel などを触ると投げる
                if (drive.IsReady)
                {
                    var volume = drive.VolumeLabel;
                    label = string.IsNullOrWhiteSpace(volume)
                        ? $"{typeName} ({drive.Name.TrimEnd('\\')})"
                        : $"{volume} ({drive.Name.TrimEnd('\\')})";
                    total = drive.TotalSize;
                    free = drive.AvailableFreeSpace;
                }
                else
                {
                    label = $"{typeName} ({drive.Name.TrimEnd('\\')})";
                }
            }
            catch
            {
                label = $"{typeName} ({drive.Name.TrimEnd('\\')})";
            }
            rows.Add(new DriveRow(drive.Name, label, typeName, total, free));
        }
        return rows;
    });

    private static string TypeNameOf(DriveInfo drive)
    {
        try
        {
            return drive.DriveType switch
            {
                DriveType.Fixed => "ローカル ディスク",
                DriveType.Removable => "リムーバブル ディスク",
                DriveType.Network => "ネットワーク ドライブ",
                DriveType.CDRom => "CD ドライブ",
                DriveType.Ram => "RAM ディスク",
                _ => "ディスク",
            };
        }
        catch
        {
            return "ディスク";
        }
    }

    public Task<IReadOnlyList<Entry>> ListDirectoriesAsync(string path) => Task.Run<IReadOnlyList<Entry>>(() =>
    {
        try
        {
            var entries = new List<Entry>();
            foreach (var info in new DirectoryInfo(path).EnumerateDirectories("*", ListOptions))
            {
                var hiddenOrSystem = (info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;
                entries.Add(new Entry(info.Name, true, 0L, info.LastWriteTime, hiddenOrSystem));
            }
            // 一覧と同じ自然順（`Folder2` < `Folder10`）で並べる
            entries.Sort((a, b) => NaturalStringComparer.Instance.Compare(a.Name, b.Name));
            return entries;
        }
        catch
        {
            // ツリー展開はアクセス不可・消滅を「子なし」として扱う(spec のエラーケース)
            return Array.Empty<Entry>();
        }
    });

    public Task<string?> ResolveNavigationTargetAsync(string input) => Task.Run<string?>(() =>
    {
        try
        {
            var path = input.Trim().Trim('"');
            if (path.Length == 0)
            {
                return null;
            }
            if (Directory.Exists(path))
            {
                return path;
            }
            // ファイルパスなら親フォルダへ（spec のエラーケース）
            if (File.Exists(path))
            {
                return System.IO.Path.GetDirectoryName(path);
            }
            return null;
        }
        catch
        {
            return null;
        }
    });
}
