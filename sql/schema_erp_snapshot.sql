-- =============================================================================
-- OpsContext ERPスナップショット メタテーブル (Azure SQL Database / T-SQL)
-- LastSyncedAt を保持するだけの軽量テーブル。
-- 実データは ErpCustomers / ErpInventory / ErpProducts / ErpProductionCapacity /
-- ErpOrders / ErpOrderLines テーブルを SqlErpDatasetStore が SELECT する。
-- これらテーブルは API/ODBC 経由の定期同期でERPから投入する前提。
-- =============================================================================

IF OBJECT_ID('ErpSnapshotMeta', 'U') IS NULL
BEGIN
    CREATE TABLE ErpSnapshotMeta (
        Id           INT          NOT NULL IDENTITY(1,1),
        LastSyncedAt DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_ErpSnapshotMeta PRIMARY KEY (Id)
    );

    -- 初期行: 起動時点の日時を挿入
    INSERT INTO ErpSnapshotMeta (LastSyncedAt) VALUES (SYSUTCDATETIME());
END
