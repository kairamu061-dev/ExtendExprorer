using ExtendExprorer.Models;

namespace ExtendExprorer.Services;

public sealed class FileSystemService : IFileSystemService
{
    public string HomePath { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

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
            foreach (var info in dir.EnumerateFileSystemInfos())
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
            foreach (var info in new DirectoryInfo(path).EnumerateDirectories())
            {
                var hiddenOrSystem = (info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;
                entries.Add(new Entry(info.Name, true, 0L, info.LastWriteTime, hiddenOrSystem));
            }
            entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
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
