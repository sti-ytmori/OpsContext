# 02-sql-erp-tool 設計

Status: 進行中

## 上流参照

plan.md の「Tool層シグネチャ」「SQLスキーマ / 擬似ERP（基幹）」「サンプルデータ」「やらないこと」「検証方法」「タイムボックス Day1 4-7h」。
関連: [[01-infra]]（接続文字列）/ [[04-orchestrator-agents]]（Tool呼び出し元）/ [[08-excel-validation]]（Tool呼び出し元）

## 目的

擬似ERP（Azure SQL Database）に7テーブルのスキーマと seed データを投入し、エージェントがロール別判断に使う `ISqlErpTool` 5メソッドを実装する。
SELECT-onlyホワイトリストガードにより、LLMが生成したSQLが読み取り専用の範囲を超えないことを保証する。

## スコープ

やること:
- 擬似ERP 7テーブルの DDL を `sql/schema.sql` に定義
- 主役データ（A商事 A001・弁P-101）を含む seed データを `sql/seed.sql` に定義
- `ISqlErpTool` インターフェースと 5メソッドの実装クラス `SqlErpTool`
- `SqlQueryResult` 型（汎用 Columns/Rows 構造）の定義
- `RunReadOnlyQueryAsync` 用の SELECT-only ホワイトリストガード
- 接続文字列は [[01-infra]] が用意した `appsettings.Development.json` の `OpsContext.SqlConnectionString` を利用

やらないこと（plan.md「やらないこと」より該当を転記）:
- 編集系 SQL（LLMが生成するSQLはSELECTのみ。INSERT/UPDATE/DELETE/DDLは一切許可しない）
- Bicep / Terraform / 単体テスト / CI
- ContextStore 3テーブル（Cases / ContextEntries / FocusSnapshots）の実装（[[05-context-store]] で扱う）

## 設計詳細

### DDL 骨子（`sql/schema.sql`）

7テーブルの列定義。型はAzure SQL（T-SQL）前提。

```
Customers
  CustomerCode  NVARCHAR(20)  PK
  Name          NVARCHAR(100) NOT NULL
  Industry      NVARCHAR(50)
  CreditLimit   DECIMAL(18,0) -- 与信枠（円）
  CreditRating  NVARCHAR(10)  -- A/B/C
  PaymentTermDays INT
  SalesRep      NVARCHAR(50)

Products
  ProductCode   NVARCHAR(20)  PK
  Name          NVARCHAR(100) NOT NULL
  Category      NVARCHAR(50)
  UnitPrice     DECIMAL(18,0) -- 販売単価（円）
  UnitCost      DECIMAL(18,0) -- 仕入原価（円）
  LeadTimeDays  INT

Orders
  OrderId       INT           PK IDENTITY
  OrderNo       NVARCHAR(20)  UNIQUE NOT NULL
  CustomerCode  NVARCHAR(20)  FK -> Customers
  OrderDate     DATE
  RequestedDeliveryDate DATE
  Status        NVARCHAR(20)  -- Pending/Confirmed/Shipped/Closed
  TotalAmount   DECIMAL(18,0)
  SalesRep      NVARCHAR(50)

OrderLines
  OrderLineId   INT           PK IDENTITY
  OrderId       INT           FK -> Orders
  ProductCode   NVARCHAR(20)  FK -> Products
  Quantity      INT
  UnitPrice     DECIMAL(18,0)
  LineAmount    DECIMAL(18,0) -- = Quantity * UnitPrice

Inventory
  ProductCode   NVARCHAR(20)  PK FK -> Products
  OnHandQty     INT           -- 在庫数
  AllocatedQty  INT           -- 引当済数
  SafetyStock   INT           -- 安全在庫
  LastUpdated   DATETIME2

ProductionCapacity
  ProductCode   NVARCHAR(20)  PK（複合） FK -> Products
  CapacityDate  DATE          PK（複合）
  AvailableUnits INT          -- その日の生産可能数
  ReservedUnits  INT          -- 既予約数

CreditHistory
  Id            INT           PK IDENTITY
  CustomerCode  NVARCHAR(20)  FK -> Customers
  OccurredAt    DATETIME2
  EventType     NVARCHAR(30)  -- PaymentDelay/CreditExceeded/NormalPayment
  Note          NVARCHAR(500)
```

### seed データ（`sql/seed.sql`）の主役

ファイル配置: `sql/seed.sql`（リポジトリルート直下の `sql/` フォルダ）

主役データ（デモシナリオの「200個受注 = 与信オーバー + 生産枠不足」が成立する数値）:

- A商事 (A001): CreditLimit=50,000,000 / 与信使用中=38,000,000（Pending/Confirmed受注合計）/ 残=12,000,000
  - 3か月前の PaymentDelay が CreditHistory に1件（経理視点翻訳に使う）
- 弁P-101（弁当用容器 弁P-101）: UnitPrice=80,000 / UnitCost=57,600（粗利28%）/ LeadTimeDays=30
  - Inventory: OnHandQty=80 / AllocatedQty=0 / SafetyStock=50
  - ProductionCapacity: 今日から2週間（14日分）で AvailableUnits 合計が120個（1日8〜9個ずつ）

周辺データ（審査員が試行錯誤しても壊れない厚み）:
- Customers: 主役A001含む計10社
- Products: 主役弁P-101含む計20品番
- Orders: 主役A001の既存受注4件（合計38M相当）含む計60件
- ProductionCapacity: 90日分のレコード（弁P-101 + 他3品番）
- CreditHistory: A001の3件（3か月前遅延・2か月前正常・1か月前正常）

### `ISqlErpTool` インターフェース（plan.md 転記）

