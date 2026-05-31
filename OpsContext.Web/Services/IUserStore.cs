namespace OpsContext.Web.Services;

/// <summary>認証済みユーザーの情報を保持する不変レコード。</summary>
/// <param name="UserName">ログイン ID (英数字)。</param>
/// <param name="DisplayName">表示名 (日本語氏名)。</param>
/// <param name="Role">業務ロール (Admin / Sales / Purchasing / Production / Accounting)。</param>
/// <param name="PersonalPrompt">パーソナルプロンプト（本人のみ編集可）。</param>
public sealed record AppUser(
    string UserName,
    string DisplayName,
    string Role,
    string? PersonalPrompt = null)
{
    /// <summary>Role == "Admin" のとき管理者として扱う。</summary>
    public bool IsAdmin => Role == "Admin";
}

/// <summary>
/// ユーザーストア抽象。
/// モックモードは MockUserStore、本番は SqlUserStore が実装する。
/// Entra ID 移行時はこのインターフェースを差し替え実装に置換する。
/// </summary>
public interface IUserStore
{
    /// <summary>
    /// ユーザー名とパスワードを検証して一致するユーザーを返す。
    /// 認証失敗時は null を返す。
    /// </summary>
    Task<AppUser?> ValidateAsync(string userName, string password, CancellationToken ct = default);

    /// <summary>アクティブなユーザー一覧を返す。ログイン画面のカード表示に使用する。</summary>
    Task<IReadOnlyList<AppUser>> ListAsync(CancellationToken ct = default);

    /// <summary>指定ユーザー名のユーザーを1件取得する。存在しない場合は null。</summary>
    Task<AppUser?> GetByUserNameAsync(string userName, CancellationToken ct = default);

    // -----------------------------------------------------------------------
    // 管理系 (Admin 権限チェックは呼び出し元で行う)
    // -----------------------------------------------------------------------

    /// <summary>新規アカウントを作成する。role に "Admin" を指定すると管理者になる。</summary>
    Task CreateAsync(string userName, string displayName, string role, string password, CancellationToken ct = default);

    /// <summary>アカウントのプロフィール（表示名・ロール）を更新する。role に "Admin" を指定すると管理者になる。</summary>
    Task UpdateProfileAsync(string userName, string displayName, string role, CancellationToken ct = default);

    /// <summary>パスワードをリセットする。</summary>
    Task SetPasswordAsync(string userName, string newPassword, CancellationToken ct = default);

    /// <summary>アカウントを論理削除 (IsActive = 0) する。</summary>
    Task DeleteAsync(string userName, CancellationToken ct = default);

    // -----------------------------------------------------------------------
    // 本人専用
    // -----------------------------------------------------------------------

    /// <summary>パーソナルプロンプトを更新する。null で消去。</summary>
    Task UpdatePersonalPromptAsync(string userName, string? prompt, CancellationToken ct = default);
}
