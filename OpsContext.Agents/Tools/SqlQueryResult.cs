using System.Text;

namespace OpsContext.Agents.Tools;

// design 02「SqlQueryResult 型の定義方針」。
// 汎用 Columns/Rows 構造でエージェントへのシリアライズを統一する。
public sealed record SqlQueryResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    int TotalRows);

public static class SqlQueryResultExtensions
{
    /// <summary>
    /// エージェントのプロンプトに埋め込むための Markdown テーブル文字列に変換する。
    /// </summary>
    public static string ToMarkdownTable(this SqlQueryResult result)
    {
        if (result.Columns.Count == 0) return "(empty)";

        var sb = new StringBuilder();

        // ヘッダー行
        sb.Append('|');
        foreach (var col in result.Columns)
            sb.Append(col).Append('|');
        sb.AppendLine();

        // 区切り行
        sb.Append('|');
        foreach (var _ in result.Columns)
            sb.Append("---|");
        sb.AppendLine();

        // データ行
        foreach (var row in result.Rows)
        {
            sb.Append('|');
            foreach (var cell in row)
            {
                var val = cell?.ToString()?.Replace("|", "\\|") ?? "";
                sb.Append(val).Append('|');
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
