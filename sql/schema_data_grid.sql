-- =============================================================================
-- OpsContext 業務データ Grid スキーマ (Azure SQL Database / T-SQL)
-- GridTables: 業務データテーブル定義（列定義を JSON で保持）
-- GridRows:   業務データ行（セル辞書を JSON で保持）
-- 再実行可能: FK 依存順の逆順で DROP してから CREATE
-- =============================================================================

IF OBJECT_ID('GridRows',   'U') IS NOT NULL DROP TABLE GridRows;
IF OBJECT_ID('GridTables', 'U') IS NOT NULL DROP TABLE GridTables;

-- -----------------------------------------------------------------------
-- GridTables: 業務データのテーブル定義
-- -----------------------------------------------------------------------
CREATE TABLE GridTables (
    TableId     NVARCHAR(64)  NOT NULL,
    Name        NVARCHAR(200) NOT NULL,
    ColumnsJson NVARCHAR(MAX) NOT NULL DEFAULT '[]',  -- List<GridColumn> の JSON
    CreatedAt   DATETIME2(0)  NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt   DATETIME2(0)  NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_GridTables PRIMARY KEY (TableId)
);

-- -----------------------------------------------------------------------
-- GridRows: 業務データの行（セル値は JSON 辞書として保存）
-- -----------------------------------------------------------------------
CREATE TABLE GridRows (
    RowId       NVARCHAR(64)  NOT NULL,
    TableId     NVARCHAR(64)  NOT NULL,
    OrderIndex  INT           NOT NULL DEFAULT 0,
    CellsJson   NVARCHAR(MAX) NOT NULL DEFAULT '{}',  -- Dictionary<string,string?> の JSON
    CONSTRAINT PK_GridRows PRIMARY KEY (RowId),
    CONSTRAINT FK_GridRows_GridTables FOREIGN KEY (TableId)
        REFERENCES GridTables (TableId) ON DELETE CASCADE
);
CREATE INDEX IX_GridRows_TableId ON GridRows (TableId);
