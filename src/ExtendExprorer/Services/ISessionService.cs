using ExtendExprorer.Models.Session;

namespace ExtendExprorer.Services;

public interface ISessionService
{
    /// <summary>session.json を読む。無し・破損・スキーマ不一致は null（破損時は .bak に退避）。</summary>
    Task<SessionFile?> LoadAsync();

    /// <summary>状態変更のデバウンス保存（非同期・失敗は無視して次回再試行）。</summary>
    Task SaveAsync(SessionFile file);

    /// <summary>終了時の最終保存（同期・呼び出しが返るまでに書き込む）。</summary>
    void SaveSync(SessionFile file);
}
