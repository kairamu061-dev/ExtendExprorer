using ExtendExprorer.Models;

namespace ExtendExprorer.Services;

public sealed class FileSystemService : IFileSystemService
{
    public string HomePath { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>列挙の条件を<b>明示的に</b>指定する。既定に任せると、
    /// <list type="bullet">
    /// <item>読めないフォルダが例外ではなく<b>空</b>として返り、「アクセスが拒否されました」を
    /// 出せない（BUG-020）</item>
    /// <item>隠し・システム属性の項目が除外されうる（この一覧では常に表示する仕様）</item>
    /// </list>
    /// という取り違えが起きる。子フォルダへは降りないので、
    /// <c>IgnoreInaccessible = false</c> で困るのは「開こうとしたフォルダ自身が読めない」場合だけ。</summary>
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

            var entries = new List<Entry>();
            foreach (var info in dir.EnumerateFileSystemInfos("*", ListOptions))
            {
                var isDir = info is DirectoryInfo;
                var size = info is FileInfo file ? file.Length : 0L;
                var hiddenOrSystem = (info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;
                entries.Add(new Entry(info.Name, isDir, size, info.LastWriteTime, hiddenOrSystem));
            }
            return (ListResult)new ListOk(entries);
        }
        catch (UnauthorizedAccessException)
        {
            return new ListError(ListErrorKind.AccessDenied, path);
        }
        catch (DirectoryNotFoundException)
        {
            return new ListError(ListErrorKind.NotFound, path);
        }
        catch (Exception ex)
        {
            return new ListError(ListErrorKind.Other, ex.Message);
        }
    });

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
