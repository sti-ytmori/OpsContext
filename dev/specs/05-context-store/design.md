# 05-context-store 設計

Status: 完了

## 上流参照

plan.md の「SQLスキーマ — コンテキストストア」「Tool層シグネチャ (IContextStoreTool)」「コンテキスト永続化方針」「タイムボックス Day2 4-7h」「やらないこと」「検証方法 ステップ10・11・12」。
関連: [[01-infra]] [[03-ai-search-tool]] [[04-orchestrator-agents]] [[06-curator-agent]] [[08-excel-validation]]

## 目的

案件 (Case) 単位で、エージェントが下した判断 (decision) と観察 (observation) をロールごとに永続化し、別ロールや後続ターンでその文脈を再構成できるようにする。SQL を正規の記録先、AI Search を検索用のミラーとして使い分け、CuratorAgent の Append* 呼び出しと Orchestrator の ReadCaseContext 呼び出しの両方に応える共有ストアを提供する。

## スコープ

やること:
- Cases / ContextEntries / FocusSnapshots の DDL 定義と Migration スクリプト
- IContextStoreTool の実装 (3メソッド)
- ContextEntry モデルクラス定義
- AppendDecision / AppendObservation 時に AI Search インデックス `opscontext-context` へ同期 upsert (embedding 生成含む)
- CaseId の生成ロジック (Web 側で NewGuid() → セッション保持)
- roleViewpoint パラメータによる絞り込みロジック
- FocusSnapshots への書き込みは [[06-curator-agent]] が担うが、テーブル定義はここで完結させる

やらないこと (plan.md「やらないこと」より):
- マルチターン会話の永続化 (CaseId 単位での retrieval はするが、会話履歴そのものの永続化はしない)
- 見積明細の DB 永続化 (取込 observation のみ ContextEntries に記録、明細行は in-memory 保持)
- Cosmos DB の利用
- 単体テスト / CI

## 設計詳細

### DDL

```sql
-- Cases: 案件の識別と状態管理
CREATE TABLE Cases (
    CaseId      UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    Title       NVARCHAR(200)    NOT NULL,
    CustomerCode NVARCHAR(20)   NOT NULL,
    Status      NVARCHAR(20)    NOT NULL DEFAULT 'open',   -- open / closed
    CreatedAt   DATETIME2(0)    NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt   DATETIME2(0)    NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_Cases PRIMARY KEY (CaseId)
);

-- ContextEntries: エージェントが記録する決定・観察・引継ぎなど
CREATE TABLE ContextEntries (
    EntryId     UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    CaseId      UNIQUEIDENTIFIER NOT NULL,
    Role        NVARCHAR(30)     NOT NULL,   -- Sales / Purchasing / Production / Accounting / Curator
    Author      NVARCHAR(100)    NOT NULL,   -- ユーザー名またはエージェント名
    Kind        NVARCHAR(20)     NOT NULL,   -- decision / observation / question / answer / handoff
    Text        NVARCHAR(MAX)    NOT NULL,
    RefSql      NVARCHAR(MAX)    NULL,       -- 根拠となった SQL クエリ (任意)
    CreatedAt   DATETIME2(0)     NOT NULL DEFAULT SYSUTCDATETIME(),
    EmbeddingId NVARCHAR(200)    NULL,       -- AI Search ドキュメント ID (upsert 後に記録)
    CONSTRAINT PK_ContextEntries PRIMARY KEY (EntryId),
    CONSTRAINT FK_ContextEntries_Cases FOREIGN KEY (CaseId) REFERENCES Cases (CaseId)
);
CREATE INDEX IX_ContextEntries_CaseId ON ContextEntries (CaseId);
CREATE INDEX IX_ContextEntries_Kind   ON ContextEntries (Kind);

-- FocusSnapshots: Curia の current_focus.md 相当。ロール別の現在フォーカスまとめ
CREATE TABLE FocusSnapshots (
    SnapshotId  UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    CaseId      UNIQUEIDENTIFIER NOT NULL,
    Role        NVARCHAR(30)     NOT NULL,
    SummaryMd   NVARCHAR(MAX)    NOT NULL,   -- Markdown 形式のフォーカス要約
    CreatedAt   DATETIME2(0)     NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_FocusSnapshots PRIMARY KEY (SnapshotId),
    CONSTRAINT FK_FocusSnapshots_Cases FOREIGN KEY (CaseId) REFERENCES Cases (CaseId)
);
CREATE INDEX IX_FocusSnapshots_CaseRole ON FocusSnapshots (CaseId, Role);
```

### ContextEntry モデル

```csharp
// OpsContext.Agents/Models/ContextEntry.cs
public sealed record ContextEntry(
    string EntryId,
    string CaseId,
    string Role,
    string Author,
    string Kind,         // decision / observation / question / answer / handoff
    string Text,
    string? RefSql,
    DateTimeOffset CreatedAt
);
```

### IContextStoreTool シグネチャ (plan.md 転記)

