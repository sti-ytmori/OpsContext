using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace OpsContext.Agents.Services;

// モックモード用 IChatClient。
// Azure OpenAI に接続せず応答を返す。
// GRID_AGENT_MODE（ツール付き）の場合は、実 Azure OpenAI の Function Calling と
// 同じプロトコル（FunctionCallContent / FunctionResultContent）を状態機械で再現する。
public sealed class MockChatClient : IChatClient
{
    public ChatClientMetadata Metadata { get; } = new("MockChatClient", null);

    object? IChatClient.GetService(Type serviceType, object? serviceKey)
        => serviceType.IsInstanceOfType(this) ? this : null;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var msgList = messages.ToList();
        var sysMsg  = msgList.FirstOrDefault(m => m.Role == ChatRole.System)?.Text ?? "";
        var userMsg = msgList.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";

        // ─── Grid エージェント（ツール）モード ─────────────────────────────
        if (sysMsg.Contains(OpsContext.Agents.GridAgentService.SystemMarker)
            && options?.Tools is { Count: > 0 })
        {
            return Task.FromResult(GenerateAgentResponse(msgList));
        }

        // ─── 通常チャットモード ─────────────────────────────────────────────
        string reply;
        if (userMsg.Contains("弁P-101") || userMsg.Contains("A001") || userMsg.Contains("A商事"))
        {
            reply = """
                【モックモード - Azure OpenAI 未接続】

                A商事（A001）からの弁P-101 200個の見積依頼を分析しました。

                【与信状況】
                - 与信枠: 5,000万円 / 使用額: 3,800万円 / 残額: 1,200万円（使用率76%）
                - ⚠ 200個（1,600万円）は与信残1,200万円を超過します

                【在庫・生産】
                - 手持在庫: 80個 / 安全在庫: 50個
                - 2週間の生産可能数: 120個（1日8〜9個）

                【推奨アクション】
                1. 第1弾 150個（1,200万円） → 与信残内で対応可
                2. 第2弾 残50個 → 3週目以降に分割受注
                3. 経理部門に与信回収条件の確認を依頼

                Azure OpenAI 接続後は実際の LLM 分析が行われます。
                """;
        }
        else if (string.IsNullOrWhiteSpace(userMsg) || userMsg == "[]")
        {
            reply = "[]";
        }
        else
        {
            reply = $"【モックモード】{userMsg.Length}文字の入力を受け取りました。Azure OpenAI 接続後は実際の業務分析が行われます。";
        }

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var result = await GetResponseAsync(messages, options, cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, result.Text ?? "");
    }

    public void Dispose() { }

    // =======================================================================
    // Grid エージェントモード: ツール呼び出しを状態機械で再現
    // =======================================================================

    private static ChatResponse GenerateAgentResponse(IList<ChatMessage> messages)
    {
        // これまでに実行済みのツール結果の件数 = 進行ステップ
        var done = messages
            .SelectMany(m => m.Contents)
            .OfType<FunctionResultContent>()
            .Count();

        var productCode = ExtractProductCode(messages) ?? "弁P-101";
        var rowId       = ExtractFirstRowId(messages);

        return done switch
        {
            0 => Call("describe_table", new()),
            1 => Call("read_rows", new() { ["limit"] = 10 }),
            2 => Call("get_product_inventory", new() { ["productCode"] = productCode }),
            3 => Call("get_customer_credit", new() { ["customerCode"] = "A001" }),
            4 => Call("get_production_capacity", new() { ["productCode"] = productCode }),
            5 => Call("evaluate_quote_against_erp", new()),
            6 when rowId is not null => Multi(
                ("propose_set_cell", new() { ["rowId"] = rowId, ["columnKey"] = "verdict",   ["value"] = "NG" }),
                ("propose_set_cell", new() { ["rowId"] = rowId, ["columnKey"] = "risk",      ["value"] = "在庫不足 / 与信超過" }),
                ("propose_set_cell", new() { ["rowId"] = rowId, ["columnKey"] = "recommend", ["value"] = "150個に分割受注し、残数は3週目以降。経理に与信回収条件を確認。" })),
            _ => Text("""
                【モックモード】基幹データと突合した結果をまとめます。

                - テーブル構造と各列の意味を確認し、見積明細を読み取りました。
                - 弁P-101 の在庫・与信・生産枠を基幹システムに照会し、突合判定を実行しました。
                - 与信残・生産枠を超過する行を検出したため、判定/リスク/推奨アクションの記入を提案しました。

                提案内容を確認のうえ、承認してください（承認するまで Grid は変更されません）。
                """)
        };
    }

    private static ChatResponse Call(string name, Dictionary<string, object?> args)
        => new(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent(Guid.NewGuid().ToString("N"), name, args)]));

    private static ChatResponse Multi(params (string Name, Dictionary<string, object?> Args)[] calls)
    {
        var contents = calls
            .Select(c => (AIContent)new FunctionCallContent(Guid.NewGuid().ToString("N"), c.Name, c.Args))
            .ToList();
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, contents));
    }

    private static ChatResponse Text(string text)
        => new(new ChatMessage(ChatRole.Assistant, text));

    // read_rows / evaluate の結果テキストから製品コードを推定（例: 弁P-101 / P-202）
    private static string? ExtractProductCode(IList<ChatMessage> messages)
    {
        foreach (var text in ToolResultTexts(messages))
        {
            var m = Regex.Match(text, @"[弁ポンプフラ\w]+-\d+");
            if (m.Success) return m.Value;
        }
        return null;
    }

    // read_rows の結果テキストから先頭行の rowId（先頭8文字 hex）を抽出
    private static string? ExtractFirstRowId(IList<ChatMessage> messages)
    {
        foreach (var text in ToolResultTexts(messages))
        {
            var m = Regex.Match(text, @"\|\s*([0-9a-f]{8})\s*\|");
            if (m.Success) return m.Groups[1].Value;
        }
        return null;
    }

    private static IEnumerable<string> ToolResultTexts(IList<ChatMessage> messages)
        => messages
            .SelectMany(m => m.Contents)
            .OfType<FunctionResultContent>()
            .Select(r => r.Result?.ToString() ?? "");
}
