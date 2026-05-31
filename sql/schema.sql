-- =============================================================================
-- OpsContext ERP関連テーブル クリーンアップ (Azure SQL Database / T-SQL)
-- ERP基幹データは GridTables/GridRows の論理テーブルとして保持する。
-- このスクリプトは旧物理テーブルをすべて削除するだけ。
-- =============================================================================

-- 旧テーブル名 (移行前)
IF OBJECT_ID('CreditHistory',         'U') IS NOT NULL DROP TABLE CreditHistory;
IF OBJECT_ID('ProductionCapacity',    'U') IS NOT NULL DROP TABLE ProductionCapacity;
IF OBJECT_ID('Inventory',             'U') IS NOT NULL DROP TABLE Inventory;
IF OBJECT_ID('OrderLines',            'U') IS NOT NULL DROP TABLE OrderLines;
IF OBJECT_ID('Orders',                'U') IS NOT NULL DROP TABLE Orders;
IF OBJECT_ID('Products',              'U') IS NOT NULL DROP TABLE Products;
IF OBJECT_ID('Customers',             'U') IS NOT NULL DROP TABLE Customers;

-- Erp プレフィックス版
IF OBJECT_ID('ErpCreditHistory',      'U') IS NOT NULL DROP TABLE ErpCreditHistory;
IF OBJECT_ID('ErpProductionCapacity', 'U') IS NOT NULL DROP TABLE ErpProductionCapacity;
IF OBJECT_ID('ErpInventory',          'U') IS NOT NULL DROP TABLE ErpInventory;
IF OBJECT_ID('ErpOrderLines',         'U') IS NOT NULL DROP TABLE ErpOrderLines;
IF OBJECT_ID('ErpOrders',             'U') IS NOT NULL DROP TABLE ErpOrders;
IF OBJECT_ID('ErpProducts',           'U') IS NOT NULL DROP TABLE ErpProducts;
IF OBJECT_ID('ErpCustomers',          'U') IS NOT NULL DROP TABLE ErpCustomers;
