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

    /// <summary>退避先。壊れていたときの記録で名前を出すために公開する。</summary>
    public string BackupPath => _path + ".bak";

    public Task<SessionFile?> LoadAsync() => Task.Run(() => Load());

    public SessionFile? Load() => Load(out _);

    /// <summary>読む。<paramref name="corrupt"/> は「**あったが読めなかった**」。
    ///
    /// <para>「無い（初回起動）」と分けるためだけの値。どちらも既定状態で起動するので
    /// 画面の見え方は同じだが、<b>片方は失われていて、片方は元から無い。</b>
    /// 後から「設定が消えた」と言われたときに、この区別が無いと追えない。</para></summary>
    public SessionFile? Load(out bool corrupt)
    {
        corrupt = false;
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
                corrupt = true;
                BackupCorrupt();
                return null;
            }
            return file;
        }
        catch
        {
            corrupt = true;
            BackupCorrupt();
            return null;
        }
    }

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
