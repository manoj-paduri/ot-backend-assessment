namespace OT.Assessment.Core.Operations;

/// <summary>
/// A point-in-time view of the ingestion pipeline, used to reconcile what was submitted with what was stored.
/// </summary>
/// <param name="StoredWagers">Wagers committed to SQL Server.</param>
/// <param name="StoredPlayers">Distinct players known to SQL Server.</param>
/// <param name="QueuedWagers">Messages waiting in the wager queue (ready, not yet delivered to the consumer).</param>
/// <param name="DeadLetteredWagers">Messages parked in the dead-letter queue because they could never be stored.</param>
/// <param name="ServerTimeUtc">When the snapshot was taken.</param>
public sealed record IngestionStats(
    long StoredWagers,
    long StoredPlayers,
    long QueuedWagers,
    long DeadLetteredWagers,
    DateTime ServerTimeUtc);
