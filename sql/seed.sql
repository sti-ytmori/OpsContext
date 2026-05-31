-- =============================================================================
-- OpsContext ERPスナップショット シードデータ (GridTables / GridRows 形式)
-- API/ODBC連携後の状態を再現したデモ用データ。
-- ERP基幹データは GridTables/GridRows の論理テーブルとして格納する。
-- =============================================================================

-- 既存 erp_ エントリをクリア（GridRows は CASCADE DELETE で連動）
DELETE FROM GridTables WHERE TableId LIKE 'erp_%';

-- -----------------------------------------------------------------------
-- GridTables: ERP論理テーブル定義 (7テーブル)
-- -----------------------------------------------------------------------
INSERT INTO GridTables (TableId, Name, ColumnsJson) VALUES
('erp_customers', N'顧客マスタ',
 N'[{"key":"CustomerCode","label":"顧客コード","type":"text"},{"key":"Name","label":"顧客名","type":"text"},{"key":"Industry","label":"業種","type":"text"},{"key":"CreditLimit","label":"与信枠（円）","type":"number"},{"key":"CreditRating","label":"格付け","type":"text"},{"key":"PaymentTermDays","label":"支払サイト（日）","type":"number"},{"key":"SalesRep","label":"担当営業","type":"text"}]'),
('erp_products', N'製品マスタ',
 N'[{"key":"ProductCode","label":"品番","type":"text"},{"key":"Name","label":"品名","type":"text"},{"key":"Category","label":"カテゴリ","type":"text"},{"key":"UnitPrice","label":"単価（円）","type":"number"},{"key":"UnitCost","label":"原価（円）","type":"number"},{"key":"LeadTimeDays","label":"リードタイム（日）","type":"number"}]'),
('erp_orders', N'受注ヘッダ',
 N'[{"key":"OrderNo","label":"受注No","type":"text"},{"key":"CustomerCode","label":"顧客コード","type":"text"},{"key":"OrderDate","label":"受注日","type":"date"},{"key":"RequestedDeliveryDate","label":"希望納期","type":"date"},{"key":"Status","label":"ステータス","type":"text"},{"key":"TotalAmount","label":"受注金額（円）","type":"number"},{"key":"SalesRep","label":"担当営業","type":"text"}]'),
('erp_orderlines', N'受注明細',
 N'[{"key":"OrderNo","label":"受注No","type":"text"},{"key":"ProductCode","label":"品番","type":"text"},{"key":"Quantity","label":"数量","type":"number"},{"key":"UnitPrice","label":"単価（円）","type":"number"},{"key":"LineAmount","label":"明細金額（円）","type":"number"}]'),
('erp_inventory', N'製品在庫',
 N'[{"key":"ProductCode","label":"品番","type":"text"},{"key":"OnHandQty","label":"手持数","type":"number"},{"key":"AllocatedQty","label":"引当数","type":"number"},{"key":"SafetyStock","label":"安全在庫","type":"number"},{"key":"LastUpdated","label":"最終更新","type":"date"}]'),
('erp_production_capacity', N'生産能力',
 N'[{"key":"ProductCode","label":"品番","type":"text"},{"key":"CapacityDate","label":"日付","type":"date"},{"key":"AvailableUnits","label":"生産可能数","type":"number"},{"key":"ReservedUnits","label":"予約済数","type":"number"}]'),
('erp_credithistory', N'与信履歴',
 N'[{"key":"CustomerCode","label":"顧客コード","type":"text"},{"key":"OccurredAt","label":"発生日時","type":"text"},{"key":"EventType","label":"イベント種別","type":"text"},{"key":"Note","label":"備考","type":"text"}]');

