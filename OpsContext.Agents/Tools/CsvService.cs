using System.Text;
using OpsContext.Agents.Models;

namespace OpsContext.Agents.Tools;

// ParseQuoteLines: ヘッダー行をスキップし、品番/品名/数量/希望納期/単価 の5列を読む。
// ExportWithVerdicts: UTF-8 BOM付きCSVで出力 (Excelで直接開けるよう)。
public sealed class CsvService : ICsvTool
{
    public IReadOnlyList<QuoteLine> ParseQuoteLines(Stream csv)
    {
        using var reader = new StreamReader(csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var lines = new List<QuoteLine>();

        var header = reader.ReadLine();
        if (header is null) return lines;

        string? raw;
        while ((raw = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var cols = SplitCsvLine(raw);
            if (cols.Length < 5) continue;

            try
            {
                var productCode = cols[0].Trim();
                if (string.IsNullOrEmpty(productCode)) continue;

                var productName = cols[1].Trim();

                if (!int.TryParse(cols[2].Trim(), out var qty)) continue;

                if (!DateOnly.TryParse(cols[3].Trim(), out var requestedDate)) continue;

                if (!decimal.TryParse(cols[4].Trim(), out var unitPrice)) continue;

                lines.Add(new QuoteLine
                {
                    ProductCode   = productCode,
                    ProductName   = productName,
                    Qty           = qty,
                    RequestedDate = requestedDate,
                    UnitPrice     = unitPrice,
                });
            }
            catch
            {
                // 型変換失敗行はスキップ
            }
        }

        return lines;
    }

    public byte[] ExportWithVerdicts(IReadOnlyList<QuoteLine> lines)
    {
        var sb = new StringBuilder();
        sb.AppendLine("品番,品名,数量,希望納期,単価,判定,リスク分類,推奨アクション,根拠参照");

        foreach (var line in lines)
        {
            sb.AppendLine(string.Join(",",
                Escape(line.ProductCode),
                Escape(line.ProductName),
                line.Qty.ToString(),
                line.RequestedDate.ToString("yyyy-MM-dd"),
                line.UnitPrice.ToString(),
                Escape(line.Verdict.ToString()),
                Escape(line.RiskLevel     ?? ""),
                Escape(line.Recommendation ?? ""),
                Escape(line.RefNote       ?? "")));
        }

        // BOM付きUTF-8 — Excelで直接開けるよう
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static string[] SplitCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuote = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuote)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inQuote = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"') { inQuote = true; }
                else if (c == ',') { result.Add(current.ToString()); current.Clear(); }
                else { current.Append(c); }
            }
        }
        result.Add(current.ToString());
        return result.ToArray();
    }
}
