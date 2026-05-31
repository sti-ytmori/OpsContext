using Azure;
using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Configuration;
using OpsContext.Agents.Options;
using OpenAI.Embeddings;

// -----------------------------------------------------------------------
// OpsContext.Seed — design 03「擬似ナレッジ20件 seed 投入」
// -----------------------------------------------------------------------

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .Build();

var opts = config.GetSection("OpsContext").Get<OpsContextOptions>()
           ?? new OpsContextOptions();

// 接続情報チェック
if (string.IsNullOrWhiteSpace(opts.AiSearch.Endpoint) ||
    string.IsNullOrWhiteSpace(opts.AiSearch.ApiKey) ||
    string.IsNullOrWhiteSpace(opts.AzureOpenAi.Endpoint) ||
    string.IsNullOrWhiteSpace(opts.AzureOpenAi.ApiKey))
{
    Console.WriteLine("appsettings.json の OpsContext セクションに接続情報を設定してから再実行してください。");
    Console.WriteLine("  AiSearch.Endpoint / AiSearch.ApiKey");
    Console.WriteLine("  AzureOpenAi.Endpoint / AzureOpenAi.ApiKey");
    return 0;
}

Console.WriteLine("=== OpsContext.Seed 開始 ===");

// -----------------------------------------------------------------------
// クライアント初期化
// -----------------------------------------------------------------------
var searchEndpoint = new Uri(opts.AiSearch.Endpoint);
var searchCredential = new AzureKeyCredential(opts.AiSearch.ApiKey);
var indexClient = new SearchIndexClient(searchEndpoint, searchCredential);

var openAiClient = new AzureOpenAIClient(
    new Uri(opts.AzureOpenAi.Endpoint),
    new AzureKeyCredential(opts.AzureOpenAi.ApiKey));
var embeddingClient = openAiClient.GetEmbeddingClient(opts.AzureOpenAi.EmbeddingDeployment);

// -----------------------------------------------------------------------
// インデックス作成（存在しなければ）
// -----------------------------------------------------------------------
await EnsureKnowledgeIndexAsync(indexClient, opts.AiSearch.KnowledgeIndex);
await EnsureContextIndexAsync(indexClient, opts.AiSearch.ContextIndex);

// -----------------------------------------------------------------------
// 擬似ナレッジ20件の定義（design 03「seed 方針」の分類に準拠）
// -----------------------------------------------------------------------
var knowledgeDocs = GetKnowledgeDocuments();

Console.WriteLine($"ナレッジ {knowledgeDocs.Count} 件を embedding 化して投入します...");

var searchClient = new SearchClient(searchEndpoint, opts.AiSearch.KnowledgeIndex, searchCredential);
var uploadBatch = new List<SearchDocument>();

foreach (var doc in knowledgeDocs)
{
    Console.Write($"  [{doc["id"]}] embedding 生成中... ");
    var content = (string)doc["content"];
    var embeddingResult = await embeddingClient.GenerateEmbeddingAsync(content);
    var vector = embeddingResult.Value.ToFloats().ToArray();
    doc["contentVector"] = vector;
    uploadBatch.Add(doc);
    Console.WriteLine("OK");
}

var uploadResult = await searchClient.UploadDocumentsAsync(uploadBatch);
Console.WriteLine($"投入完了: {uploadResult.Value.Results.Count} 件");
Console.WriteLine("=== OpsContext.Seed 完了 ===");
return 0;

