namespace OpsContext.Web.Services;

/// <summary>ロールプロンプトの1エントリを表すレコード。</summary>
/// <param name="Role">業務ロール (Sales / Purchasing / Production / Accounting)。</param>
/// <param name="Prompt">追加指示テキスト。null または空文字は「指示なし」扱い。</param>
/// <param name="UpdatedAt">最終更新日時 (UTC)。</param>
public sealed record RolePromptEntry(string Role, string? Prompt, DateTime UpdatedAt);

/// <summary>
/// ロールプロンプトストア抽象。
/// モックモードは MockRolePromptStore、本番は SqlRolePromptStore が実装する。
/// </summary>
public interface IRolePromptStore
{
    /// <summary>指定ロールの追加指示テキストを取得する。未設定の場合は null。</summary>
    Task<string?> GetPromptAsync(string role, CancellationToken ct = default);

    /// <summary>全ロール分のプロンプトエントリを返す。</summary>
    Task<IReadOnlyList<RolePromptEntry>> ListAsync(CancellationToken ct = default);

    /// <summary>指定ロールの追加指示テキストを保存する。null または空文字で消去。</summary>
    Task SetPromptAsync(string role, string? prompt, CancellationToken ct = default);
}
