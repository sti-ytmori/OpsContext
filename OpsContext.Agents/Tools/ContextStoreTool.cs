using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpsContext.Agents.Models;
using OpsContext.Agents.Options;

namespace OpsContext.Agents.Tools;

// design 05「IContextStoreTool」実装クラス。
// SQL を正規の記録先、AI Search を検索用ミラーとして使い分ける。
// AI Search upsert はベストエフォート（失敗しても SQL コミットを維持）。
public sealed class ContextStoreTool : IContextStoreTool
{
    private readonly string _connectionString;
    private readonly IAiSearchTool _aiSearch;
    private readonly ILogger<ContextStoreTool> _logger;

    public ContextStoreTool(
        IOptions<OpsContextOptions> opts,
        IAiSearchTool aiSearch,
        ILogger<ContextStoreTool> logger)
    {
        _connectionString = opts.Value.SqlConnectionString;
        _aiSearch = aiSearch;
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // ReadCaseContextAsync
    // roleViewpoint 指定時: Role = @roleViewpoint OR Kind = 'handoff' で絞り込む。
    // null 時: 全件取得。
    // -----------------------------------------------------------------------
    public async Task<IReadOnlyList<ContextEntry>> ReadCaseContextAsync(
        string caseId, string? roleViewpoint, CancellationToken ct)
    {
        string sql;
        SqlParameter[] parameters;

        if (roleViewpoint is not null)
        {
            sql = """
                SELECT
                    EntryId, CaseId, Role, Author, Kind,
                    Text, RefSql, CreatedAt
                FROM ContextEntries
                WHERE CaseId = @CaseId
                  AND (Role = @RoleViewpoint OR Kind = 'handoff')
                ORDER BY CreatedAt
                """;
            parameters =
            [
                new SqlParameter("@CaseId",        caseId),
                new SqlParameter("@RoleViewpoint", roleViewpoint)
            ];
        }
        else
        {
            sql = """
                SELECT
                    EntryId, CaseId, Role, Author, Kind,
                    Text, RefSql, CreatedAt
                FROM ContextEntries
                WHERE CaseId = @CaseId
                ORDER BY CreatedAt
                """;
            parameters =
            [
                new SqlParameter("@CaseId", caseId)
            ];
        }

        _logger.LogDebug(
            "ReadCaseContextAsync: caseId={CaseId} roleViewpoint={RoleViewpoint}",
            caseId, roleViewpoint);

        return await ReadEntriesAsync(sql, parameters, ct);
    }

    // -----------------------------------------------------------------------
    // AppendDecisionAsync
    // Kind = 'decision' でエントリを追加し、EntryId を返す。
    // CaseId が Cases に存在しなければ INSERT する。
    // INSERT 後に AI Search へ upsert し、EmbeddingId を UPDATE する（ベストエフォート）。
    // -----------------------------------------------------------------------
    public async Task<string> AppendDecisionAsync(
        string caseId, string role, string author, string text, CancellationToken ct)
    {
        _logger.LogDebug(
            "AppendDecisionAsync: caseId={CaseId} role={Role} author={Author}",
            caseId, role, author);

        return await AppendEntryAsync(caseId, role, author, "decision", text, ct);
    }

    // -----------------------------------------------------------------------
    // AppendObservationAsync
    // Kind = 'observation' でエントリを追加し、EntryId を返す。
    // INSERT 後に AI Search へ upsert し、EmbeddingId を UPDATE する（ベストエフォート）。
    // -----------------------------------------------------------------------
    public async Task<string> AppendObservationAsync(
        string caseId, string role, string author, string text, CancellationToken ct)
    {
        _logger.LogDebug(
            "AppendObservationAsync: caseId={CaseId} role={Role} author={Author}",
            caseId, role, author);

        return await AppendEntryAsync(caseId, role, author, "observation", text, ct);
    }

    // -----------------------------------------------------------------------
    // AppendHandoffAsync
    // Kind = 'handoff' でエントリを追加し、EntryId を返す。
    // roleViewpoint フィルタに関係なく全ロールから参照可能なエントリ。
    // CuratorAgent がロール横断の文脈要約を保存するために使う。
    // -----------------------------------------------------------------------
    public async Task<string> AppendHandoffAsync(
        string caseId, string role, string author, string text, CancellationToken ct)
    {
        _logger.LogDebug(
            "AppendHandoffAsync: caseId={CaseId} role={Role} author={Author}",
            caseId, role, author);

        return await AppendEntryAsync(caseId, role, author, "handoff", text, ct);
    }

    // -----------------------------------------------------------------------
    // ReadLatestFocusSnapshotAsync
    // 指定ロールの最新スナップショット（CreatedAt DESC TOP 1）の SummaryMd を返す。
    // -----------------------------------------------------------------------
    public async Task<string?> ReadLatestFocusSnapshotAsync(
        string caseId, string role, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP 1 SummaryMd
            FROM FocusSnapshots
            WHERE CaseId = @CaseId AND Role = @Role
            ORDER BY CreatedAt DESC
            """;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@CaseId", caseId));
        cmd.Parameters.Add(new SqlParameter("@Role",   role));
        cmd.CommandTimeout = 30;

        var result = await cmd.ExecuteScalarAsync(ct);
        return result as string;
    }

    // -----------------------------------------------------------------------
    // InsertFocusSnapshotAsync
    // FocusSnapshots テーブルに新規行を INSERT する（追記、上書きしない）。
    // -----------------------------------------------------------------------
    public async Task InsertFocusSnapshotAsync(
        string caseId, string role, string summaryMd, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO FocusSnapshots (SnapshotId, CaseId, Role, SummaryMd, CreatedAt)
            VALUES (NEWID(), @CaseId, @Role, @SummaryMd, SYSUTCDATETIME())
            """;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@CaseId",    caseId));
        cmd.Parameters.Add(new SqlParameter("@Role",      role));
        cmd.Parameters.Add(new SqlParameter("@SummaryMd", summaryMd));
        cmd.CommandTimeout = 30;

        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogInformation(
            "FocusSnapshot inserted: caseId={CaseId} role={Role}", caseId, role);
    }

    // -----------------------------------------------------------------------
    // 共通 INSERT ロジック
    // 1) ContextEntries に INSERT (NewGuid EntryId)
    // 2) AI Search upsert（失敗しても catch してログ記録、SQL コミットを維持）
    // 3) upsert 後の EmbeddingId を ContextEntries に UPDATE
    // -----------------------------------------------------------------------
    private async Task<string> AppendEntryAsync(
        string caseId, string role, string author, string kind, string text, CancellationToken ct)
    {
        var entryId = Guid.NewGuid();

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // --- Step 1: ContextEntries に INSERT ---
        const string insertEntry = """
            INSERT INTO ContextEntries
                (EntryId, CaseId, Role, Author, Kind, Text, RefSql, CreatedAt, EmbeddingId)
            VALUES
                (@EntryId, @CaseId, @Role, @Author, @Kind, @Text, NULL, SYSUTCDATETIME(), NULL)
            """;

        await using (var cmd = new SqlCommand(insertEntry, conn))
        {
            cmd.Parameters.Add(new SqlParameter("@EntryId", entryId));
            cmd.Parameters.Add(new SqlParameter("@CaseId",  caseId));
            cmd.Parameters.Add(new SqlParameter("@Role",    role));
            cmd.Parameters.Add(new SqlParameter("@Author",  author));
            cmd.Parameters.Add(new SqlParameter("@Kind",    kind));
            cmd.Parameters.Add(new SqlParameter("@Text",    text));
            cmd.CommandTimeout = 30;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        _logger.LogInformation(
            "ContextEntry inserted: entryId={EntryId} caseId={CaseId} kind={Kind}",
            entryId, caseId, kind);

        // --- Step 2: AI Search upsert（ベストエフォート） ---
        string? embeddingId = null;
        try
        {
            // CustomerCode は Cases テーブルから取得するが、
            // 初回 INSERT 直後で空の場合があるため空文字で渡す（後続で更新可）
            embeddingId = await _aiSearch.UpsertContextEntryAsync(
                entryId.ToString(), caseId, role, kind,
                customerCode: "",
                text: text,
                ct: ct);

            _logger.LogInformation(
                "AI Search upsert succeeded: entryId={EntryId} embeddingId={EmbeddingId}",
                entryId, embeddingId);
        }
        catch (Exception ex)
        {
            // ミラーの失敗は SQL コミットに影響しない
            _logger.LogWarning(ex,
                "AI Search upsert failed (best-effort): entryId={EntryId}. SQL commit maintained.",
                entryId);
        }

        // --- Step 3: EmbeddingId を UPDATE（upsert 成功時のみ） ---
        if (embeddingId is not null)
        {
            const string updateEmbedding = """
                UPDATE ContextEntries
                SET EmbeddingId = @EmbeddingId
                WHERE EntryId = @EntryId
                """;

            try
            {
                await using var cmd = new SqlCommand(updateEmbedding, conn);
                cmd.Parameters.Add(new SqlParameter("@EmbeddingId", embeddingId));
                cmd.Parameters.Add(new SqlParameter("@EntryId",     entryId));
                cmd.CommandTimeout = 30;
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "EmbeddingId UPDATE failed: entryId={EntryId}. Non-fatal.", entryId);
            }
        }

        return entryId.ToString();
    }

    // -----------------------------------------------------------------------
    // 共通クエリヘルパ: SELECT 結果を ContextEntry リストに変換する。
    // -----------------------------------------------------------------------
    private async Task<IReadOnlyList<ContextEntry>> ReadEntriesAsync(
        string sql, IEnumerable<SqlParameter> parameters, CancellationToken ct)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new SqlCommand(sql, conn);
        cmd.CommandTimeout = 30;

        foreach (var p in parameters)
            cmd.Parameters.Add(p);

        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var entries = new List<ContextEntry>();
        while (await reader.ReadAsync(ct))
        {
            entries.Add(new ContextEntry(
                EntryId:   reader.GetGuid(0).ToString(),
                CaseId:    reader.GetString(1),
                Role:      reader.GetString(2),
                Author:    reader.GetString(3),
                Kind:      reader.GetString(4),
                Text:      reader.GetString(5),
                RefSql:    reader.IsDBNull(6) ? null : reader.GetString(6),
                CreatedAt: new DateTimeOffset(reader.GetDateTime(7), TimeSpan.Zero)
            ));
        }

        return entries;
    }
}
