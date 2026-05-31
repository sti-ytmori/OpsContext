using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using OpsContext.Agents.Options;

namespace OpsContext.Agents.Tools;

// 本番モード用 DemoService。
// Azure SQL に直接 INSERT/DELETE してサンプルデータを管理する。
public sealed class ProdDemoService : IDemoService
{
    private readonly string _connectionString;

    // シードデータで使う CaseId は GridTables の TableId と一致させる。
    // Data.razor は _caseId = TableId で ContextEntries を引くため、
    // GUID ではなく実在の TableId でないと UI に表示されない。
    private static readonly string[] SeedCaseIds =
    [
        "erp_orders",    // Case 1: A商事 受注検討（進行中）  Case 3: C製造 定期発注（完了）
        "erp_inventory", // Case 2: B製造 緊急調達（保留）
    ];

    public ProdDemoService(IOptions<OpsContextOptions> opts)
    {
        _connectionString = opts.Value.SqlConnectionString;
    }

    public bool IsAvailable => true;

    // -----------------------------------------------------------------------
    // SeedSampleDataAsync — ContextEntries にサンプル行を INSERT する
    // 既に存在する場合はスキップ（冪等）
    // -----------------------------------------------------------------------
    public async Task SeedSampleDataAsync(CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // 既にシード済みならスキップ（冪等）
        await using (var checkCmd = new SqlCommand(
            "SELECT COUNT(1) FROM ContextEntries WHERE CaseId IN ('erp_orders','erp_inventory')", conn)
            { CommandTimeout = 30 })
        {
            var existing = (int)await checkCmd.ExecuteScalarAsync(ct);
            if (existing > 0) return;
        }

        var now = DateTimeOffset.UtcNow;

        // ─── Case 1: A商事 弁P-101 150個受注検討（進行中）
        var c1 = SeedCaseIds[0]; // "erp_orders"
        await InsertEntryIfNotExistsAsync(conn, c1, "Sales", "SalesAgent", "observation",
            "A商事の与信枠を確認。与信限度額5,000万円に対し使用額3,800万円（使用率76%）。残与信枠1,200万円。弁P-101 150個（単価8万円）の受注金額は1,200万円であり、残与信枠と同額で与信枠を満限まで使用する水準。",
            now.AddDays(-2).AddHours(-3), ct);
        await InsertEntryIfNotExistsAsync(conn, c1, "Sales", "SalesAgent", "observation",
            "弁P-101の在庫状況を確認。現在庫80個（安全在庫50個）、引当済み0個。利用可能在庫80個。安全在庫を超える在庫があるため、即時出荷分については問題なし。",
            now.AddDays(-2).AddHours(-2), ct);
        await InsertEntryIfNotExistsAsync(conn, c1, "Production", "ProductionAgent", "observation",
            "直近2週間の生産能力を確認。14日間で合計121個の生産枠あり。在庫80個と合わせると最大201個の出荷が可能。150個の受注は在庫80個＋生産70個でカバーでき、標準リードタイム30日以内に納品可能。",
            now.AddDays(-2).AddHours(-1), ct);
        await InsertEntryIfNotExistsAsync(conn, c1, "Accounting", "AccountingAgent", "observation",
            "A商事の直近3か月の支払履歴を確認。2026年3月に5日超の支払遅延実績あり。追加保証金（受注金額の10%＝120万円）の提供を要求するか、前払い条件への切替が必要。",
            now.AddDays(-1).AddHours(-5), ct);
        await InsertEntryIfNotExistsAsync(conn, c1, "Accounting", "AccountingAgent", "decision",
            "支払遅延履歴に基づき、受注金額1,200万円の10%相当（120万円）の追加保証金を要求する。または営業部長・経理部長の連名承認を得たうえで前払い条件へ切替を行う。どちらの対応を採るか営業担当と協議が必要。",
            now.AddDays(-1).AddHours(-4), ct);
        await InsertEntryIfNotExistsAsync(conn, c1, "Sales", "OrchestratorAgent", "handoff",
            "【Curator まとめ】A商事 弁P-101 150個の受注検討案件。在庫・生産能力はともに充足しており、納期リスクなし。受注金額1,200万円は残与信枠と同額で与信枠を満限使用。3月の支払遅延履歴（5日超）に基づき追加保証金120万円の要求が発生。営業担当と経理担当が協議中。",
            now.AddDays(-1).AddHours(-2), ct);

        // ─── Case 2: B製造 緊急調達案件（保留）
        var c2 = SeedCaseIds[1]; // "erp_inventory"
        await InsertEntryIfNotExistsAsync(conn, c2, "Purchasing", "PurchasingAgent", "observation",
            "弁P-101の現在庫は80個。過去3か月の出荷実績から月間平均需要は約60個。安全在庫50個を下回るには約0.5か月後の見込み。B製造から150個の緊急発注依頼が入った場合、在庫不足が確定する。",
            now.AddDays(-5).AddHours(-6), ct);
        await InsertEntryIfNotExistsAsync(conn, c2, "Purchasing", "PurchasingAgent", "observation",
            "サプライヤーAへの緊急手配を確認。最短3日出荷が可能だが通常価格の120%が必要。発注量が月間在庫の30%以内であることも確認済み。購買部長の口頭承認を取得予定。",
            now.AddDays(-5).AddHours(-5), ct);
        await InsertEntryIfNotExistsAsync(conn, c2, "Purchasing", "PurchasingAgent", "decision",
            "B製造の緊急調達要件に対応するため、サプライヤーAへの緊急発注（100個、通常比120%）を推奨する。ただしB製造の最終発注確認が保留中のため、承認取得後に実発注を行う。",
            now.AddDays(-4).AddHours(-3), ct);
        await InsertEntryIfNotExistsAsync(conn, c2, "Purchasing", "OrchestratorAgent", "handoff",
            "【Curator まとめ】B製造 緊急調達案件。サプライヤーA経由で緊急手配可能だが、B製造側の最終発注確認待ちのため案件は保留状態。購買部長の承認は取得次第実施予定。",
            now.AddDays(-4).AddHours(-1), ct);

        // ─── Case 3: C製造 定期発注 Q1（完了）
        var c3 = SeedCaseIds[0]; // "erp_orders"（Case 1 と同じテーブル、累積ログに追記）
        await InsertEntryIfNotExistsAsync(conn, c3, "Sales", "SalesAgent", "observation",
            "Cフードから弁P-101 80個の定期発注を受領。与信枠2,000万円に対し使用額380万円（使用率19%）。残与信枠1,620万円で受注金額640万円は余裕で収まる。",
            now.AddDays(-10).AddHours(-8), ct);
        await InsertEntryIfNotExistsAsync(conn, c3, "Production", "ProductionAgent", "observation",
            "弁P-101 80個の生産スケジュールを確認。現在庫80個で即時出荷可能。生産枠を消費せず在庫のみで対応できるため、他の受注への影響なし。",
            now.AddDays(-10).AddHours(-7), ct);
        await InsertEntryIfNotExistsAsync(conn, c3, "Sales", "SalesAgent", "decision",
            "Cフードへの弁P-101 80個の受注を承認。与信枠内、在庫充足、生産枠未使用。標準納期で処理する。受注確定・出荷手配を営業事務に指示。",
            now.AddDays(-9).AddHours(-6), ct);
        await InsertEntryIfNotExistsAsync(conn, c3, "Accounting", "AccountingAgent", "observation",
            "Cフードの支払履歴を確認。直近12か月すべて期日内に入金。次回四半期与信見直し時に与信枠増額を検討予定。",
            now.AddDays(-8).AddHours(-4), ct);
        await InsertEntryIfNotExistsAsync(conn, c3, "Sales", "OrchestratorAgent", "handoff",
            "【Curator まとめ】Cフード 定期発注 Q1完了。弁P-101 80個を在庫のみで充足。与信・入金ともに問題なし。出荷手配完了・請求書発行済み。案件クローズ。",
            now.AddDays(-7).AddHours(-2), ct);
    }

