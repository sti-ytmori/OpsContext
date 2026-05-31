using System.Text.RegularExpressions;

namespace OpsContext.Agents.Tools;

// design 02「SELECT-only ホワイトリストガード」。
// LLM が生成した SQL がデータ書き換え・スキーマ操作を含まないことを保証する。
public static class SqlReadOnlyGuard
{
    // ERP 7テーブルのホワイトリスト（ContextStore テーブルは別ツールの責務）
    private static readonly HashSet<string> AllowedTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "GridRows", "GridTables"
    };

    // SELECT 以外の危険キーワード（単語境界でマッチ）
    private static readonly string[] ForbiddenKeywords =
    [
        "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "CREATE",
        "EXEC", "EXECUTE", "TRUNCATE", "MERGE", "GRANT", "REVOKE"
    ];

    /// <summary>
    /// 与えられた SQL 文字列を検証する。違反があれば <see cref="InvalidOperationException"/> をスローする。
    /// </summary>
    public static void Validate(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            throw new InvalidOperationException("SQL が空です。");

        // 1. 先頭トークンが SELECT であること
        var firstToken = sql.TrimStart().Split(new[] { ' ', '\t', '\r', '\n' }, 2)[0];
        if (!firstToken.Equals("SELECT", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"SQL は SELECT で始まる必要があります。先頭トークン: '{firstToken}'");

        // 2. 禁止キーワードが含まれないこと
        foreach (var keyword in ForbiddenKeywords)
        {
            if (Regex.IsMatch(sql, $@"\b{keyword}\b", RegexOptions.IgnoreCase))
                throw new InvalidOperationException(
                    $"SQL に禁止キーワード '{keyword}' が含まれています。");
        }

        // 3. FROM / JOIN 句のテーブル名がホワイトリストのみであること
        // FROM tableName または JOIN tableName にマッチ
        var tableRefs = Regex.Matches(sql,
            @"\b(?:FROM|JOIN)\s+(\[?\w+\]?)",
            RegexOptions.IgnoreCase);

        foreach (Match m in tableRefs)
        {
            var tableName = m.Groups[1].Value.Trim('[', ']');
            if (!AllowedTables.Contains(tableName))
                throw new InvalidOperationException(
                    $"テーブル '{tableName}' はホワイトリストに含まれていません。" +
                    $"許可テーブル: {string.Join(", ", AllowedTables)}");
        }
    }
}
