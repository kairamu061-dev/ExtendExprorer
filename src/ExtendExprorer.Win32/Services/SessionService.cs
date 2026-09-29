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

    /// <summary>退避先。壊れていたときの記録で名前を出すために公開する。
    ///
    /// <para>★ <b>2026-09-29 に <c>.bak</c> から <c>.corrupt</c> へ改名した。</b>
    /// <c>.bak</c> は「最後に正常だったもの」を連想させるが、ここに入るのは
    /// <b>常に壊れたファイル</b>で、復旧には使えない。
    /// 動作確認でも <c>.bak</c> を「残骸」として数えていたので、
    /// <b>名前から中身を読み違える下地ができていた</b>（2026-09-29 のコードレビュー指摘 14）。
    /// 本物の last-known-good を持ちたくなったら、そのとき別に <c>.bak</c> を作る。</para></summary>
    public string BackupPath => _path + ".corrupt";

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
            if (file is not null && file.Version > 1)
            {
                // ★ 新しい版を読めないこと自体は正しいが、利用者には
                //   「設定が消えた」としか見えない。後から追えるように 1 行残す
                //   （Diagnostics.Note を用意した動機がこれ）
                UI.Diagnostics.Note(
                    $"session.json の版が新しい（Version={file.Version}。読めるのは 1 まで）ので、"
                    + $"{BackupPath} へ退避し、既定状態で起動した");
            }
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
                File.Copy(_path, BackupPath, overwrite: true);
            }
        }
        catch
        {
            // 退避失敗は無視
        }
    }
}