```csharp
public interface ISqlErpTool
{
    Task<SqlQueryResult> GetCustomerCreditAsync(string customerCode, CancellationToken ct);
    Task<SqlQueryResult> GetProductInventoryAsync(string productCode, CancellationToken ct);
    Task<SqlQueryResult> GetProductionCapacityAsync(string productCode, DateOnly from, DateOnly to, CancellationToken ct);
    Task<SqlQueryResult> SearchSimilarOrdersAsync(string customerCode, string productCode, int topN, CancellationToken ct);
    Task<SqlQueryResult> RunReadOnlyQueryAsync(string sql, CancellationToken ct);
}
```

### `SqlQueryResult` 型の定義方針

専用DTOではなく汎用の Columns/Rows 構造を採用する。理由: LLMへのシリアライズが統一でき、メソッドごとにDTOクラスを増やさずに済む。エージェントはこの構造をそのままプロンプト文字列に変換して判断に使う。

```csharp
public sealed record SqlQueryResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    int TotalRows
);
```

エージェント側は `SqlQueryResult.ToMarkdownTable()` 拡張メソッド（実装タスクに含む）でプロンプトに埋め込む。

### 各メソッドの内部クエリ骨子

`GetCustomerCreditAsync`: Customers + Ordersの未確定残合計をJOINして与信枠・使用額・残額を1行で返す。

`GetProductInventoryAsync`: Inventoryを ProductCode で引き、OnHandQty/AllocatedQty/SafetyStock/AvailableQty(=OnHandQty-AllocatedQty)を返す。

`GetProductionCapacityAsync`: ProductionCapacity を ProductCode + CapacityDate BETWEEN from AND to でフィルタし、日別レコードを返す。合計AvailableUnits・合計ReservedUnitsのサマリ行も末尾に付加。

`SearchSimilarOrdersAsync`: Orders + OrderLines + Customers を JOIN し、同一製品コードの過去受注をOrderDate降順で topN 件返す。

`RunReadOnlyQueryAsync`: SELECT-onlyホワイトリストガード（後述）を通過した場合のみ実行する。

### SELECT-only ホワイトリストガードの設計

`RunReadOnlyQueryAsync` が受け取ったSQL文字列に対して以下を順番にチェックし、1つでも違反したら `InvalidOperationException` を投げてクエリを実行しない。

1. 先頭トークン（空白・改行を除いた最初の単語）が `SELECT` であること（大文字小文字問わず）
2. SQL文字列に `INSERT`/`UPDATE`/`DELETE`/`DROP`/`ALTER`/`CREATE`/`EXEC`/`EXECUTE`/`TRUNCATE`/`MERGE`/`GRANT`/`REVOKE` のいずれかのキーワードが含まれないこと（単語境界でマッチ）
3. FROM / JOIN 句に含まれるテーブル名がホワイトリスト（下記）のみであること

テーブルホワイトリスト（ERP 7テーブルのみ。ContextStore側は別ツールの責務）:
```
Customers, Products, Orders, OrderLines, Inventory, ProductionCapacity, CreditHistory
```

ガード実装クラス: `SqlReadOnlyGuard`（static クラス。`Validate(string sql)` が成功/失敗を返す）。`SqlErpTool` は内部で `SqlReadOnlyGuard.Validate` を呼び出す。

### 接続文字列の取得

`SqlErpTool` は DI で `IConfiguration` を受け取り、`config["OpsContext:SqlConnectionString"]` を利用する。[[01-infra]] が `appsettings.Development.json` に書き出した値をそのまま使う。`Microsoft.Data.SqlClient` を使用。

## 依存

- [[01-infra]]: `appsettings.Development.json` の `OpsContext:SqlConnectionString` が存在すること

## 受け入れ条件

plan.md「検証方法」の以下ステップが主に該当する:

- ステップ4: 「基幹と突合」実行時、右ペインに `GetCustomerCredit / GetProductInventory / GetProductionCapacity` の SQL カードが並列で現れること
- ステップ5: 弁P-101 200個の行が NG (赤) で表示され、推奨に「150+80 分割」相当が含まれること（NG判定の根拠となる在庫80・生産枠合計120・与信残12Mがseedデータから正しく取得できていることが前提）
- ステップ6: 在庫/与信に余裕のある行が OK (緑) で表示されること（他製品・他顧客のseedデータが正常に返ること）
- ステップ10: `ContextEntries WHERE CaseId = ...` に取込 observation が入ること（これはContextStoreTool側だが、SqlErpToolの結果が判定ロジックに渡っていることが前提）

## タスク

- [x] `sql/` フォルダを作成し、`sql/schema.sql`（7テーブルDDL）を書く
- [x] `sql/seed.sql` を書く（A001 与信枠50M/使用38M、弁P-101 単価8万/在庫80/安全在庫50、生産能力90日分、計10顧客/20製品/60受注）
- [x] `Microsoft.Data.SqlClient` を `OpsContext.Agents` プロジェクトに追加済み（csproj 確認済み）。スキーマ適用は Azure 接続後（human-task）
- [x] `SqlQueryResult` レコードと `ToMarkdownTable()` 拡張メソッドを実装する
- [x] `SqlReadOnlyGuard` クラス（SELECT-only + テーブルホワイトリスト検証）を実装する
- [x] `ISqlErpTool` インターフェースを定義し、`SqlErpTool` 実装クラスを作成する（5メソッド）
- [x] DI 登録: `builder.Services.AddScoped<ISqlErpTool, SqlErpTool>()` を `Program.cs` に追加済み（非モック時分岐）
- [ ] `GetCustomerCreditAsync` / `GetProductInventoryAsync` / `GetProductionCapacityAsync` を手動呼び出しして返却値を確認する（A001・弁P-101 のデータが期待値と一致すること）
