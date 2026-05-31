using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using OpsContext.Agents.Options;
using System.Security.Cryptography;

namespace OpsContext.Web.Services;

/// <summary>
/// 本番用 SqlUserStore。Users テーブルを参照してユーザー検証・CRUD を行う。
/// パスワードは PBKDF2-SHA256 (100,000 回) でハッシュ化して保存する。
/// 管理者かどうかは Role = "Admin" で判定し、IsAdmin 列は持たない。
/// 起動時に SeedIfEmptyAsync を呼び出すことで、テーブルが空の場合にデモアカウントを自動投入する。
/// </summary>
public sealed class SqlUserStore : IUserStore
{
    private readonly string _connectionString;

    // -----------------------------------------------------------------------
    // デモ用シード定義 (user_system は Role = "Admin" で管理者)
    // -----------------------------------------------------------------------
    private const string DemoPassword = "Demo@2026";

    private static readonly (AppUser User, string Password)[] SeedUsers =
    [
        (new AppUser("user_system",      "システム管理者", "Admin"),       DemoPassword),
        (new AppUser("user_sales",       "田中 一郎",      "Sales"),       DemoPassword),
        (new AppUser("user_purchasing",  "鈴木 花子",      "Purchasing"),  DemoPassword),
        (new AppUser("user_production",  "佐藤 次郎",      "Production"),  DemoPassword),
        (new AppUser("user_accounting",  "山田 三郎",      "Accounting"),  DemoPassword),
    ];

    public SqlUserStore(IOptions<OpsContextOptions> opts)
    {
        _connectionString = opts.Value.SqlConnectionString;
    }

    // -----------------------------------------------------------------------
    // IUserStore 実装 — 読み取り
    // -----------------------------------------------------------------------

    public async Task<AppUser?> ValidateAsync(string userName, string password, CancellationToken ct = default)
    {
        const string sql = """
            SELECT UserName, DisplayName, Role, PasswordHash, PersonalPrompt
            FROM Users
            WHERE UserName = @UserName AND IsActive = 1
            """;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@UserName", userName));
        cmd.CommandTimeout = 10;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        var user       = ReadUser(reader);
        var storedHash = reader.GetString(3);

        return VerifyPassword(password, storedHash) ? user : null;
    }

