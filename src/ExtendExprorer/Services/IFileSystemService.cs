using ExtendExprorer.Models;

namespace ExtendExprorer.Services;

public interface IFileSystemService
{
    string HomePath { get; }
    Task<ListResult> ListAsync(string path);

    /// <summary>サブフォルダのみを名前昇順で列挙する（folder-tree 用）。失敗時は空リスト。</summary>
    Task<IReadOnlyList<Entry>> ListDirectoriesAsync(string path);
}