-- -----------------------------------------------------------------------
-- erp_customers (10社)
-- 主役: A001 (A商事) CreditLimit=50,000,000 / CreditRating=B
-- -----------------------------------------------------------------------
INSERT INTO GridRows (RowId, TableId, OrderIndex, CellsJson) VALUES
('erp_cust_A001','erp_customers',1,N'{"CustomerCode":"A001","Name":"A商事株式会社","Industry":"商社","CreditLimit":"50000000","CreditRating":"B","PaymentTermDays":"30","SalesRep":"田中 一郎"}'),
('erp_cust_A002','erp_customers',2,N'{"CustomerCode":"A002","Name":"B製造株式会社","Industry":"製造業","CreditLimit":"80000000","CreditRating":"A","PaymentTermDays":"45","SalesRep":"鈴木 花子"}'),
('erp_cust_A003','erp_customers',3,N'{"CustomerCode":"A003","Name":"Cフード株式会社","Industry":"食品","CreditLimit":"20000000","CreditRating":"B","PaymentTermDays":"30","SalesRep":"佐藤 次郎"}'),
('erp_cust_A004','erp_customers',4,N'{"CustomerCode":"A004","Name":"Dサービス株式会社","Industry":"サービス業","CreditLimit":"15000000","CreditRating":"C","PaymentTermDays":"60","SalesRep":"田中 一郎"}'),
('erp_cust_A005','erp_customers',5,N'{"CustomerCode":"A005","Name":"Eテクノロジー株式会社","Industry":"IT","CreditLimit":"60000000","CreditRating":"A","PaymentTermDays":"30","SalesRep":"山田 三郎"}'),
('erp_cust_A006','erp_customers',6,N'{"CustomerCode":"A006","Name":"Fロジスティクス株式会社","Industry":"物流","CreditLimit":"35000000","CreditRating":"B","PaymentTermDays":"45","SalesRep":"鈴木 花子"}'),
('erp_cust_A007','erp_customers',7,N'{"CustomerCode":"A007","Name":"G建設株式会社","Industry":"建設","CreditLimit":"25000000","CreditRating":"B","PaymentTermDays":"60","SalesRep":"佐藤 次郎"}'),
('erp_cust_A008','erp_customers',8,N'{"CustomerCode":"A008","Name":"H医療株式会社","Industry":"医療","CreditLimit":"40000000","CreditRating":"A","PaymentTermDays":"30","SalesRep":"山田 三郎"}'),
('erp_cust_A009','erp_customers',9,N'{"CustomerCode":"A009","Name":"I小売株式会社","Industry":"小売","CreditLimit":"10000000","CreditRating":"C","PaymentTermDays":"30","SalesRep":"田中 一郎"}'),
('erp_cust_A010','erp_customers',10,N'{"CustomerCode":"A010","Name":"J農業株式会社","Industry":"農業","CreditLimit":"18000000","CreditRating":"B","PaymentTermDays":"45","SalesRep":"鈴木 花子"}');

