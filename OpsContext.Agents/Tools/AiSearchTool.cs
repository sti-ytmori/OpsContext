using Azure;
using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpsContext.Agents.Models;
using OpsContext.Agents.Options;

namespace OpsContext.Agents.Tools;

// design 03: AiSearchTool — IAiSearchTool の本番実装。
// Azure AI Search の2インデックス（opscontext-knowledge / opscontext-context）に
// ベクトル検索をかける。embedding は embedding-small (text-embedding-3-small) を使用。
public sealed class AiSearchTool : IAiSearchTool
{
    private readonly OpsContextOptions _opts;
    private readonly ILogger<AiSearchTool> _logger;
    private readonly AzureOpenAIClient _openAiClient;
    private readonly SearchClient _knowledgeClient;
    private readonly SearchClient _contextClient;

    public AiSearchTool(IOptions<OpsContextOptions> options, ILogger<AiSearchTool> logger)
    {
        _opts = options.Value;
        _logger = logger;

        _openAiClient = new AzureOpenAIClient(
            new Uri(_opts.AzureOpenAi.Endpoint),
            new AzureKeyCredential(_opts.AzureOpenAi.ApiKey));

        var searchEndpoint = new Uri(_opts.AiSearch.Endpoint);
        var searchCredential = new AzureKeyCredential(_opts.AiSearch.ApiKey);

        _knowledgeClient = new SearchClient(searchEndpoint, _opts.AiSearch.KnowledgeIndex, searchCredential);
        _contextClient = new SearchClient(searchEndpoint, _opts.AiSearch.ContextIndex, searchCredential);
    }

    // -----------------------------------------------------------------------
    // 共通ヘルパ
    // -----------------------------------------------------------------------

    private async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct)
    {
        var embeddingClient = _openAiClient.GetEmbeddingClient(_opts.AzureOpenAi.EmbeddingDeployment);
        var result = await embeddingClient.GenerateEmbeddingAsync(text, cancellationToken: ct);
        return result.Value.ToFloats().ToArray();
    }

    private static string EscapeSingleQuote(string value)
        => value.Replace("'", "''");

    // -----------------------------------------------------------------------
    // SearchKnowledgeAsync
    // -----------------------------------------------------------------------

    public async Task<IReadOnlyList<AiSearchHit>> SearchKnowledgeAsync(
        string query, int topK, string? roleFilter, CancellationToken ct)
    {
        _logger.LogDebug("SearchKnowledgeAsync: query={Query} topK={TopK} roleFilter={RoleFilter}",
            query, topK, roleFilter);

        var vector = await GenerateEmbeddingAsync(query, ct);

        var vectorQuery = new VectorizedQuery(vector)
        {
            KNearestNeighborsCount = topK,
            Fields = { "contentVector" }
        };

        var searchOptions = new SearchOptions
        {
            Size = topK,
            Select = { "id", "content", "role", "category", "createdAt" },
        };
        searchOptions.VectorSearch = new VectorSearchOptions();
        searchOptions.VectorSearch.Queries.Add(vectorQuery);

        if (!string.IsNullOrEmpty(roleFilter))
        {
            var escaped = EscapeSingleQuote(roleFilter);
            searchOptions.Filter = $"role eq '{escaped}' or role eq 'All'";
        }

        var response = await _knowledgeClient.SearchAsync<SearchDocument>(null, searchOptions, ct);

        var hits = new List<AiSearchHit>();
        await foreach (var result in response.Value.GetResultsAsync())
        {
            var doc = result.Document;
            hits.Add(new AiSearchHit
            {
                Score = result.Score ?? 0,
                Id = doc.GetString("id") ?? "",
                Content = doc.GetString("content") ?? "",
                Metadata = new Dictionary<string, string>
                {
                    ["role"] = doc.GetString("role") ?? "",
                    ["category"] = doc.GetString("category") ?? ""
                }
            });
        }

        _logger.LogDebug("SearchKnowledgeAsync: {Count} hits returned", hits.Count);
        return hits;
    }

    // -----------------------------------------------------------------------
    // SearchDecisionLogsAsync
    // -----------------------------------------------------------------------

    public async Task<IReadOnlyList<AiSearchHit>> SearchDecisionLogsAsync(
        string query, string? customerCode, int topK, CancellationToken ct)
    {
        _logger.LogDebug("SearchDecisionLogsAsync: query={Query} topK={TopK} customerCode={CustomerCode}",
            query, topK, customerCode);

        var vector = await GenerateEmbeddingAsync(query, ct);

        var vectorQuery = new VectorizedQuery(vector)
        {
            KNearestNeighborsCount = topK,
            Fields = { "contentVector" }
        };

        var searchOptions = new SearchOptions
        {
            Size = topK,
            Select = { "id", "content", "caseId", "role", "kind", "customerCode", "createdAt" },
        };
        searchOptions.VectorSearch = new VectorSearchOptions();
        searchOptions.VectorSearch.Queries.Add(vectorQuery);

        if (!string.IsNullOrEmpty(customerCode))
        {
            var escaped = EscapeSingleQuote(customerCode);
            searchOptions.Filter = $"customerCode eq '{escaped}'";
        }

        var response = await _contextClient.SearchAsync<SearchDocument>(null, searchOptions, ct);

        var hits = new List<AiSearchHit>();
        await foreach (var result in response.Value.GetResultsAsync())
        {
            var doc = result.Document;
            hits.Add(new AiSearchHit
            {
                Score = result.Score ?? 0,
                Id = doc.GetString("id") ?? "",
                Content = doc.GetString("content") ?? "",
                Metadata = new Dictionary<string, string>
                {
                    ["caseId"] = doc.GetString("caseId") ?? "",
                    ["role"] = doc.GetString("role") ?? "",
                    ["kind"] = doc.GetString("kind") ?? "",
                    ["customerCode"] = doc.GetString("customerCode") ?? ""
                }
            });
        }

        _logger.LogDebug("SearchDecisionLogsAsync: {Count} hits returned", hits.Count);
        return hits;
    }

    // -----------------------------------------------------------------------
    // UpsertContextEntryAsync
    // -----------------------------------------------------------------------

    public async Task<string> UpsertContextEntryAsync(
        string entryId, string caseId, string role, string kind,
        string customerCode, string text, CancellationToken ct)
    {
        _logger.LogDebug("UpsertContextEntryAsync: entryId={EntryId} caseId={CaseId} kind={Kind}",
            entryId, caseId, kind);

        var vector = await GenerateEmbeddingAsync(text, ct);

        var doc = new Dictionary<string, object?>
        {
            ["id"] = entryId,
            ["caseId"] = caseId,
            ["role"] = role,
            ["kind"] = kind,
            ["customerCode"] = customerCode,
            ["content"] = text,
            ["contentVector"] = vector,
            ["createdAt"] = DateTimeOffset.UtcNow
        };

        var batch = IndexDocumentsBatch.MergeOrUpload(new[] { new SearchDocument(doc) });
        var result = await _contextClient.IndexDocumentsAsync(batch, cancellationToken: ct);

        _logger.LogDebug("UpsertContextEntryAsync: upsert result status={Status}", result.Value.Results[0].Status);
        return entryId;
    }
}