```csharp
public interface IContextStoreTool
{
    // 案件の全エントリを返す。roleViewpoint 指定時は
    // Role = roleViewpoint OR Kind = 'handoff' の行に絞り込む。
    Task<IReadOnlyList<ContextEntry>> ReadCaseContextAsync(
        string caseId,
        string? roleViewpoint,
        CancellationToken ct);

    // Kind = 'decision' でエントリを追加し、EntryId を返す。
    Task<string> AppendDecisionAsync(
        string caseId,
        string role,
        string author,
        string text,
        CancellationToken ct);

    // Kind = 'observation' でエントリを追加し、EntryId を返す。
    Task<string> AppendObservationAsync(
        string caseId,
        string role,
        string author,
        string text,
        CancellationToken ct);
}
```

### roleViewpoint の絞り込みロジック

ReadCaseContextAsync で roleViewpoint が指定された場合は以下の条件で SQL を発行する:

```sql
SELECT * FROM ContextEntries
WHERE CaseId = @caseId
  AND (Role = @roleViewpoint OR Kind = 'handoff')
ORDER BY CreatedAt;
```

roleViewpoint が null の場合は全件取得。

### AI Search ミラー方針

- AppendDecisionAsync / AppendObservationAsync の SQL INSERT 成功後、同期で `opscontext-context` インデックスに upsert する。
- embedding の生成は Azure OpenAI `text-embedding-3-small` (デプロイ名: `embedding-small`) を呼び出し、`contentVector` フィールドに格納する。
- upsert 後に返された AI Search ドキュメント ID を ContextEntries.EmbeddingId に UPDATE する (失敗しても SQL 本体はロールバックしない — ミラーはベストエフォート)。
- upsert に使う [[03-ai-search-tool]] の内部メソッド (UpsertContextEntryAsync) を直接呼び出す。

AI Search ドキュメントスキーマ (opscontext-context インデックス):

```json
{
  "id": "<EntryId>",
  "caseId": "<CaseId>",
  "role": "<Role>",
  "author": "<Author>",
  "kind": "<Kind>",
  "content": "<Text>",
  "createdAt": "<ISO8601>",
  "contentVector": [/* float[] */]
}
```

### CaseId の生成タイミング

最初のチャットメッセージ送信時に Blazor Server 側で `Guid.NewGuid()` を生成し、Blazor のコンポーネントステートまたは `CascadingValue` としてセッション中保持する。ページリロードで別 CaseId になることを許容 (MVP 制約)。

### ファイル配置

```
OpsContext.Agents/
  Models/
    ContextEntry.cs
  Tools/
    ContextStoreTool.cs     -- IContextStoreTool 実装
    IContextStoreTool.cs    -- インターフェース定義
sql/
  schema_context_store.sql  -- Cases / ContextEntries / FocusSnapshots DDL
```

## 依存

- [[01-infra]]: Azure SQL Database 接続文字列、AI Search エンドポイント・APIキーが appsettings.Development.json に揃っていること
- [[03-ai-search-tool]]: UpsertContextEntryAsync の内部実装を提供すること (Append* 時の同期ミラーに利用)

後続:
- [[06-curator-agent]]: AppendObservationAsync を呼び出してCuratorの抽出結果を記録する
- [[04-orchestrator-agents]]: ReadCaseContextAsync を呼び出して経理視点翻訳の入力を取得する
- [[08-excel-validation]]: Excel 明細取込の事実を AppendObservationAsync で記録する

## 受け入れ条件

plan.md「検証方法」より:

- ステップ10: 「この線で進めます」送信後に `SELECT * FROM ContextEntries WHERE CaseId = '<caseId>'` を実行し、Kind = 'decision' の行と Kind = 'observation' の行 (「Excel明細 N 行取込・うち M 行リスク」相当) が存在すること
- ステップ11: ドロップダウンを Accounting に切替後、案件一覧から同 CaseId の案件が選択できること (Cases テーブルにレコードが存在すること)
- ステップ12: 経理視点要約に「A商事 3か月前与信遅延 / 与信枠80%消費 / Excel明細 N 行リスク」が含まれること (ReadCaseContextAsync が roleViewpoint=Accounting で handoff と decision を返せること)

## タスク

- [x] `sql/schema_context_store.sql` に Cases / ContextEntries / FocusSnapshots の DDL を記述し、Azure SQL Database に適用する
- [x] `OpsContext.Agents/Models/ContextEntry.cs` に ContextEntry レコード型を定義する
- [x] `OpsContext.Agents/Tools/IContextStoreTool.cs` にインターフェースを定義する
- [x] `OpsContext.Agents/Tools/ContextStoreTool.cs` に ReadCaseContextAsync を実装する (roleViewpoint 絞り込みロジック含む)
- [x] AppendDecisionAsync / AppendObservationAsync を実装する (SQL INSERT 後に AI Search upsert を呼び出す)
- [x] AI Search upsert 失敗時のベストエフォート処理 (例外を catch してログ記録、SQL はコミット済みのまま継続) を実装する
- [x] Blazor Server 側で最初のチャット送信時に CaseId を NewGuid() 生成しセッション保持する配線を追加する
- [x] DI 登録 (AddScoped<IContextStoreTool, ContextStoreTool>) を OpsContext.Web の Program.cs に追加する
- [x] ListCasesAsync を IContextStoreTool / ContextStoreTool / MockContextStoreTool に追加する
- [ ] `SELECT * FROM ContextEntries WHERE CaseId = '<id>'` でステップ10の受け入れ条件を手動確認する（Azure接続後）