    public async Task<IReadOnlyList<AppUser>> ListAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT UserName, DisplayName, Role, PersonalPrompt
            FROM Users
            WHERE IsActive = 1
            ORDER BY UserId
            """;

        return await QueryUsersAsync(sql, null, ct);
    }

    public async Task<AppUser?> GetByUserNameAsync(string userName, CancellationToken ct = default)
    {
        const string sql = """
            SELECT UserName, DisplayName, Role, PersonalPrompt
            FROM Users
            WHERE UserName = @UserName AND IsActive = 1
            """;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@UserName", userName));
        cmd.CommandTimeout = 10;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return ReadUserNoHash(reader);
    }

    // -----------------------------------------------------------------------
    // IUserStore 実装 — 管理系
    // -----------------------------------------------------------------------

    public async Task CreateAsync(string userName, string displayName, string role, string password, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO Users (UserName, DisplayName, Role, PasswordHash)
            VALUES (@UserName, @DisplayName, @Role, @PasswordHash)
            """;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@UserName",     userName));
        cmd.Parameters.Add(new SqlParameter("@DisplayName",  displayName));
        cmd.Parameters.Add(new SqlParameter("@Role",         role));
        cmd.Parameters.Add(new SqlParameter("@PasswordHash", HashPassword(password)));
        cmd.CommandTimeout = 10;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateProfileAsync(string userName, string displayName, string role, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE Users
            SET DisplayName = @DisplayName, Role = @Role
            WHERE UserName = @UserName
            """;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@UserName",    userName));
        cmd.Parameters.Add(new SqlParameter("@DisplayName", displayName));
        cmd.Parameters.Add(new SqlParameter("@Role",        role));
        cmd.CommandTimeout = 10;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SetPasswordAsync(string userName, string newPassword, CancellationToken ct = default)
    {
        const string sql = "UPDATE Users SET PasswordHash = @PasswordHash WHERE UserName = @UserName";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@UserName",     userName));
        cmd.Parameters.Add(new SqlParameter("@PasswordHash", HashPassword(newPassword)));
        cmd.CommandTimeout = 10;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(string userName, CancellationToken ct = default)
    {
        const string sql = "UPDATE Users SET IsActive = 0 WHERE UserName = @UserName";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@UserName", userName));
        cmd.CommandTimeout = 10;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // -----------------------------------------------------------------------
    // IUserStore 実装 — 本人専用
    // -----------------------------------------------------------------------

    public async Task UpdatePersonalPromptAsync(string userName, string? prompt, CancellationToken ct = default)
    {
        const string sql = "UPDATE Users SET PersonalPrompt = @Prompt WHERE UserName = @UserName";

        var trimmed = string.IsNullOrWhiteSpace(prompt) ? null : prompt.Trim();

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@UserName", userName));
        cmd.Parameters.Add(new SqlParameter("@Prompt",
            trimmed is null ? DBNull.Value : (object)trimmed));
        cmd.CommandTimeout = 10;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // -----------------------------------------------------------------------
    // 起動時シード (Users テーブルが空の場合のみ投入)
    // -----------------------------------------------------------------------

    public async Task SeedIfEmptyAsync(CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using (var countCmd = new SqlCommand("SELECT COUNT(*) FROM Users", conn))
        {
            var count = (int)(await countCmd.ExecuteScalarAsync(ct))!;
            if (count > 0) return;
        }

        const string insertSql = """
            INSERT INTO Users (UserName, DisplayName, Role, PasswordHash)
            VALUES (@UserName, @DisplayName, @Role, @PasswordHash)
            """;

        foreach (var (user, password) in SeedUsers)
        {
            var hash = HashPassword(password);
            await using var cmd = new SqlCommand(insertSql, conn);
            cmd.Parameters.Add(new SqlParameter("@UserName",     user.UserName));
            cmd.Parameters.Add(new SqlParameter("@DisplayName",  user.DisplayName));
            cmd.Parameters.Add(new SqlParameter("@Role",         user.Role));
            cmd.Parameters.Add(new SqlParameter("@PasswordHash", hash));
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    // -----------------------------------------------------------------------
    // 内部ヘルパー
    // -----------------------------------------------------------------------

    private async Task<List<AppUser>> QueryUsersAsync(string sql, Action<SqlCommand>? paramSetup, CancellationToken ct)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        paramSetup?.Invoke(cmd);
        cmd.CommandTimeout = 10;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<AppUser>();
        while (await reader.ReadAsync(ct))
            results.Add(ReadUserNoHash(reader));
        return results;
    }

    // ValidateAsync 用（Name/Display/Role/PasswordHash/Prompt の順）
    private static AppUser ReadUser(SqlDataReader reader) =>
        new(
            UserName:       reader.GetString(0),
            DisplayName:    reader.GetString(1),
            Role:           reader.GetString(2),
            PersonalPrompt: reader.IsDBNull(4) ? null : reader.GetString(4));

    // ListAsync / GetByUserNameAsync 用（Name/Display/Role/Prompt の順）
    private static AppUser ReadUserNoHash(SqlDataReader reader) =>
        new(
            UserName:       reader.GetString(0),
            DisplayName:    reader.GetString(1),
            Role:           reader.GetString(2),
            PersonalPrompt: reader.IsDBNull(3) ? null : reader.GetString(3));

    // -----------------------------------------------------------------------
    // パスワードハッシュ (PBKDF2-SHA256)
    // -----------------------------------------------------------------------

    private static string HashPassword(string password)
    {
        const int saltBytes = 16;
        const int hashBytes = 32;
        const int iterations = 100_000;

        var salt = RandomNumberGenerator.GetBytes(saltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, iterations, HashAlgorithmName.SHA256, hashBytes);

        var combined = new byte[saltBytes + hashBytes];
        Buffer.BlockCopy(salt, 0, combined, 0,         saltBytes);
        Buffer.BlockCopy(hash, 0, combined, saltBytes, hashBytes);
        return Convert.ToBase64String(combined);
    }

    private static bool VerifyPassword(string password, string storedBase64)
    {
        const int saltBytes = 16;
        const int hashBytes = 32;
        const int iterations = 100_000;

        byte[] combined;
        try { combined = Convert.FromBase64String(storedBase64); }
        catch { return false; }

        if (combined.Length != saltBytes + hashBytes) return false;

        var salt     = combined[..saltBytes];
        var expected = combined[saltBytes..];
        var actual   = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, iterations, HashAlgorithmName.SHA256, hashBytes);

        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
