using OpsContext.Agents.Models;

namespace OpsContext.Agents.Tools;

public interface ICsvTool
{
    IReadOnlyList<QuoteLine> ParseQuoteLines(Stream csv);
    byte[] ExportWithVerdicts(IReadOnlyList<QuoteLine> lines);
}
