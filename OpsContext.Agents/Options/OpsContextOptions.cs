namespace OpsContext.Agents.Options;

// plan.md「Tool 層シグネチャ」/ design 04 の OpsContextOptions を流用。
// appsettings の "OpsContext" セクションにバインドする。
public sealed class OpsContextOptions
{
    public AzureOpenAiOptions AzureOpenAi { get; set; } = new();
    public string SqlConnectionString { get; set; } = "";
    public AiSearchOptions AiSearch { get; set; } = new();
    public string BlobConnectionString { get; set; } = "";
}

public sealed class AzureOpenAiOptions
{
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string ChatDeployment { get; set; } = "gpt-4o";
    public string EmbeddingDeployment { get; set; } = "embedding-small";
}

public sealed class AiSearchOptions
{
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string KnowledgeIndex { get; set; } = "opscontext-knowledge";
    public string ContextIndex { get; set; } = "opscontext-context";
}
