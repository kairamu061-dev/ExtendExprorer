using ExtendExprorer.Models;

namespace ExtendExprorer.Services;

public interface IFileSystemService
{
    string HomePath { get; }
    Task<ListResult> ListAsync(string path);

    /// <summary>サブフォルダのみを名前昇順で列挙する（folder-tree 用）。失敗時は空リスト。</summary>
    Task<IReadOnlyList<Entry>> ListDirectoriesAsync(string path);

    /// <summary>アドレスバー入力を移動先フォルダに解決する。ディレクトリならそのまま、
    /// ファイルなら親フォルダ、存在しなければ null（address-bar 用）。</summary>
    Task<string?> ResolveNavigationTargetAsync(string input);
}