// -----------------------------------------------------------------------
// ローカル関数: インデックス定義
// -----------------------------------------------------------------------
static async Task EnsureKnowledgeIndexAsync(SearchIndexClient client, string indexName)
{
    try
    {
        await client.GetIndexAsync(indexName);
        Console.WriteLine($"インデックス '{indexName}' は既に存在します。スキップします。");
        return;
    }
    catch (RequestFailedException ex) when (ex.Status == 404)
    {
        // 存在しない → 作成する
    }

    Console.WriteLine($"インデックス '{indexName}' を作成します...");

    var vectorSearch = new VectorSearch();
    vectorSearch.Algorithms.Add(new HnswAlgorithmConfiguration("hnsw-config")
    {
        Parameters = new HnswParameters { M = 4, EfConstruction = 400, Metric = VectorSearchAlgorithmMetric.Cosine }
    });
    vectorSearch.Profiles.Add(new VectorSearchProfile("vector-profile", "hnsw-config"));

    var index = new SearchIndex(indexName)
    {
        Fields =
        {
            new SimpleField("id", SearchFieldDataType.String) { IsKey = true, IsFilterable = true },
            new SearchableField("content") { IsFilterable = false },
            new VectorSearchField("contentVector", 1536, "vector-profile"),
            new SimpleField("role", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
            new SimpleField("category", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
            new SimpleField("createdAt", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true }
        },
        VectorSearch = vectorSearch
    };

    await client.CreateIndexAsync(index);
    Console.WriteLine($"インデックス '{indexName}' を作成しました。");
}

static async Task EnsureContextIndexAsync(SearchIndexClient client, string indexName)
{
    try
    {
        await client.GetIndexAsync(indexName);
        Console.WriteLine($"インデックス '{indexName}' は既に存在します。スキップします。");
        return;
    }
    catch (RequestFailedException ex) when (ex.Status == 404)
    {
        // 存在しない → 作成する
    }

    Console.WriteLine($"インデックス '{indexName}' を作成します...");

    var vectorSearch = new VectorSearch();
    vectorSearch.Algorithms.Add(new HnswAlgorithmConfiguration("hnsw-config")
    {
        Parameters = new HnswParameters { M = 4, EfConstruction = 400, Metric = VectorSearchAlgorithmMetric.Cosine }
    });
    vectorSearch.Profiles.Add(new VectorSearchProfile("vector-profile", "hnsw-config"));

    var index = new SearchIndex(indexName)
    {
        Fields =
        {
            new SimpleField("id", SearchFieldDataType.String) { IsKey = true, IsFilterable = true },
            new SearchableField("content") { IsFilterable = false },
            new VectorSearchField("contentVector", 1536, "vector-profile"),
            new SimpleField("caseId", SearchFieldDataType.String) { IsFilterable = true },
            new SimpleField("role", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
            new SimpleField("kind", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
            new SimpleField("customerCode", SearchFieldDataType.String) { IsFilterable = true },
            new SimpleField("createdAt", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true }
        },
        VectorSearch = vectorSearch
    };

    await client.CreateIndexAsync(index);
    Console.WriteLine($"インデックス '{indexName}' を作成しました。");
}

// -----------------------------------------------------------------------
// ローカル関数: 擬似ナレッジ20件定義
// design 03 の seed 方針（与信規程5件・過去案件5件・生産ルール4件・購買調達3件 = 合計17件 + 汎用3件）
// -----------------------------------------------------------------------
static List<SearchDocument> GetKnowledgeDocuments()
{
    var now = DateTimeOffset.UtcNow;

    var docs = new List<(string id, string role, string category, string content)>
    {
        // 与信規程 (credit_policy, role=Accounting) 5件
        (
            "credit-001",
            "Accounting",
            "credit_policy",
            "与信枠使用率80%超の顧客に対する分割出荷ルール。顧客の与信枠使用率が80%を超えた場合、営業担当は経理部門に事前承認を申請し、承認を得た後に分割出荷計画を作成する必要がある。分割出荷は月次ベースで管理し、各回の出荷前に残与信枠を再確認する。使用率が95%を超えた場合は経営層の追加承認が必要となる。与信枠の見直しは四半期ごとに行い、過去12か月の入金実績を基に算定する。"
        ),
        (
            "credit-002",
            "Accounting",
            "credit_policy",
            "3か月以内に支払遅延歴がある顧客への与信加算条件。直近3か月以内に支払期日を7日以上超過した実績がある顧客については、通常与信枠に対して追加保証金（受注金額の10%相当）の提供を求めるか、前払い条件に切り替える。遅延が2回以上ある場合は、営業部長・経理部長の連名承認が必要。遅延事由が自然災害等の不可抗力である場合は、経理部長の判断で加算条件を免除できる。A商事については2026年2月に支払遅延（15日超過）の実績あり。"
        ),
        (
            "credit-003",
            "Accounting",
            "credit_policy",
            "新規顧客の初回与信枠設定基準。新規顧客（取引開始から12か月未満）の与信枠は、提出された決算書（直近2期分）の売上高・経常利益・純資産を基に算定する。初回与信枠の上限は1,000万円とし、超過する場合は代表取締役の保証または担保提供を必要とする。6か月の取引実績（支払遅延なし）が確認できた後、実績に応じて与信枠を増額できる。与信調査には帝国データバンクまたは東京商工リサーチの調査報告書を活用する。"
        ),
        (
            "credit-004",
            "Accounting",
            "credit_policy",
            "大口受注（受注金額1,000万円超）の経営承認フロー。単一案件で受注金額が1,000万円を超える場合、以下の承認フローが必要となる。営業担当 → 営業部長（与信確認）→ 経理部長（キャッシュフロー確認）→ 代表取締役（最終承認）。各承認は稟議システムに起票し、通常3営業日以内に完了する。緊急案件は営業部長が口頭承認後に事後起票できるが、経理部長・代表取締役の承認は必須とする。分割受注で合計額が1,000万円超となる場合も同フローが適用される。"
        ),
        (
            "credit-005",
            "Accounting",
            "credit_policy",
            "売上計上が月を跨ぐ場合の請求タイミング基準。製品の出荷が月末を跨ぐ場合、原則として実際の出荷完了日を売上計上日とする。ただし、月次締め処理の都合上、月末最終営業日の17時までに出荷完了が見込まれる場合は、当月計上として処理できる。部分出荷（分割出荷の初回）は、各出荷分を個別に計上する。請求書は出荷完了後3営業日以内に発行し、支払期日は請求日の翌月末とする。検収条件がある案件は、検収完了日を売上計上日とする。"
        ),

        // 過去類似案件 (past_order, role=Sales) 5件
        (
            "sales-001",
            "Sales",
            "past_order",
            "A商事 バルブ弁P-101 150個 分割受注 成立事例（3年前）。2023年4月、A商事から弁P-101を200個受注しようとしたが、与信枠使用率が82%に達していたため、150個を即時受注・残50個を翌月受注の分割方式で成立させた事例。交渉ポイントは「分割受注でも価格は一括受注と同単価を適用する」との特例約束で、A商事側も合意した。結果として両月で完納し、翌四半期には与信枠が増額されて大口受注が継続できるようになった。この事例はA商事担当営業の交渉テンプレートとして社内共有されている。"
        ),
        (
            "sales-002",
            "Sales",
            "past_order",
            "A商事 生産遅延による納期延長交渉成功事例。2024年8月、弁P-101の主要部材（ステンレス鋼管）の調達遅延により、A商事への納期を15日延長せざるを得なくなった事例。営業担当が遅延発覚後即日にA商事の購買担当へ連絡し、遅延理由・新納期・代替措置（中間製品の先行納品）を文書で提示した。A商事側の生産ラインへの影響が最小限になるよう部分納品スケジュールを提案したことで、ペナルティなしで解決。事前連絡の速さと代替案提示が交渉成功の鍵となった事例として記録されている。"
        ),
        (
            "sales-003",
            "Sales",
            "past_order",
            "大口受注を分割提案に切り替えて与信枠内に収めた事例。2025年1月、C製造から一括で2,000万円相当の受注打診があったが、与信枠（1,500万円）を超過していたため成立しなかった。そこで、受注を4四半期に分けた年間フレーム契約に切り替える提案を実施。C製造側にとって価格の安定と優先供給の保証が得られるメリットを強調した。結果として四半期ごとに500万円の受注を4回確定させることで与信枠内に収め、かつ年間売上として2,000万円を確保した成功事例。"
        ),
        (
            "sales-004",
            "Sales",
            "past_order",
            "競合切替時の特例値引き承認事例。2024年11月、既存顧客のD工業が競合他社の見積もりを持ち込み、自社製品の5%値引きを要求した案件。通常の値引き権限（営業担当2%、営業部長5%）を超える要求であったが、競合他社への切り替えによる年間売上損失（約800万円）を考慮し、営業部長・経理部長の連名で7%値引きを特例承認した。この事例では「競合切替リスクと値引き額のROI比較」を意思決定の根拠とした分析資料が評価された。特例値引きは3か月の条件付きとし、入金実績が良好であれば継続更新する取り決めとした。"
        ),
        (
            "sales-005",
            "Sales",
            "past_order",
            "納期2週間以内の緊急受注対応フロー。顧客の設備故障等による緊急受注（標準リードタイム30日以内の納期要求）に対応するフロー。まず在庫確認を最優先で実施し、在庫がある場合は即日出荷手配。在庫なしの場合は生産部門に緊急生産枠（月間生産キャパの10%確保）での対応可否を確認する。緊急生産の場合は段取り替え費用（標準の1.3倍）が発生するため、顧客への価格提示前に営業部長確認が必要。過去実績では最短10日での納品実績があり、部材手配が鍵となる。"
        ),

        // 生産ルール (production_rule, role=Production) 4件
        (
            "prod-001",
            "Production",
            "production_rule",
            "弁P-101 標準リードタイム30日・緊急枠での最短2週対応条件。弁P-101の標準生産リードタイムは受注確定から30日。内訳は部材手配10日・加工15日・検査5日。緊急対応枠を使用した場合の最短リードタイムは14日（部材手配5日・加工7日・検査2日）だが、以下の条件が必要となる。（1）部材の在庫が倉庫に現物があること、（2）生産ラインの空き枠が確保できること、（3）検査工程の外注を利用すること。緊急対応は月間生産キャパの最大10%まで受け入れ可能。費用は通常比1.3倍で計上する。"
        ),
        (
            "prod-002",
            "Production",
            "production_rule",
            "安全在庫50個を下回る場合の追加発注トリガー。弁P-101の安全在庫基準は50個（約3週間分の出荷量に相当）。在庫管理システムが安全在庫を下回ったことを検知した場合、自動的に購買部門へ発注依頼（100個単位）を送信する。発注後の補充リードタイムは通常40日（部材手配25日・生産10日・検査5日）。在庫が25個を下回った場合は緊急発注フラグを立て、購買部門が緊急手配ルートを使用する。在庫数の確認は毎営業日朝8時に自動実行される。"
        ),
        (
            "prod-003",
            "Production",
            "production_rule",
            "同製品複数受注が重なった場合の優先順位付けルール。同一製品（例: 弁P-101）に複数の受注が重複した場合の生産優先順位付けルール。基本は「納期順」で管理するが、以下の要素で加点して優先度を決定する。（1）既存顧客の緊急受注：+2点、（2）経営承認済み大口受注：+1点、（3）長期フレーム契約顧客：+1点、（4）新規顧客獲得案件：+1点。同点の場合は受注確定日順。この優先順位は生産計画会議（毎週月曜10時）で確定し、営業部門に通知される。変更が生じた場合は即日関係者に連絡する。"
        ),
        (
            "prod-004",
            "Production",
            "production_rule",
            "部材手配を含む生産能力の計算方法。弁P-101の月間最大生産能力は200個。部材手配の前提条件として、主要部材（ステンレス鋼管・ボールバルブ・シール材）の調達リードタイムを考慮する必要がある。受注から逆算した生産計画では、出荷予定日から生産リードタイム（30日）を引いた日を「部材発注期限」とする。月初時点の確定受注量と内示受注量の合計が生産能力の80%を超えた場合、新規受注の受入可否を判断するための「生産能力確認会議」を開催する。"
        ),

        // 購買調達 (procurement_rule, role=Purchasing) 3件
        (
            "proc-001",
            "Purchasing",
            "procurement_rule",
            "弁P-101 主要サプライヤーとの緊急手配条件。弁P-101の主要部材であるステンレス鋼管はサプライヤーA（国内）とサプライヤーB（アジア）の2社体制で調達。緊急手配の場合はサプライヤーAを優先使用（国内在庫有り・最短3日出荷）。緊急手配の条件は（1）発注量がサプライヤーAの月間在庫の30%以内、（2）通常価格の120%を受け入れること。年間取引実績により緊急対応優先権が付与されており、2026年現在は弊社が「優先顧客」ステータスを保持している。緊急発注は購買部長の口頭承認後に発注可能（事後起票）。"
        ),
        (
            "proc-002",
            "Purchasing",
            "procurement_rule",
            "代替調達可能部材のリストと切替判断基準。弁P-101の主要部材について、主サプライヤーからの調達が困難な場合の代替調達先と切替基準を定義する。ステンレス鋼管：サプライヤーC（国内）に切替可能（リードタイム+5日、コスト+8%）。ボールバルブ：サプライヤーD（国内）またはE（欧州）に切替可能（リードタイム+10〜15日、コスト+5〜12%）。切替判断の基準は「主サプライヤーの供給遅延が7日超が見込まれる場合」とする。代替調達を決定した場合、品質保証部門による受入検査プロセスを追加で実施する。"
        ),
        (
            "proc-003",
            "Purchasing",
            "procurement_rule",
            "部材リードタイムが生産スケジュールに与える影響評価。主要部材の調達遅延が生産スケジュールに与える影響を定量的に評価するための基準。ステンレス鋼管が5日遅延した場合、弁P-101の生産は5日後倒し（出荷リードタイム35日相当）。10日遅延の場合は緊急生産枠を使用しても出荷リードタイムは40日超となり、顧客への納期変更通知が必要。評価は購買部門が毎週木曜に「部材進捗レポート」を生産部門・営業部門に配信する。遅延リスクが発生した場合は翌営業日中に関係部署に口頭連絡する。"
        )
    };

    return docs.Select(d =>
    {
        var doc = new SearchDocument();
        doc["id"] = d.id;
        doc["role"] = d.role;
        doc["category"] = d.category;
        doc["content"] = d.content;
        doc["createdAt"] = DateTimeOffset.UtcNow;
        return doc;
    }).ToList();
}
