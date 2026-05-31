using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpsContext.Agents.Options;

namespace OpsContext.Web.Services;

/// <summary>
/// 本番用 SqlLoginHistoryStore。LoginHistory テーブルに認証試行を記録する。
/// スキーマは EnsureSchemaAsync が起動時に自動作成する（既存テーブルは保持）。
/// </summary>
public sealed class SqlLoginHistoryStore : ILoginHistoryStore
{
    private readonly string _connectionString;
    private readonly ILogger<SqlLoginHistoryStore> _logger;

    private bool _schemaEnsured;
    private readonly SemaphoreSlim _schemaSemaphore = new(1, 1);

    public SqlLoginHistoryStore(
        IOptions<OpsContextOptions> opts,
        ILogger<SqlLoginHistoryStore> logger)
    {
        _connectionString = opts.Value.SqlConnectionString;
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // スキーマ自動作成（冪等: DROP なし、CREATE IF NOT EXISTS）
    // -----------------------------------------------------------------------

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        if (_schemaEnsured) return;

        await _schemaSemaphore.WaitAsync(ct);
        try
        {
            if (_schemaEnsured) return;

            const string ddl = """
                IF OBJECT_ID('LoginHistory', 'U') IS NULL
                BEGIN
                    CREATE TABLE LoginHistory (
                        Id          BIGINT          NOT NULL IDENTITY(1,1),
                        UserName    NVARCHAR(50)    NOT NULL,
                        DisplayName NVARCHAR(100)   NULL,
                        Role        NVARCHAR(20)    NULL,
                        Success     BIT             NOT NULL,
                        ClientIp    NVARCHAR(64)    NULL,
                        UserAgent   NVARCHAR(512)   NULL,
                        CreatedAt   DATETIME2(0)    NOT NULL DEFAULT SYSUTCDATETIME(),
                        CONSTRAINT PK_LoginHistory PRIMARY KEY (Id)
                    );
                    CREATE INDEX IX_LoginHistory_CreatedAt ON LoginHistory (CreatedAt DESC);
                    CREATE INDEX IX_LoginHistory_UserName  ON LoginHistory (UserName);
                END
                """;

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand(ddl, conn) { CommandTimeout = 30 };
            await cmd.ExecuteNonQueryAsync(ct);

            _schemaEnsured = true;
            _logger.LogInformation("SqlLoginHistoryStore: LoginHistory スキーマを確認しました。");
        }
        finally
        {
            _schemaSemaphore.Release();
        }
    }

    // -----------------------------------------------------------------------
    // ILoginHistoryStore 実装
    // -----------------------------------------------------------------------

    public async Task RecordAsync(
        string  userName,
        string? displayName,
        string? role,
        bool    success,
        string? clientIp,
        string? userAgent,
        CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO LoginHistory (UserName, DisplayName, Role, Success, ClientIp, UserAgent)
            VALUES (@UserName, @DisplayName, @Role, @Success, @ClientIp, @UserAgent)
            """;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 10 };

        cmd.Parameters.Add(new SqlParameter("@UserName",    userName));
        cmd.Parameters.Add(new SqlParameter("@DisplayName", (object?)displayName ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Role",        (object?)role        ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Success",     success));
        cmd.Parameters.Add(new SqlParameter("@ClientIp",   (object?)clientIp    ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@UserAgent",   (object?)userAgent   ?? DBNull.Value));

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<LoginHistoryEntry>> ListRecentAsync(int take = 200, CancellationToken ct = default)
    {
        const string sql = """
            SELECT TOP (@Take) Id, UserName, DisplayName, Role, Success, ClientIp, UserAgent, CreatedAt
            FROM LoginHistory
            ORDER BY CreatedAt DESC
            """;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 10 };
        cmd.Parameters.Add(new SqlParameter("@Take", take));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<LoginHistoryEntry>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new LoginHistoryEntry(
                UserName:    reader.GetString(1),
                DisplayName: reader.IsDBNull(2) ? null : reader.GetString(2),
                Role:        reader.IsDBNull(3) ? null : reader.GetString(3),
                Success:     reader.GetBoolean(4),
                ClientIp:    reader.IsDBNull(5) ? null : reader.GetString(5),
                UserAgent:   reader.IsDBNull(6) ? null : reader.GetString(6),
                CreatedAt:   reader.GetDateTime(7)));
        }
        return results;
    }
}
