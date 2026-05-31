-- =============================================================================
-- OpsContext RolePrompts スキーマ (Azure SQL Database / T-SQL)
-- ロール別追加プロンプトを格納するテーブル。再実行可能。
-- シードデータは OpsContext.Web 起動時に SqlRolePromptStore.SeedIfEmptyAsync が自動投入する。
-- =============================================================================

IF OBJECT_ID('RolePrompts', 'U') IS NOT NULL DROP TABLE RolePrompts;

CREATE TABLE RolePrompts (
    Role       NVARCHAR(20)   NOT NULL,          -- Sales / Purchasing / Production / Accounting
    Prompt     NVARCHAR(MAX)  NULL,              -- 追加指示テキスト (NULL = 指示なし)
    UpdatedAt  DATETIME2(0)   NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_RolePrompts PRIMARY KEY (Role)
);

-- =============================================================================
-- 初期データ（SqlRolePromptStore.SeedIfEmptyAsync で自動投入されるため通常は不要）
-- 手動投入する場合は以下を参考に実行する（Prompt は後から管理画面で更新可）
-- =============================================================================
-- INSERT INTO RolePrompts (Role) VALUES
--   ('Sales'),
--   ('Purchasing'),
--   ('Production'),
--   ('Accounting');
