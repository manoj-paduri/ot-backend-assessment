/* Post-run integrity checks for OT_Assessment_DB. Every "must be 0" query returns no rows when the data is consistent.
   Run:  sqlcmd -S localhost -E -C -d OT_Assessment_DB -i scripts/Reconcile.sql
   (SQL auth / Docker: sqlcmd -S localhost,1433 -U sa -P OtAssessment_Passw0rd1 -C -d OT_Assessment_DB -i scripts/Reconcile.sql) */
SET NOCOUNT ON;

PRINT '1. Stored wagers vs distinct wager ids (equal = no duplicates)';
SELECT COUNT_BIG(*) AS StoredWagers, COUNT_BIG(DISTINCT WagerId) AS DistinctWagerIds FROM dbo.Wager;

PRINT '2. Time to store: first and last insert (this is the ingest duration of the run)';
SELECT
    MIN(InsertedUtc) AS FirstInsertedUtc,
    MAX(InsertedUtc) AS LastInsertedUtc,
    DATEDIFF_BIG(MILLISECOND, MIN(InsertedUtc), MAX(InsertedUtc)) / 1000.0 AS StoringSeconds
FROM dbo.Wager;

PRINT '3. Player running totals must equal the sum of their wagers (must be 0 rows)';
SELECT p.AccountId, p.WagerCount, x.WagerCountRecomputed, p.TotalAmountSpend, x.TotalRecomputed
FROM dbo.Player AS p
CROSS APPLY
(
    SELECT COUNT(*) AS WagerCountRecomputed, ISNULL(SUM(w.Amount), 0) AS TotalRecomputed
    FROM dbo.Wager AS w
    WHERE w.AccountId = p.AccountId
) AS x
WHERE p.WagerCount <> x.WagerCountRecomputed OR p.TotalAmountSpend <> x.TotalRecomputed;

PRINT '4. Wagers without a player or a game (must be 0 rows)';
SELECT w.WagerSeq FROM dbo.Wager AS w
WHERE NOT EXISTS (SELECT 1 FROM dbo.Player AS p WHERE p.AccountId = w.AccountId)
   OR NOT EXISTS (SELECT 1 FROM dbo.Game   AS g WHERE g.GameId    = w.GameId);

PRINT '5. Top 5 spenders as served by the API vs recomputed from wagers (columns must match)';
SELECT TOP (5) pl.Username, pl.TotalAmountSpend AS FromPlayerTable,
       (SELECT SUM(w.Amount) FROM dbo.Wager AS w WHERE w.AccountId = pl.AccountId) AS RecomputedFromWagers
FROM dbo.Player AS pl
ORDER BY pl.TotalAmountSpend DESC;
