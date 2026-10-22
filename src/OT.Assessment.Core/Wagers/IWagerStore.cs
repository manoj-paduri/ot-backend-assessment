namespace OT.Assessment.Core.Wagers;

/// <summary>Persists wager events.</summary>
public interface IWagerStore
{
    /// <summary>
    /// Stores a batch of wagers in a single transaction. The operation is idempotent:
    /// wagers that were already stored (same <see cref="CasinoWagerMessage.WagerId"/>) are skipped,
    /// so a redelivered message can never be counted twice.
    /// </summary>
    /// <returns>The number of wagers that were newly inserted.</returns>
    Task<int> SaveBatchAsync(IReadOnlyCollection<CasinoWagerMessage> wagers, CancellationToken cancellationToken);
}