    // -----------------------------------------------------------------------
    // ResetAsync — サンプルデータの ContextEntries / FocusSnapshots を削除
    // -----------------------------------------------------------------------
    public async Task ResetAsync(CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var ids = string.Join(",", SeedCaseIds.Select(id => $"'{id}'"));

        foreach (var table in new[] { "FocusSnapshots", "ContextEntries" })
        {
            var sql = $"DELETE FROM {table} WHERE CaseId IN ({ids})";
            await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    // -----------------------------------------------------------------------
    // ヘルパー
    // -----------------------------------------------------------------------
    private static async Task InsertEntryIfNotExistsAsync(
        SqlConnection conn, string caseId, string role, string author,
        string kind, string text, DateTimeOffset at, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO ContextEntries
                (EntryId, CaseId, Role, Author, Kind, Text, RefSql, CreatedAt, EmbeddingId)
            VALUES
                (NEWID(), @CaseId, @Role, @Author, @Kind, @Text, NULL, @At, NULL)
            """;
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        cmd.Parameters.AddWithValue("@CaseId",  caseId);
        cmd.Parameters.AddWithValue("@Role",    role);
        cmd.Parameters.AddWithValue("@Author",  author);
        cmd.Parameters.AddWithValue("@Kind",    kind);
        cmd.Parameters.AddWithValue("@Text",    text);
        cmd.Parameters.AddWithValue("@At",      at.UtcDateTime);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
