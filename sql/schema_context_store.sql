-- =============================================================================
-- OpsContext コンテキストストア スキーマ (Azure SQL Database / T-SQL)
-- 案件データは GridTables/GridRows に集約。ContextEntries/FocusSnapshots は
-- エージェントのログテーブルとして残し、CaseId は GridRows の RowId を参照する文字列。
-- 再実行可能: DROP してから CREATE
-- =============================================================================

-- -----------------------------------------------------------------------
-- DROP
-- -----------------------------------------------------------------------
IF OBJECT_ID('FocusSnapshots',  'U') IS NOT NULL DROP TABLE FocusSnapshots;
IF OBJECT_ID('ContextEntries',  'U') IS NOT NULL DROP TABLE ContextEntries;

-- -----------------------------------------------------------------------
-- ContextEntries: エージェントが記録する決定・観察・引継ぎなど
-- CaseId は GridRows.RowId（文字列）を参照する。FK 制約なし。
-- -----------------------------------------------------------------------
CREATE TABLE ContextEntries (
    EntryId         UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    CaseId          NVARCHAR(100)    NOT NULL,
    Role            NVARCHAR(30)     NOT NULL,   -- Sales / Purchasing / Production / Accounting / Curator
    Author          NVARCHAR(100)    NOT NULL,
    Kind            NVARCHAR(20)     NOT NULL,   -- decision / observation / question / answer / handoff
    Text            NVARCHAR(MAX)    NOT NULL,
    RefSql          NVARCHAR(MAX)    NULL,
    CreatedAt       DATETIME2(0)     NOT NULL DEFAULT SYSUTCDATETIME(),
    EmbeddingId     NVARCHAR(200)    NULL,
    CONSTRAINT PK_ContextEntries PRIMARY KEY (EntryId)
);
CREATE INDEX IX_ContextEntries_CaseId ON ContextEntries (CaseId);
CREATE INDEX IX_ContextEntries_Kind   ON ContextEntries (Kind);

-- -----------------------------------------------------------------------
-- FocusSnapshots: ロール別の現在フォーカスまとめ (CuratorAgent が書き込む)
-- CaseId は GridRows.RowId（文字列）を参照する。FK 制約なし。
-- -----------------------------------------------------------------------
CREATE TABLE FocusSnapshots (
    SnapshotId      UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    CaseId          NVARCHAR(100)    NOT NULL,
    Role            NVARCHAR(30)     NOT NULL,
    SummaryMd       NVARCHAR(MAX)    NOT NULL,
    CreatedAt       DATETIME2(0)     NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_FocusSnapshots PRIMARY KEY (SnapshotId)
);
CREATE INDEX IX_FocusSnapshots_CaseRole ON FocusSnapshots (CaseId, Role);