-- -----------------------------------------------------------------------
-- erp_products (20品番)
-- 主役: 弁P-101 UnitPrice=80,000 / LeadTimeDays=30
-- -----------------------------------------------------------------------
INSERT INTO GridRows (RowId, TableId, OrderIndex, CellsJson) VALUES
('erp_prod_弁P-101','erp_products',1, N'{"ProductCode":"弁P-101","Name":"弁当用容器弁P-101","Category":"容器","UnitPrice":"80000","UnitCost":"57600","LeadTimeDays":"30"}'),
('erp_prod_P-102','erp_products',2, N'{"ProductCode":"P-102","Name":"断熱容器P-102","Category":"容器","UnitPrice":"95000","UnitCost":"68400","LeadTimeDays":"35"}'),
('erp_prod_P-103','erp_products',3, N'{"ProductCode":"P-103","Name":"仕切り付容器P-103","Category":"容器","UnitPrice":"72000","UnitCost":"51840","LeadTimeDays":"25"}'),
('erp_prod_P-104','erp_products',4, N'{"ProductCode":"P-104","Name":"保冷容器P-104","Category":"容器","UnitPrice":"120000","UnitCost":"86400","LeadTimeDays":"40"}'),
('erp_prod_P-201','erp_products',5, N'{"ProductCode":"P-201","Name":"包装フィルムS-201","Category":"包材","UnitPrice":"15000","UnitCost":"10800","LeadTimeDays":"14"}'),
('erp_prod_P-202','erp_products',6, N'{"ProductCode":"P-202","Name":"包装フィルムM-202","Category":"包材","UnitPrice":"18000","UnitCost":"12960","LeadTimeDays":"14"}'),
('erp_prod_P-203','erp_products',7, N'{"ProductCode":"P-203","Name":"包装フィルムL-203","Category":"包材","UnitPrice":"22000","UnitCost":"15840","LeadTimeDays":"14"}'),
('erp_prod_P-204','erp_products',8, N'{"ProductCode":"P-204","Name":"シュリンクフィルム-204","Category":"包材","UnitPrice":"12000","UnitCost":"8640","LeadTimeDays":"10"}'),
('erp_prod_P-301','erp_products',9, N'{"ProductCode":"P-301","Name":"緩衝材A-301","Category":"梱包材","UnitPrice":"8000","UnitCost":"5760","LeadTimeDays":"7"}'),
('erp_prod_P-302','erp_products',10,N'{"ProductCode":"P-302","Name":"緩衝材B-302","Category":"梱包材","UnitPrice":"10000","UnitCost":"7200","LeadTimeDays":"7"}'),
('erp_prod_P-303','erp_products',11,N'{"ProductCode":"P-303","Name":"段ボールS-303","Category":"梱包材","UnitPrice":"5000","UnitCost":"3600","LeadTimeDays":"5"}'),
('erp_prod_P-304','erp_products',12,N'{"ProductCode":"P-304","Name":"段ボールM-304","Category":"梱包材","UnitPrice":"6500","UnitCost":"4680","LeadTimeDays":"5"}'),
('erp_prod_P-401','erp_products',13,N'{"ProductCode":"P-401","Name":"ラベルシールA-401","Category":"印刷物","UnitPrice":"3000","UnitCost":"2160","LeadTimeDays":"3"}'),
('erp_prod_P-402','erp_products',14,N'{"ProductCode":"P-402","Name":"パンフレットB-402","Category":"印刷物","UnitPrice":"25000","UnitCost":"18000","LeadTimeDays":"10"}'),
('erp_prod_P-501','erp_products',15,N'{"ProductCode":"P-501","Name":"スチール棚S-501","Category":"設備","UnitPrice":"150000","UnitCost":"108000","LeadTimeDays":"60"}'),
('erp_prod_P-502','erp_products',16,N'{"ProductCode":"P-502","Name":"スチール棚M-502","Category":"設備","UnitPrice":"200000","UnitCost":"144000","LeadTimeDays":"60"}'),
('erp_prod_P-601','erp_products',17,N'{"ProductCode":"P-601","Name":"配送用パレット-601","Category":"物流","UnitPrice":"35000","UnitCost":"25200","LeadTimeDays":"20"}'),
('erp_prod_P-602','erp_products',18,N'{"ProductCode":"P-602","Name":"ラック用トレー-602","Category":"物流","UnitPrice":"28000","UnitCost":"20160","LeadTimeDays":"15"}'),
('erp_prod_P-701','erp_products',19,N'{"ProductCode":"P-701","Name":"洗浄剤クリーンA-701","Category":"消耗品","UnitPrice":"4500","UnitCost":"3240","LeadTimeDays":"5"}'),
('erp_prod_P-702','erp_products',20,N'{"ProductCode":"P-702","Name":"潤滑油ルブB-702","Category":"消耗品","UnitPrice":"6800","UnitCost":"4896","LeadTimeDays":"7"}');

