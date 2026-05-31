-- =============================================================================
-- OpsContext LoginHistory スキーマ (Azure SQL Database / T-SQL)
-- ログイン試行（成功・失敗）をクライアント IP / User-Agent とともに記録する。
-- 本テーブルは SqlLoginHistoryStore.EnsureSchemaAsync が起動時に自動作成するため、
-- 手動 DDL 適用は任意。再実行しても既存データは保持される（CREATE IF NOT EXISTS 方式）。
-- =============================================================================

IF OBJECT_ID('LoginHistory', 'U') IS NOT NULL DROP TABLE LoginHistory;

CREATE TABLE LoginHistory (
    Id          BIGINT          NOT NULL IDENTITY(1,1),
    UserName    NVARCHAR(50)    NOT NULL,           -- 入力されたユーザー名（失敗時も記録）
    DisplayName NVARCHAR(100)   NULL,               -- 成功時のみ解決
    Role        NVARCHAR(20)    NULL,               -- 成功時のみ解決
    Success     BIT             NOT NULL,
    ClientIp    NVARCHAR(64)    NULL,
    UserAgent   NVARCHAR(512)   NULL,
    CreatedAt   DATETIME2(0)    NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_LoginHistory PRIMARY KEY (Id)
);

CREATE INDEX IX_LoginHistory_CreatedAt ON LoginHistory (CreatedAt DESC);
CREATE INDEX IX_LoginHistory_UserName  ON LoginHistory (UserName);
