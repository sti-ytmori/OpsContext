namespace OpsContext.Web.Services;

/// <summary>
/// モックモード用インメモリ RolePromptStore。
/// 4ロール分のエントリを辞書で保持し、SetPromptAsync で更新できる。
/// </summary>
public sealed class MockRolePromptStore : IRolePromptStore
{
    private static readonly string[] ValidRoles = ["Sales", "Purchasing", "Production", "Accounting"];

    // ロール → (Prompt, UpdatedAt)
    private readonly Dictionary<string, (string? Prompt, DateTime UpdatedAt)> _store;
    private readonly object _lock = new();

    public MockRolePromptStore()
    {
        var now = DateTime.UtcNow;
        _store = new Dictionary<string, (string?, DateTime)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Sales"]       = (null, now),
            ["Purchasing"]  = (null, now),
            ["Production"]  = (null, now),
            ["Accounting"]  = (null, now),
        };
    }

    public Task<string?> GetPromptAsync(string role, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _store.TryGetValue(role, out var entry);
            return Task.FromResult(string.IsNullOrWhiteSpace(entry.Prompt) ? null : entry.Prompt);
        }
    }

    public Task<IReadOnlyList<RolePromptEntry>> ListAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            IReadOnlyList<RolePromptEntry> list = ValidRoles
                .Select(r =>
                {
                    _store.TryGetValue(r, out var entry);
                    return new RolePromptEntry(r, entry.Prompt, entry.UpdatedAt);
                })
                .ToList();
            return Task.FromResult(list);
        }
    }

    public Task SetPromptAsync(string role, string? prompt, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var trimmed = string.IsNullOrWhiteSpace(prompt) ? null : prompt.Trim();
            _store[role] = (trimmed, DateTime.UtcNow);
        }
        return Task.CompletedTask;
    }
}