-- -----------------------------------------------------------------------
-- erp_orders (60件) - 一時テーブル経由で動的日付JSON組み立て
-- DaysOrd: 受注日(今日からの差分日数) / DaysDlv: 希望納期
-- -----------------------------------------------------------------------
DROP TABLE IF EXISTS #Ord;
CREATE TABLE #Ord (
    Seq          INT IDENTITY(1,1),
    OrderNo      NVARCHAR(20),
    CustomerCode NVARCHAR(10),
    DaysOrd      INT,
    DaysDlv      INT,
    Status       NVARCHAR(20),
    TotalAmount  BIGINT,
    SalesRep     NVARCHAR(20)
);
INSERT INTO #Ord (OrderNo,CustomerCode,DaysOrd,DaysDlv,Status,TotalAmount,SalesRep) VALUES
('ORD-0001','A001', -60,-30,'Confirmed',10000000,N'田中 一郎'),
('ORD-0002','A001', -45,-15,'Confirmed',12000000,N'田中 一郎'),
('ORD-0003','A001', -20, 10,'Pending',   8000000,N'田中 一郎'),
('ORD-0004','A001',  -7, 23,'Pending',   8000000,N'田中 一郎'),
('ORD-0005','A002', -50,-20,'Shipped',   5000000,N'鈴木 花子'),
('ORD-0006','A002', -30,  0,'Confirmed', 8000000,N'鈴木 花子'),
('ORD-0007','A002', -10, 20,'Pending',   3000000,N'鈴木 花子'),
('ORD-0008','A002',  -5, 25,'Pending',   6000000,N'鈴木 花子'),
('ORD-0009','A002', -90,-60,'Closed',   12000000,N'鈴木 花子'),
('ORD-0010','A003', -40,-10,'Shipped',   1500000,N'佐藤 次郎'),
('ORD-0011','A003', -15, 15,'Confirmed', 2000000,N'佐藤 次郎'),
('ORD-0012','A003',  -3, 27,'Pending',   1800000,N'佐藤 次郎'),
('ORD-0013','A004', -55,-25,'Closed',    800000, N'田中 一郎'),
('ORD-0014','A004', -20, 10,'Confirmed', 900000, N'田中 一郎'),
('ORD-0015','A004',  -8, 22,'Pending',   600000, N'田中 一郎'),
('ORD-0016','A005', -70,-40,'Closed',   18000000,N'山田 三郎'),
('ORD-0017','A005', -35, -5,'Shipped',   7000000,N'山田 三郎'),
('ORD-0018','A005', -12, 18,'Confirmed', 9000000,N'山田 三郎'),
('ORD-0019','A005',  -4, 26,'Pending',   5000000,N'山田 三郎'),
('ORD-0020','A006', -80,-50,'Closed',    4000000,N'鈴木 花子'),
('ORD-0021','A006', -25,  5,'Confirmed', 6000000,N'鈴木 花子'),
('ORD-0022','A006',  -6, 24,'Pending',   3500000,N'鈴木 花子'),
('ORD-0023','A006',  -2, 28,'Pending',   2000000,N'鈴木 花子'),
('ORD-0024','A007', -60,-30,'Closed',    3000000,N'佐藤 次郎'),
('ORD-0025','A007', -18, 12,'Confirmed', 4500000,N'佐藤 次郎'),
('ORD-0026','A007',  -9, 21,'Pending',   2800000,N'佐藤 次郎'),
('ORD-0027','A008', -50,-20,'Shipped',   9000000,N'山田 三郎'),
('ORD-0028','A008', -22,  8,'Confirmed', 7000000,N'山田 三郎'),
('ORD-0029','A008', -11, 19,'Pending',   5500000,N'山田 三郎'),
('ORD-0030','A008',  -1, 29,'Pending',   4000000,N'山田 三郎'),
('ORD-0031','A009', -45,-15,'Closed',    500000, N'田中 一郎'),
('ORD-0032','A009', -16, 14,'Confirmed', 700000, N'田中 一郎'),
('ORD-0033','A009',  -5, 25,'Pending',   400000, N'田中 一郎'),
('ORD-0034','A010', -75,-45,'Closed',    2000000,N'鈴木 花子'),
('ORD-0035','A010', -28,  2,'Shipped',   1800000,N'鈴木 花子'),
('ORD-0036','A010', -13, 17,'Confirmed', 2200000,N'鈴木 花子'),
('ORD-0037','A010',  -4, 26,'Pending',   1500000,N'鈴木 花子'),
('ORD-0038','A001',-120,-90,'Closed',    5000000,N'田中 一郎'),
('ORD-0039','A001',-100,-70,'Closed',    7000000,N'田中 一郎'),
('ORD-0040','A002',-110,-80,'Closed',    9000000,N'鈴木 花子'),
('ORD-0041','A003', -85,-55,'Closed',    3200000,N'佐藤 次郎'),
('ORD-0042','A004', -95,-65,'Closed',    1200000,N'田中 一郎'),
('ORD-0043','A005',-115,-85,'Closed',   22000000,N'山田 三郎'),
('ORD-0044','A006', -92,-62,'Closed',    5500000,N'鈴木 花子'),
('ORD-0045','A007',-105,-75,'Closed',    3800000,N'佐藤 次郎'),
('ORD-0046','A008', -88,-58,'Closed',   11000000,N'山田 三郎'),
('ORD-0047','A009', -98,-68,'Closed',    650000, N'田中 一郎'),
('ORD-0048','A010',-108,-78,'Closed',    2500000,N'鈴木 花子'),
('ORD-0049','A002', -65,-35,'Closed',    4000000,N'鈴木 花子'),
('ORD-0050','A005', -55,-25,'Closed',   15000000,N'山田 三郎'),
('ORD-0051','A003', -62,-32,'Closed',    2800000,N'佐藤 次郎'),
('ORD-0052','A006', -48,-18,'Closed',    4200000,N'鈴木 花子'),
('ORD-0053','A007', -72,-42,'Closed',    3100000,N'佐藤 次郎'),
('ORD-0054','A008', -68,-38,'Closed',    8500000,N'山田 三郎'),
('ORD-0055','A010', -52,-22,'Closed',    1900000,N'鈴木 花子'),
('ORD-0056','A001',-130,-100,'Closed',   4500000,N'田中 一郎'),
('ORD-0057','A004', -78,-48,'Closed',    750000, N'田中 一郎'),
('ORD-0058','A009', -66,-36,'Closed',    480000, N'田中 一郎'),
('ORD-0059','A002', -42,-12,'Closed',    6500000,N'鈴木 花子'),
('ORD-0060','A005', -38, -8,'Shipped',  11000000,N'山田 三郎');

