using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace OpsContext.Agents.Agents;

// design 04: AgentDefinitionLoader — agents/*.json を読み込む static クラス。
// Curia AgentHubService パターンを移植。個別耐性あり・名前順ソート。
public static class AgentDefinitionLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// 指定ディレクトリ内の *.json をすべて読み込み AgentDefinition のリストを返す。
    /// デシリアライズに失敗したファイルは読み飛ばす（個別耐性）。
    /// 返すリストは Name 昇順でソートされる。
    /// </summary>
    public static IReadOnlyList<AgentDefinition> LoadAll(string agentsDir)
    {
        if (!Directory.Exists(agentsDir))
            return [];

        var result = new List<AgentDefinition>();

        foreach (var jsonFile in Directory.GetFiles(agentsDir, "*.json"))
        {
            try
            {
                var content = File.ReadAllText(jsonFile, new UTF8Encoding(false));
                var def = JsonSerializer.Deserialize<AgentDefinition>(content, JsonOptions);
                if (def != null)
                    result.Add(def);
            }
            catch (Exception ex)
            {
                // 個別耐性: 読み込み失敗ファイルはスキップ
                System.Diagnostics.Debug.WriteLine(
                    $"[AgentDefinitionLoader] Failed to load {jsonFile}: {ex.Message}");
            }
        }

        return [.. result.OrderBy(d => d.Name)];
    }
}
