-- =============================================================================
-- OpsContext Users スキーマ (Azure SQL Database / T-SQL)
-- 管理者は Role = 'Admin' で表現する。IsAdmin 列は持たない。
-- シードデータは OpsContext.Web 起動時に SqlUserStore.SeedIfEmptyAsync が自動投入する。
-- 手動投入は sql/seed_users.sql を使用すること。
-- =============================================================================

IF OBJECT_ID('Users', 'U') IS NOT NULL DROP TABLE Users;

CREATE TABLE Users (
    UserId         INT             NOT NULL IDENTITY(1,1),
    UserName       NVARCHAR(50)    NOT NULL,
    DisplayName    NVARCHAR(100)   NOT NULL,
    Role           NVARCHAR(20)    NOT NULL,   -- Admin / Sales / Purchasing / Production / Accounting
    PasswordHash   NVARCHAR(512)   NOT NULL,   -- PBKDF2-SHA256 (salt + hash を Base64 化)
    IsActive       BIT             NOT NULL DEFAULT 1,
    PersonalPrompt NVARCHAR(MAX)   NULL,       -- アカウント別追加プロンプト
    CreatedAt      DATETIME2(0)    NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_Users PRIMARY KEY (UserId),
    CONSTRAINT UQ_Users_UserName UNIQUE (UserName)
);

-- =============================================================================
-- 既存 DB への冪等 ALTER（DROP/CREATE できない場合に使用）
-- IsAdmin 列が残っている場合は削除、PersonalPrompt がなければ追加
-- =============================================================================

IF COL_LENGTH('Users', 'IsAdmin') IS NOT NULL
    ALTER TABLE Users DROP COLUMN IsAdmin;

IF COL_LENGTH('Users', 'PersonalPrompt') IS NULL
    ALTER TABLE Users ADD PersonalPrompt NVARCHAR(MAX) NULL;
