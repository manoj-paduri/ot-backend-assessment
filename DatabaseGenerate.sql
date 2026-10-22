/* =====================================================================================
   OT_Assessment_DB - schema for the Online Betting Data Capture and Analytics System

   Re-runnable (idempotent): creates the database if needed, then every object only if it is
   missing; procedures and the view use CREATE OR ALTER.

   Run with SSMS / Azure Data Studio, or:
       sqlcmd -S localhost -E -b -i DatabaseGenerate.sql                      (Windows auth)
       sqlcmd -S localhost,1433 -U sa -P <password> -C -b -i DatabaseGenerate.sql   (SQL auth / Docker)

   Model (all names are singular, PascalCase; procedures are usp_<Entity>_<Action>):

       Provider 1 --- * Game 1 --- * Wager * --- 1 Player

   Design notes
   - Provider / Game / Player are normalised so repeated text (provider and game names, usernames)
     is stored once; a wager only carries small keys.
   - Wager is clustered on an ever-increasing identity (append-only inserts, no page splits). The wager
     GUID, which arrives from the client, has its own unique index: that index is what makes the
     consumer idempotent (a redelivered message cannot create a second row).
   - Player keeps two running aggregates (TotalAmountSpend, WagerCount) that the insert procedure updates in
     the same transaction as the wagers. "Top spenders" and the wager count behind paging become index reads
     instead of aggregations over millions of rows.
   - Fields that no requirement uses (brand, external reference, transaction type, session data) are not stored.
   ===================================================================================== */

USE [master];
GO

IF DB_ID(N'OT_Assessment_DB') IS NULL
BEGIN
    CREATE DATABASE [OT_Assessment_DB];
END
GO

-- Row versioning: readers (the GET endpoints) never block behind the consumer's write transactions.
IF EXISTS (SELECT 1 FROM sys.databases WHERE name = N'OT_Assessment_DB' AND is_read_committed_snapshot_on = 0)
BEGIN
    ALTER DATABASE [OT_Assessment_DB] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
END
GO

USE [OT_Assessment_DB];
GO

/* ------------------------------------------------------------------ Tables --- */

