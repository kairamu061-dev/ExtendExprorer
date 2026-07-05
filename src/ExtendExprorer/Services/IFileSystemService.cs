using ExtendExprorer.Models;

namespace ExtendExprorer.Services;

public interface IFileSystemService
{
    string HomePath { get; }
    Task<ListResult> ListAsync(string path);
}
