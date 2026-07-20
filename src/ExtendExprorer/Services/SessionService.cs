using System.Text.Json;
using ExtendExprorer.Models.Session;

namespace ExtendExprorer.Services;

/// <summary>session.json の読み書き。書き込みは一時ファイル→置換のアトミック方式。
/// JSON は AOT 対応のソース生成コンテキスト（SessionJsonContext）経由でのみ扱う。</summary>
public sealed class SessionService : ISessionService
{
    private readonly string _dir;
    private readonly string _path;

    public SessionService()
    {
        _dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExtendExprorer");
        _path = Path.Combine(_dir, "session.json");
    }

    public Task<SessionFile?> LoadAsync() => Task.Run(() =>
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }
            var json = File.ReadAllText(_path);
            var file = JsonSerializer.Deserialize(json, SessionJsonContext.Default.SessionFile);
            if (file is null || file.Version != 1 || file.Layout is null)
            {
                BackupCorrupt();
                return null;
            }
            return file;
        }
        catch
        {
            BackupCorrupt();
            return null;
        }
    });

    public Task SaveAsync(SessionFile file) => Task.Run(() => Write(file));

    public void SaveSync(SessionFile file) => Write(file);

    private void Write(SessionFile file)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            var json = JsonSerializer.Serialize(file, SessionJsonContext.Default.SessionFile);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, overwrite: true);
        }
        catch
        {
            // 書込失敗はアプリ継続。次回変更時に再試行される
        }
    }

    private void BackupCorrupt()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Copy(_path, _path + ".bak", overwrite: true);
            }
        }
        catch
        {
            // 退避失敗は無視
        }
    }
}
