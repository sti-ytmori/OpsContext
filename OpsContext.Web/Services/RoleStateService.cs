namespace OpsContext.Web.Services;

/// <summary>
/// design 07「RoleStateService」。
/// Scoped サービスとして現在ロールを保持し、ロール変更時に OnRoleChanged イベントを発火する。
/// MainLayout のロール切替ドロップダウンと各ページコンポーネントが購読する。
/// </summary>
public sealed class RoleStateService
{
    private string _currentRole = "Sales";

    /// <summary>業務ロール (Sales / Purchasing / Production / Accounting)。</summary>
    public static readonly string[] BusinessRoles = ["Sales", "Purchasing", "Production", "Accounting"];

    /// <summary>現在のロール (Admin / Sales / Purchasing / Production / Accounting)。</summary>
    public string CurrentRole => _currentRole;

    /// <summary>ロールが変更されたときに発火するイベント。</summary>
    public Action? OnRoleChanged { get; set; }

    /// <summary>
    /// ロールを変更してイベントを発火する。
    /// 不正値の場合は "Sales" にフォールバックする。
    /// </summary>
    public void SetRole(string role)
    {
        var validRoles = new[] { "Admin", "Sales", "Purchasing", "Production", "Accounting" };
        _currentRole = Array.Exists(validRoles, r => r == role) ? role : "Sales";
        OnRoleChanged?.Invoke();
    }
}
