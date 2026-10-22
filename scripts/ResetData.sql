/* Empties the wager and player data so a load test starts from zero. Providers and games are kept (they are re-learned anyway).
   Stop the consumer first and purge the queue in the RabbitMQ UI if it still holds messages.
   Run:  sqlcmd -S localhost -E -C -d OT_Assessment_DB -i scripts/ResetData.sql */
SET NOCOUNT ON;
TRUNCATE TABLE dbo.Wager;     -- nothing references Wager, so TRUNCATE is allowed; it also restarts the identity
DELETE FROM dbo.Player;
PRINT 'Wager and Player data removed.';