INSERT INTO GridRows (RowId, TableId, OrderIndex, CellsJson)
SELECT
    CONCAT(N'erp_order_', o.OrderNo),
    N'erp_orders',
    o.Seq,
    CONCAT(N'{"OrderNo":"',           o.OrderNo,
           N'","CustomerCode":"',     o.CustomerCode,
           N'","OrderDate":"',        CONVERT(NVARCHAR(10), DATEADD(day, o.DaysOrd, CAST(GETDATE() AS DATE)), 23),
           N'","RequestedDeliveryDate":"', CONVERT(NVARCHAR(10), DATEADD(day, o.DaysDlv, CAST(GETDATE() AS DATE)), 23),
           N'","Status":"',           o.Status,
           N'","TotalAmount":"',      CAST(o.TotalAmount AS NVARCHAR(20)),
           N'","SalesRep":"',         o.SalesRep, N'"}')
FROM #Ord o;

-- -----------------------------------------------------------------------
-- erp_orderlines
-- -----------------------------------------------------------------------
DROP TABLE IF EXISTS #OL;
CREATE TABLE #OL (OrderNo NVARCHAR(20), ProductCode NVARCHAR(20), Qty INT, UnitPrice BIGINT, LineAmt BIGINT);
INSERT INTO #OL VALUES
-- ORD-0001
('ORD-0001','弁P-101',100,80000,8000000),('ORD-0001','P-201',133,15000,1995000),
-- ORD-0002
('ORD-0002','弁P-101',120,80000,9600000),('ORD-0002','P-202',80,18000,1440000),
-- ORD-0003
('ORD-0003','弁P-101',90,80000,7200000),('ORD-0003','P-301',100,8000,800000),
-- ORD-0004
('ORD-0004','弁P-101',80,80000,6400000),('ORD-0004','P-102',16,95000,1520000),
-- ORD-0005
('ORD-0005','P-102',40,95000,3800000),('ORD-0005','P-203',55,22000,1210000),
-- ORD-0006
('ORD-0006','P-501',40,150000,6000000),('ORD-0006','P-301',250,8000,2000000),
-- ORD-0007
('ORD-0007','P-201',200,15000,3000000),
-- ORD-0008
('ORD-0008','P-104',30,120000,3600000),('ORD-0008','P-302',240,10000,2400000),
-- ORD-0009
('ORD-0009','P-502',60,200000,12000000),
-- ORD-0010
('ORD-0010','P-103',20,72000,1440000),('ORD-0010','P-401',20,3000,60000),
-- ORD-0011
('ORD-0011','P-103',25,72000,1800000),('ORD-0011','P-204',10,12000,120000),
('ORD-0011','P-401',8,3000,24000),    ('ORD-0011','P-303',11,5000,55000),
-- ORD-0012
('ORD-0012','弁P-101',20,80000,1600000),('ORD-0012','P-204',10,12000,120000),('ORD-0012','P-701',11,4500,49500),
-- ORD-0013
('ORD-0013','P-401',266,3000,798000),
-- ORD-0014
('ORD-0014','P-402',36,25000,900000),
-- ORD-0015
('ORD-0015','P-701',133,4500,598500),
-- ORD-0016
('ORD-0016','P-501',80,150000,12000000),('ORD-0016','P-601',171,35000,5985000),
-- ORD-0017
('ORD-0017','P-502',35,200000,7000000),
-- ORD-0018
('ORD-0018','P-501',60,150000,9000000),
-- ORD-0019
('ORD-0019','P-601',100,35000,3500000),('ORD-0019','P-602',54,28000,1512000),
-- ORD-0020
('ORD-0020','P-601',100,35000,3500000),('ORD-0020','P-303',100,5000,500000),
-- ORD-0021
('ORD-0021','P-602',150,28000,4200000),('ORD-0021','P-304',277,6500,1800500),
-- ORD-0022
('ORD-0022','P-601',100,35000,3500000),
-- ORD-0023
('ORD-0023','P-602',71,28000,1988000),
-- ORD-0024
('ORD-0024','P-303',600,5000,3000000),
-- ORD-0025
('ORD-0025','P-304',500,6500,3250000),('ORD-0025','P-302',125,10000,1250000),
-- ORD-0026
('ORD-0026','P-301',200,8000,1600000),('ORD-0026','P-303',240,5000,1200000),
-- ORD-0027
('ORD-0027','P-104',75,120000,9000000),
-- ORD-0028
('ORD-0028','P-104',58,120000,6960000),('ORD-0028','P-702',5,6800,34000),
-- ORD-0029
('ORD-0029','P-102',50,95000,4750000),('ORD-0029','P-701',5,4500,22500),('ORD-0029','P-702',100,6800,680000),
-- ORD-0030
('ORD-0030','P-402',160,25000,4000000),
-- ORD-0031
('ORD-0031','P-401',166,3000,498000),
-- ORD-0032
('ORD-0032','P-401',233,3000,699000),
-- ORD-0033
('ORD-0033','P-702',58,6800,394400),
-- ORD-0034
('ORD-0034','P-203',90,22000,1980000),
-- ORD-0035
('ORD-0035','P-202',100,18000,1800000),
-- ORD-0036
('ORD-0036','P-201',100,15000,1500000),('ORD-0036','P-203',32,22000,704000),
-- ORD-0037
('ORD-0037','P-202',83,18000,1494000),
-- 過去クローズ分
('ORD-0038','弁P-101',62,80000,4960000),
('ORD-0039','弁P-101',87,80000,6960000),
('ORD-0040','P-502',45,200000,9000000),
('ORD-0041','P-103',44,72000,3168000),
('ORD-0042','P-402',48,25000,1200000),
('ORD-0043','P-501',100,150000,15000000),
('ORD-0043','P-502',10,200000,2000000),
('ORD-0043','P-502',25,200000,5000000),
('ORD-0044','P-601',157,35000,5495000),('ORD-0044','P-304',477,6500,3100500),
('ORD-0045','P-303',620,5000,3100000), ('ORD-0045','P-304',108,6500,702000),
('ORD-0046','P-104',71,120000,8520000),('ORD-0046','P-702',363,6800,2468400),
('ORD-0047','P-401',216,3000,648000),
('ORD-0048','P-203',90,22000,1980000),('ORD-0048','P-202',28,18000,504000),
('ORD-0049','P-102',42,95000,3990000),
('ORD-0050','P-501',100,150000,15000000),
('ORD-0051','P-103',38,72000,2736000),
('ORD-0052','P-602',150,28000,4200000),
('ORD-0053','P-301',387,8000,3096000),
('ORD-0054','P-104',70,120000,8400000),('ORD-0054','P-701',1,4500,4500),
('ORD-0055','P-203',86,22000,1892000),
('ORD-0056','弁P-101',56,80000,4480000),
('ORD-0057','P-402',30,25000,750000),
('ORD-0058','P-401',160,3000,480000),
('ORD-0059','P-102',68,95000,6460000),
('ORD-0060','P-502',55,200000,11000000);

