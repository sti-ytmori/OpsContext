using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using OpsContext.Agents.Options;

namespace OpsContext.Web.Services;

/// <summary>
/// 本番用 SqlRolePromptStore。RolePrompts テーブルを参照してロール別プロンプトを管理する。
/// 起動時に SeedIfEmptyAsync を呼び出すことで、テーブルが空の場合に 4 ロール分の行を自動投入する。
/// </summary>
public sealed class SqlRolePromptStore : IRolePromptStore
{
    private readonly string _connectionString;

    private static readonly string[] ValidRoles = ["Sales", "Purchasing", "Production", "Accounting"];

    public SqlRolePromptStore(IOptions<OpsContextOptions> opts)
    {
        _connectionString = opts.Value.SqlConnectionString;
    }

    public async Task<string?> GetPromptAsync(string role, CancellationToken ct = default)
    {
        const string sql = "SELECT Prompt FROM RolePrompts WHERE Role = @Role";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@Role", role));
        cmd.CommandTimeout = 10;

        var result = await cmd.ExecuteScalarAsync(ct);
        var prompt = result is DBNull or null ? null : (string?)result;
        return string.IsNullOrWhiteSpace(prompt) ? null : prompt;
    }

    public async Task<IReadOnlyList<RolePromptEntry>> ListAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT Role, Prompt, UpdatedAt FROM RolePrompts ORDER BY Role
            """;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.CommandTimeout = 10;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<RolePromptEntry>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new RolePromptEntry(
                Role:      reader.GetString(0),
                Prompt:    reader.IsDBNull(1) ? null : reader.GetString(1),
                UpdatedAt: reader.GetDateTime(2)));
        }
        return results;
    }

    public async Task SetPromptAsync(string role, string? prompt, CancellationToken ct = default)
    {
        // MERGE で UPSERT
        const string sql = """
            MERGE RolePrompts AS target
            USING (SELECT @Role AS Role, @Prompt AS Prompt, SYSUTCDATETIME() AS UpdatedAt) AS source
            ON target.Role = source.Role
            WHEN MATCHED THEN
                UPDATE SET Prompt = source.Prompt, UpdatedAt = source.UpdatedAt
            WHEN NOT MATCHED THEN
                INSERT (Role, Prompt, UpdatedAt) VALUES (source.Role, source.Prompt, source.UpdatedAt);
            """;

        var trimmed = string.IsNullOrWhiteSpace(prompt) ? null : prompt.Trim();

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@Role",   role));
        cmd.Parameters.Add(new SqlParameter("@Prompt",
            trimmed is null ? DBNull.Value : (object)trimmed));
        cmd.CommandTimeout = 10;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // -----------------------------------------------------------------------
    // 起動時シード (RolePrompts テーブルが空の場合のみ投入)
    // -----------------------------------------------------------------------

    /// <summary>
    /// RolePrompts テーブルが空であれば 4 ロール分の空行を投入する。冪等。
    /// Program.cs の app.Lifetime.ApplicationStarted から呼び出す。
    /// </summary>
    public async Task SeedIfEmptyAsync(CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using (var countCmd = new SqlCommand("SELECT COUNT(*) FROM RolePrompts", conn))
        {
            var count = (int)(await countCmd.ExecuteScalarAsync(ct))!;
            if (count > 0) return;
        }

        const string insertSql = """
            INSERT INTO RolePrompts (Role) VALUES (@Role)
            """;

        foreach (var role in ValidRoles)
        {
            await using var cmd = new SqlCommand(insertSql, conn);
            cmd.Parameters.Add(new SqlParameter("@Role", role));
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
