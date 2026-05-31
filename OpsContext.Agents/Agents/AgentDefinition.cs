namespace OpsContext.Agents.Agents;

// design 04: AgentDefinition — Curia AgentHubModels.cs の AgentDefinition を
// OpsContext 向けに移植。systemPrompt と tools フィールドを追加。
public class AgentDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string SystemPrompt { get; set; } = "";
    public List<string> Tools { get; set; } = [];
}