INSERT INTO GridRows (RowId, TableId, OrderIndex, CellsJson)
SELECT
    CONCAT(N'erp_ol_', ol.OrderNo, N'_', ol.ProductCode, N'_',
           ROW_NUMBER() OVER (PARTITION BY ol.OrderNo, ol.ProductCode ORDER BY (SELECT NULL))),
    N'erp_orderlines',
    ROW_NUMBER() OVER (ORDER BY ol.OrderNo, ol.ProductCode),
    CONCAT(N'{"OrderNo":"',     ol.OrderNo,
           N'","ProductCode":"',ol.ProductCode,
           N'","Quantity":"',   CAST(ol.Qty AS NVARCHAR(10)),
           N'","UnitPrice":"',  CAST(ol.UnitPrice AS NVARCHAR(20)),
           N'","LineAmount":"', CAST(ol.LineAmt AS NVARCHAR(20)), N'"}')
FROM #OL ol;

-- -----------------------------------------------------------------------
-- erp_inventory (20品番)
-- 主役: 弁P-101 OnHandQty=80 / AllocatedQty=0 / SafetyStock=50
-- -----------------------------------------------------------------------
INSERT INTO GridRows (RowId, TableId, OrderIndex, CellsJson) VALUES
('erp_inv_弁P-101','erp_inventory',1, N'{"ProductCode":"弁P-101","OnHandQty":"80","AllocatedQty":"0","SafetyStock":"50","LastUpdated":"2026-05-31"}'),
('erp_inv_P-102','erp_inventory',2, N'{"ProductCode":"P-102","OnHandQty":"45","AllocatedQty":"5","SafetyStock":"20","LastUpdated":"2026-05-31"}'),
('erp_inv_P-103','erp_inventory',3, N'{"ProductCode":"P-103","OnHandQty":"120","AllocatedQty":"10","SafetyStock":"30","LastUpdated":"2026-05-31"}'),
('erp_inv_P-104','erp_inventory',4, N'{"ProductCode":"P-104","OnHandQty":"30","AllocatedQty":"8","SafetyStock":"15","LastUpdated":"2026-05-31"}'),
('erp_inv_P-201','erp_inventory',5, N'{"ProductCode":"P-201","OnHandQty":"500","AllocatedQty":"20","SafetyStock":"100","LastUpdated":"2026-05-31"}'),
('erp_inv_P-202','erp_inventory',6, N'{"ProductCode":"P-202","OnHandQty":"400","AllocatedQty":"15","SafetyStock":"80","LastUpdated":"2026-05-31"}'),
('erp_inv_P-203','erp_inventory',7, N'{"ProductCode":"P-203","OnHandQty":"350","AllocatedQty":"12","SafetyStock":"60","LastUpdated":"2026-05-31"}'),
('erp_inv_P-204','erp_inventory',8, N'{"ProductCode":"P-204","OnHandQty":"600","AllocatedQty":"0","SafetyStock":"100","LastUpdated":"2026-05-31"}'),
('erp_inv_P-301','erp_inventory',9, N'{"ProductCode":"P-301","OnHandQty":"800","AllocatedQty":"30","SafetyStock":"150","LastUpdated":"2026-05-31"}'),
('erp_inv_P-302','erp_inventory',10,N'{"ProductCode":"P-302","OnHandQty":"600","AllocatedQty":"25","SafetyStock":"100","LastUpdated":"2026-05-31"}'),
('erp_inv_P-303','erp_inventory',11,N'{"ProductCode":"P-303","OnHandQty":"1200","AllocatedQty":"50","SafetyStock":"200","LastUpdated":"2026-05-31"}'),
('erp_inv_P-304','erp_inventory',12,N'{"ProductCode":"P-304","OnHandQty":"900","AllocatedQty":"40","SafetyStock":"150","LastUpdated":"2026-05-31"}'),
('erp_inv_P-401','erp_inventory',13,N'{"ProductCode":"P-401","OnHandQty":"2000","AllocatedQty":"80","SafetyStock":"300","LastUpdated":"2026-05-31"}'),
('erp_inv_P-402','erp_inventory',14,N'{"ProductCode":"P-402","OnHandQty":"150","AllocatedQty":"10","SafetyStock":"30","LastUpdated":"2026-05-31"}'),
('erp_inv_P-501','erp_inventory',15,N'{"ProductCode":"P-501","OnHandQty":"12","AllocatedQty":"2","SafetyStock":"5","LastUpdated":"2026-05-31"}'),
('erp_inv_P-502','erp_inventory',16,N'{"ProductCode":"P-502","OnHandQty":"8","AllocatedQty":"1","SafetyStock":"3","LastUpdated":"2026-05-31"}'),
('erp_inv_P-601','erp_inventory',17,N'{"ProductCode":"P-601","OnHandQty":"90","AllocatedQty":"20","SafetyStock":"30","LastUpdated":"2026-05-31"}'),
('erp_inv_P-602','erp_inventory',18,N'{"ProductCode":"P-602","OnHandQty":"200","AllocatedQty":"15","SafetyStock":"50","LastUpdated":"2026-05-31"}'),
('erp_inv_P-701','erp_inventory',19,N'{"ProductCode":"P-701","OnHandQty":"5000","AllocatedQty":"0","SafetyStock":"500","LastUpdated":"2026-05-31"}'),
('erp_inv_P-702','erp_inventory',20,N'{"ProductCode":"P-702","OnHandQty":"3000","AllocatedQty":"0","SafetyStock":"300","LastUpdated":"2026-05-31"}');