IF OBJECT_ID(N'dbo.Provider', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Provider
    (
        ProviderId  int            IDENTITY(1, 1) NOT NULL,
        Name        nvarchar(200)  NOT NULL,
        CONSTRAINT PK_Provider      PRIMARY KEY CLUSTERED (ProviderId),
        CONSTRAINT UQ_Provider_Name UNIQUE (Name)
    );
END
GO

IF OBJECT_ID(N'dbo.Game', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Game
    (
        GameId      int            IDENTITY(1, 1) NOT NULL,
        ProviderId  int            NOT NULL,
        Name        nvarchar(200)  NOT NULL,
        Theme       nvarchar(100)  NULL,
        CONSTRAINT PK_Game                   PRIMARY KEY CLUSTERED (GameId),
        CONSTRAINT FK_Game_Provider          FOREIGN KEY (ProviderId) REFERENCES dbo.Provider (ProviderId),
        CONSTRAINT UQ_Game_ProviderId_Name   UNIQUE (ProviderId, Name)
    );
END
GO

IF OBJECT_ID(N'dbo.Player', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Player
    (
        AccountId         uniqueidentifier NOT NULL,
        Username          nvarchar(100)    NOT NULL,
        TotalAmountSpend  decimal(19, 4)   NOT NULL CONSTRAINT DF_Player_TotalAmountSpend DEFAULT (0),
        WagerCount        int              NOT NULL CONSTRAINT DF_Player_WagerCount       DEFAULT (0),
        CreatedUtc        datetime2(3)     NOT NULL CONSTRAINT DF_Player_CreatedUtc       DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_Player PRIMARY KEY CLUSTERED (AccountId)
    );
END
GO

IF OBJECT_ID(N'dbo.Wager', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Wager
    (
        WagerSeq            bigint           IDENTITY(1, 1) NOT NULL,
        WagerId             uniqueidentifier NOT NULL,
        AccountId           uniqueidentifier NOT NULL,
        GameId              int              NOT NULL,
        TransactionId       uniqueidentifier NOT NULL,
        CountryCode         varchar(3)       NULL,
        Amount              decimal(19, 4)   NOT NULL,
        NumberOfBets        int              NOT NULL,
        Duration            bigint           NOT NULL,
        CreatedDateTimeUtc  datetime2(3)     NOT NULL,
        InsertedUtc         datetime2(3)     NOT NULL CONSTRAINT DF_Wager_InsertedUtc DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_Wager          PRIMARY KEY CLUSTERED (WagerSeq),
        CONSTRAINT FK_Wager_Player   FOREIGN KEY (AccountId) REFERENCES dbo.Player (AccountId),
        CONSTRAINT FK_Wager_Game     FOREIGN KEY (GameId)    REFERENCES dbo.Game (GameId)
    );
END
GO

/* ----------------------------------------------------------------- Indexes --- */

-- Idempotency: one row per wager GUID, and the lookup used by the insert procedure.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Wager_WagerId' AND object_id = OBJECT_ID(N'dbo.Wager'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX UX_Wager_WagerId ON dbo.Wager (WagerId);
END
GO

-- GET api/player/{playerId}/casino: newest wagers of one player. Key order matches the ORDER BY of the
-- procedure and the included columns cover it, so a page is a short range scan with no sort.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Wager_AccountId_CreatedDateTimeUtc' AND object_id = OBJECT_ID(N'dbo.Wager'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Wager_AccountId_CreatedDateTimeUtc
        ON dbo.Wager (AccountId ASC, CreatedDateTimeUtc DESC, WagerSeq DESC)
        INCLUDE (WagerId, GameId, Amount);
END
GO

-- Foreign key support for joins from Game to Wager (e.g. analytics per game).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Wager_GameId' AND object_id = OBJECT_ID(N'dbo.Wager'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Wager_GameId ON dbo.Wager (GameId);
END
GO

-- GET api/player/topSpenders: the index is already in "highest first" order.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Player_TotalAmountSpend' AND object_id = OBJECT_ID(N'dbo.Player'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Player_TotalAmountSpend
        ON dbo.Player (TotalAmountSpend DESC)
        INCLUDE (Username);
END
GO

/* ------------------------------------------------------------- Table type --- */

-- Shape of one batch sent by the consumer. Must mirror SqlWagerStore.BatchColumns.
IF TYPE_ID(N'dbo.WagerBatchType') IS NULL
BEGIN
    CREATE TYPE dbo.WagerBatchType AS TABLE
    (
        WagerId             uniqueidentifier NOT NULL,
        AccountId           uniqueidentifier NOT NULL,
        Username            nvarchar(100)    NOT NULL,
        Provider            nvarchar(200)    NOT NULL,
        GameName            nvarchar(200)    NOT NULL,
        Theme               nvarchar(100)    NULL,
        TransactionId       uniqueidentifier NOT NULL,
        CountryCode         varchar(3)       NULL,
        Amount              decimal(19, 4)   NOT NULL,
        NumberOfBets        int              NOT NULL,
        Duration            bigint           NOT NULL,
        CreatedDateTimeUtc  datetime2(7)     NOT NULL
    );
END
GO

/* -------------------------------------------------------------------- View --- */

-- Flattened wager with its game, provider and player: the reporting/analytics entry point.
CREATE OR ALTER VIEW dbo.vw_WagerDetail
AS
SELECT
    w.WagerSeq,
    w.WagerId,
    w.AccountId,
    pl.Username,
    pr.Name  AS Provider,
    g.Name   AS Game,
    g.Theme,
    w.TransactionId,
    w.CountryCode,
    w.Amount,
    w.NumberOfBets,
    w.Duration,
    w.CreatedDateTimeUtc
FROM dbo.Wager    AS w
INNER JOIN dbo.Player   AS pl ON pl.AccountId  = w.AccountId
INNER JOIN dbo.Game     AS g  ON g.GameId      = w.GameId
INNER JOIN dbo.Provider AS pr ON pr.ProviderId = g.ProviderId;
GO

/* ------------------------------------------------------------- Procedures --- */

/* Write path. Idempotent batch insert used by OT.Assessment.Consumer.
   One transaction: providers -> games -> players -> wagers -> per-player totals.
   Returns a single value: how many wagers were newly inserted (duplicates are skipped and not counted).
   UPDLOCK + HOLDLOCK on the "does it exist?" checks make concurrent consumers safe when they meet
   the same new provider/game/player at the same time. */
CREATE OR ALTER PROCEDURE dbo.usp_Wager_InsertBatch
    @Wagers dbo.WagerBatchType READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Inserted TABLE
    (
        AccountId uniqueidentifier NOT NULL,
        Amount    decimal(19, 4)   NOT NULL
    );

    BEGIN TRANSACTION;

    INSERT INTO dbo.Provider (Name)
    SELECT DISTINCT w.Provider
    FROM @Wagers AS w
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.Provider AS p WITH (UPDLOCK, HOLDLOCK)
        WHERE p.Name = w.Provider
    );

    INSERT INTO dbo.Game (ProviderId, Name, Theme)
    SELECT src.ProviderId, src.GameName, src.Theme
    FROM
    (
        SELECT
            p.ProviderId,
            w.GameName,
            w.Theme,
            ROW_NUMBER() OVER (PARTITION BY p.ProviderId, w.GameName ORDER BY (SELECT NULL)) AS rn
        FROM @Wagers AS w
        INNER JOIN dbo.Provider AS p ON p.Name = w.Provider
    ) AS src
    WHERE src.rn = 1
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.Game AS g WITH (UPDLOCK, HOLDLOCK)
          WHERE g.ProviderId = src.ProviderId
            AND g.Name       = src.GameName
      );

    INSERT INTO dbo.Player (AccountId, Username)
    SELECT latest.AccountId, latest.Username
    FROM
    (
        SELECT
            w.AccountId,
            w.Username,
            ROW_NUMBER() OVER (PARTITION BY w.AccountId ORDER BY w.CreatedDateTimeUtc DESC) AS rn
        FROM @Wagers AS w
    ) AS latest
    WHERE latest.rn = 1
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.Player AS p WITH (UPDLOCK, HOLDLOCK)
          WHERE p.AccountId = latest.AccountId
      );

    INSERT INTO dbo.Wager
        (WagerId, AccountId, GameId, TransactionId, CountryCode, Amount, NumberOfBets, Duration, CreatedDateTimeUtc)
    OUTPUT inserted.AccountId, inserted.Amount INTO @Inserted (AccountId, Amount)
    SELECT
        w.WagerId,
        w.AccountId,
        g.GameId,
        w.TransactionId,
        w.CountryCode,
        w.Amount,
        w.NumberOfBets,
        w.Duration,
        CAST(w.CreatedDateTimeUtc AS datetime2(3))
    FROM
    (
        SELECT t.*, ROW_NUMBER() OVER (PARTITION BY t.WagerId ORDER BY (SELECT NULL)) AS rn
        FROM @Wagers AS t
    ) AS w
    INNER JOIN dbo.Provider AS p ON p.Name       = w.Provider
    INNER JOIN dbo.Game     AS g ON g.ProviderId = p.ProviderId AND g.Name = w.GameName
    WHERE w.rn = 1
      AND NOT EXISTS (SELECT 1 FROM dbo.Wager AS existing WHERE existing.WagerId = w.WagerId);

    -- Only wagers that were really inserted move the totals, so a redelivered message changes nothing.
    UPDATE pl
    SET pl.TotalAmountSpend = pl.TotalAmountSpend + added.TotalAmount,
        pl.WagerCount       = pl.WagerCount       + added.WagerCount
    FROM dbo.Player AS pl
    INNER JOIN
    (
        SELECT AccountId, SUM(Amount) AS TotalAmount, COUNT(*) AS WagerCount
        FROM @Inserted
        GROUP BY AccountId
    ) AS added ON added.AccountId = pl.AccountId;

    COMMIT TRANSACTION;

    SELECT COUNT(*) AS InsertedCount FROM @Inserted;
END
GO

/* GET api/player/{playerId}/casino
   Result set 1: the requested page, newest first.  Result set 2: the player's total number of wagers.
   @Page is 1-based. The offset is computed as bigint so huge page numbers cannot overflow. */
CREATE OR ALTER PROCEDURE dbo.usp_Wager_GetPageByAccount
    @AccountId uniqueidentifier,
    @Page      int,
    @PageSize  int
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        v.WagerId,
        v.Game,
        v.Provider,
        v.Amount,
        v.CreatedDateTimeUtc AS CreatedDate
    FROM dbo.vw_WagerDetail AS v
    WHERE v.AccountId = @AccountId
    ORDER BY v.CreatedDateTimeUtc DESC, v.WagerSeq DESC
    OFFSET CAST(@Page - 1 AS bigint) * @PageSize ROWS
    FETCH NEXT @PageSize ROWS ONLY;

    SELECT ISNULL((SELECT pl.WagerCount FROM dbo.Player AS pl WHERE pl.AccountId = @AccountId), 0) AS Total;
END
GO

/* GET api/admin/stats (load-test reconciliation): how many wagers and players are stored.
   COUNT_BIG(*) is answered from the narrowest index (UX_Wager_WagerId), so it stays cheap on a large table. */
CREATE OR ALTER PROCEDURE dbo.usp_Ingestion_GetStats
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        (SELECT COUNT_BIG(*) FROM dbo.Wager)  AS StoredWagers,
        (SELECT COUNT_BIG(*) FROM dbo.Player) AS StoredPlayers;
END
GO

/* GET api/player/topSpenders: highest total spend first (ties broken by AccountId for a stable order). */
CREATE OR ALTER PROCEDURE dbo.usp_Player_GetTopSpenders
    @Count int
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@Count)
        pl.AccountId,
        pl.Username,
        pl.TotalAmountSpend
    FROM dbo.Player AS pl
    ORDER BY pl.TotalAmountSpend DESC, pl.AccountId;
END
GO
