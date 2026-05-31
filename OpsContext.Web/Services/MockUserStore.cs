namespace OpsContext.Web.Services;

/// <summary>
/// モックモード用インメモリ UserStore。
/// 擬似ERP シードデータの担当者名と同じ 4 アカウントに加え、
/// 管理者アカウント user_system (Role = "Admin") を保持する。全員共通パスワード "Demo@2026"。
/// </summary>
public sealed class MockUserStore : IUserStore
{
    private const string DemoPassword = "Demo@2026";

    // ユーザーデータをミュータブルなリストで保持（インメモリ CRUD 用）
    private readonly List<MockUserEntry> _users;
    private readonly object _lock = new();

    public MockUserStore()
    {
        _users =
        [
            new MockUserEntry("user_system",     "システム管理者",  "Admin",       DemoPassword),
            new MockUserEntry("user_sales",       "田中 一郎",      "Sales",       DemoPassword),
            new MockUserEntry("user_purchasing",  "鈴木 花子",      "Purchasing",  DemoPassword),
            new MockUserEntry("user_production",  "佐藤 次郎",      "Production",  DemoPassword),
            new MockUserEntry("user_accounting",  "山田 三郎",      "Accounting",  DemoPassword),
        ];
    }

    // -----------------------------------------------------------------------
    // IUserStore 実装 — 読み取り
    // -----------------------------------------------------------------------

    public Task<AppUser?> ValidateAsync(string userName, string password, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var entry = _users.FirstOrDefault(u =>
                string.Equals(u.UserName, userName, StringComparison.OrdinalIgnoreCase)
                && u.IsActive
                && u.Password == password);

            return Task.FromResult<AppUser?>(entry?.ToAppUser());
        }
    }

    public Task<IReadOnlyList<AppUser>> ListAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            IReadOnlyList<AppUser> list = _users
                .Where(u => u.IsActive)
                .Select(u => u.ToAppUser())
                .ToList();
            return Task.FromResult(list);
        }
    }

    public Task<AppUser?> GetByUserNameAsync(string userName, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var entry = _users.FirstOrDefault(u =>
                string.Equals(u.UserName, userName, StringComparison.OrdinalIgnoreCase)
                && u.IsActive);
            return Task.FromResult<AppUser?>(entry?.ToAppUser());
        }
    }

    // -----------------------------------------------------------------------
    // IUserStore 実装 — 管理系 (Admin 権限チェックは呼び出し元)
    // -----------------------------------------------------------------------

    public Task CreateAsync(string userName, string displayName, string role, string password, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_users.Any(u => string.Equals(u.UserName, userName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"ユーザー名 '{userName}' は既に存在します。");

            _users.Add(new MockUserEntry(userName, displayName, role, password));
        }
        return Task.CompletedTask;
    }

    public Task UpdateProfileAsync(string userName, string displayName, string role, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var entry = _users.FirstOrDefault(u =>
                string.Equals(u.UserName, userName, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                throw new InvalidOperationException($"ユーザー '{userName}' が見つかりません。");

            entry.DisplayName = displayName;
            entry.Role        = role;
        }
        return Task.CompletedTask;
    }

    public Task SetPasswordAsync(string userName, string newPassword, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var entry = _users.FirstOrDefault(u =>
                string.Equals(u.UserName, userName, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                throw new InvalidOperationException($"ユーザー '{userName}' が見つかりません。");

            entry.Password = newPassword;
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string userName, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var entry = _users.FirstOrDefault(u =>
                string.Equals(u.UserName, userName, StringComparison.OrdinalIgnoreCase));
            if (entry is not null)
                entry.IsActive = false;
        }
        return Task.CompletedTask;
    }

    // -----------------------------------------------------------------------
    // IUserStore 実装 — 本人専用
    // -----------------------------------------------------------------------

    public Task UpdatePersonalPromptAsync(string userName, string? prompt, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var entry = _users.FirstOrDefault(u =>
                string.Equals(u.UserName, userName, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                throw new InvalidOperationException($"ユーザー '{userName}' が見つかりません。");

            entry.PersonalPrompt = string.IsNullOrWhiteSpace(prompt) ? null : prompt.Trim();
        }
        return Task.CompletedTask;
    }

    // -----------------------------------------------------------------------
    // 内部クラス — ミュータブルなユーザーエントリ
    // -----------------------------------------------------------------------

    private sealed class MockUserEntry(string userName, string displayName, string role, string password)
    {
        public string  UserName        { get; }       = userName;
        public string  DisplayName     { get; set; }  = displayName;
        public string  Role            { get; set; }  = role;
        public string  Password        { get; set; }  = password;
        public bool    IsActive        { get; set; }  = true;
        public string? PersonalPrompt  { get; set; }  = null;

        public AppUser ToAppUser() => new(UserName, DisplayName, Role, PersonalPrompt);
    }
}