-- -----------------------------------------------------------------------
-- erp_production_capacity: 4品番×90日 (CTE で生成)
-- 弁P-101: 1-14日は 9/8 交互パターン(合計121) / 15日以降は8
-- P-102: 12 / P-103: 15 / P-104: 5
-- -----------------------------------------------------------------------
WITH Nums AS (
    SELECT n FROM (VALUES
    (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),
    (11),(12),(13),(14),(15),(16),(17),(18),(19),(20),
    (21),(22),(23),(24),(25),(26),(27),(28),(29),(30),
    (31),(32),(33),(34),(35),(36),(37),(38),(39),(40),
    (41),(42),(43),(44),(45),(46),(47),(48),(49),(50),
    (51),(52),(53),(54),(55),(56),(57),(58),(59),(60),
    (61),(62),(63),(64),(65),(66),(67),(68),(69),(70),
    (71),(72),(73),(74),(75),(76),(77),(78),(79),(80),
    (81),(82),(83),(84),(85),(86),(87),(88),(89),(90)
    ) t(n)
),
Prods AS (
    SELECT ProductCode, BaseUnits FROM (VALUES
    ('弁P-101',8),('P-102',12),('P-103',15),('P-104',5)
    ) p(ProductCode, BaseUnits)
)
INSERT INTO GridRows (RowId, TableId, OrderIndex, CellsJson)
SELECT
    CONCAT(N'erp_cap_', p.ProductCode, N'_d', n.n),
    N'erp_production_capacity',
    ROW_NUMBER() OVER (ORDER BY p.ProductCode, n.n),
    CONCAT(N'{"ProductCode":"', p.ProductCode,
           N'","CapacityDate":"',
           CONVERT(NVARCHAR(10), DATEADD(day, n.n, CAST(GETDATE() AS DATE)), 23),
           N'","AvailableUnits":"',
           CASE WHEN p.ProductCode = '弁P-101' AND n.n <= 14
                    THEN CASE WHEN n.n IN (2,5,7,10,13) THEN N'8' ELSE N'9' END
                ELSE CAST(p.BaseUnits AS NVARCHAR(4))
           END,
           N'","ReservedUnits":"0"}')
