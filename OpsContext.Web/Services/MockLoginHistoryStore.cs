namespace OpsContext.Web.Services;

/// <summary>
/// モックモード用インメモリ LoginHistoryStore。
/// スレッドセーフな List で履歴を保持し、ListRecentAsync は降順で返す。
/// </summary>
public sealed class MockLoginHistoryStore : ILoginHistoryStore
{
    private readonly List<LoginHistoryEntry> _entries = [];
    private readonly object _lock = new();

    public Task RecordAsync(
        string  userName,
        string? displayName,
        string? role,
        bool    success,
        string? clientIp,
        string? userAgent,
        CancellationToken ct = default)
    {
        var entry = new LoginHistoryEntry(
            UserName:    userName,
            DisplayName: displayName,
            Role:        role,
            Success:     success,
            ClientIp:    clientIp,
            UserAgent:   userAgent,
            CreatedAt:   DateTime.UtcNow);

        lock (_lock)
            _entries.Add(entry);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LoginHistoryEntry>> ListRecentAsync(int take = 200, CancellationToken ct = default)
    {
        lock (_lock)
        {
            IReadOnlyList<LoginHistoryEntry> result = _entries
                .OrderByDescending(e => e.CreatedAt)
                .Take(take)
                .ToList();
            return Task.FromResult(result);
        }
    }
}
