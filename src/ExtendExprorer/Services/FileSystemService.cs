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
                entries.Add(new Entry(info.Name, isDir, size, info.LastWriteTime));
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
}