FROM Nums n CROSS JOIN Prods p;

-- -----------------------------------------------------------------------
-- erp_credithistory (A001: 3件)
-- -----------------------------------------------------------------------
INSERT INTO GridRows (RowId, TableId, OrderIndex, CellsJson) VALUES
('erp_credit_A001_1','erp_credithistory',1,
 CONCAT(N'{"CustomerCode":"A001","OccurredAt":"',
        CONVERT(NVARCHAR(19), DATEADD(month,-3,SYSUTCDATETIME()), 126),
        N'","EventType":"PaymentDelay","Note":"支払期日を5日超過。担当営業へ督促済み。"}')),
('erp_credit_A001_2','erp_credithistory',2,
 CONCAT(N'{"CustomerCode":"A001","OccurredAt":"',
        CONVERT(NVARCHAR(19), DATEADD(month,-2,SYSUTCDATETIME()), 126),
        N'","EventType":"NormalPayment","Note":"期日通り入金確認。"}')),
('erp_credit_A001_3','erp_credithistory',3,
 CONCAT(N'{"CustomerCode":"A001","OccurredAt":"',
        CONVERT(NVARCHAR(19), DATEADD(month,-1,SYSUTCDATETIME()), 126),
        N'","EventType":"NormalPayment","Note":"期日通り入金確認。"}'));
