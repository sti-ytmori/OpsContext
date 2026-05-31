namespace OpsContext.Web.Services;

/// <summary>ログイン試行の1件分のレコード。</summary>
/// <param name="UserName">入力されたユーザー名（失敗時も記録）。</param>
/// <param name="DisplayName">成功時のみ解決される表示名。</param>
/// <param name="Role">成功時のみ解決されるロール。</param>
/// <param name="Success">認証成否。</param>
/// <param name="ClientIp">クライアントIPアドレス（取得できない場合は null）。</param>
/// <param name="UserAgent">User-Agent ヘッダの値（512文字で切り詰め）。</param>
/// <param name="CreatedAt">記録日時（UTC）。</param>
public sealed record LoginHistoryEntry(
    string  UserName,
    string? DisplayName,
    string? Role,
    bool    Success,
    string? ClientIp,
    string? UserAgent,
    DateTime CreatedAt);

/// <summary>
/// ログイン履歴ストア抽象。
/// モックモードは MockLoginHistoryStore、本番は SqlLoginHistoryStore が実装する。
/// </summary>
public interface ILoginHistoryStore
{
    /// <summary>ログイン試行を記録する。</summary>
    Task RecordAsync(
        string  userName,
        string? displayName,
        string? role,
        bool    success,
        string? clientIp,
        string? userAgent,
        CancellationToken ct = default);

    /// <summary>直近の履歴を降順で返す。</summary>
    Task<IReadOnlyList<LoginHistoryEntry>> ListRecentAsync(int take = 200, CancellationToken ct = default);
}
