using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OT.Assessment.Core.Wagers;
using OT.Assessment.Infrastructure.Messaging;
using OT.Assessment.Infrastructure.Resilience;

namespace OT.Assessment.Consumer;

/// <summary>
/// Turns a batch of broker deliveries into stored wagers and settles each delivery.
///
/// Delivery guarantees:
/// - A message is acknowledged only after it is committed to SQL Server (at-least-once). The store is
///   idempotent, so a redelivered message cannot be stored twice: the net effect is exactly-once.
/// - When SQL Server is unavailable the batch is retried with backoff, indefinitely: the messages simply stay
///   unacknowledged in the queue and nothing is lost.
/// - A message that can never be stored (malformed, or rejected by the database for a data reason) is
///   dead-lettered on its own, so one bad message cannot block or discard the good ones around it.
/// </summary>
public sealed class WagerBatchProcessor
{
    private readonly IWagerStore _store;
    private readonly WagerConsumerOptions _options;
    private readonly ILogger<WagerBatchProcessor> _logger;

    private long _totalReceived;
    private long _totalStored;
    private long _totalDeadLettered;
    private long _firstBatchTimestamp;

    public WagerBatchProcessor(
        IWagerStore store,
        IOptions<WagerConsumerOptions> options,
        ILogger<WagerBatchProcessor> logger)
    {
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ProcessAsync(
        IReadOnlyList<WagerDelivery> deliveries, IMessageAcknowledger acknowledger, CancellationToken cancellationToken)
    {
        if (deliveries.Count == 0)
        {
            return;
        }

        var startedAt = Stopwatch.GetTimestamp();
        Interlocked.CompareExchange(ref _firstBatchTimestamp, startedAt, 0);

        var parsed = new List<ParsedDelivery>(deliveries.Count);
        var deadLettered = 0;
        ulong highestTag = 0;

        foreach (var delivery in deliveries)
        {
            highestTag = Math.Max(highestTag, delivery.DeliveryTag);

            if (WagerMessageSerializer.TryDeserialize(delivery.Body, out var message))
            {
                parsed.Add(new ParsedDelivery(delivery.DeliveryTag, message));
            }
            else
            {
                _logger.LogWarning("Delivery {DeliveryTag} is not a valid wager; dead-lettering it", delivery.DeliveryTag);
                await acknowledger.RejectAsync(delivery.DeliveryTag, cancellationToken).ConfigureAwait(false);
                deadLettered++;
            }
        }

        var inserted = 0;
        var duplicates = 0;
        if (parsed.Count > 0)
        {
            var outcome = await PersistAsync(parsed, acknowledger, cancellationToken).ConfigureAwait(false);
            inserted = outcome.Inserted;
            deadLettered += outcome.DeadLettered;
            duplicates = parsed.Count - outcome.Inserted - outcome.DeadLettered;
        }

        // Every delivery up to the highest tag is now settled (stored, duplicate, or already rejected above),
        // so one multi-ack covers the whole batch.
        await acknowledger.AcknowledgeUpToAsync(highestTag, cancellationToken).ConfigureAwait(false);

        var received = Interlocked.Add(ref _totalReceived, deliveries.Count);
        var stored = Interlocked.Add(ref _totalStored, inserted);
        var dead = Interlocked.Add(ref _totalDeadLettered, deadLettered);

        _logger.LogInformation(
            "Batch of {Received}: stored {Inserted}, duplicates {Duplicates}, dead-lettered {DeadLettered} in {ElapsedMs:F0} ms. " +
            "Running totals: received {TotalReceived}, stored {TotalStored}, dead-lettered {TotalDead}, running for {RunningFor:F1}s",
            deliveries.Count,
            inserted,
            duplicates,
            deadLettered,
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
            received,
            stored,
            dead,
            Stopwatch.GetElapsedTime(Interlocked.Read(ref _firstBatchTimestamp)).TotalSeconds);
    }

    private async Task<PersistOutcome> PersistAsync(
        List<ParsedDelivery> parsed, IMessageAcknowledger acknowledger, CancellationToken cancellationToken)
    {
        try
        {
            var messages = parsed.Select(p => p.Message).ToList();
            var inserted = await SaveWithRetryAsync(messages, cancellationToken).ConfigureAwait(false);
            return new PersistOutcome(inserted, 0);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex, "Batch of {Count} wagers failed with a non-transient error; storing them one by one to isolate it", parsed.Count);
        }

        var insertedTotal = 0;
        var deadLettered = 0;

        foreach (var item in parsed)
        {
            try
            {
                insertedTotal += await SaveWithRetryAsync(new[] { item.Message }, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Wager {WagerId} cannot be stored; dead-lettering it", item.Message.WagerId);
                await acknowledger.RejectAsync(item.DeliveryTag, cancellationToken).ConfigureAwait(false);
                deadLettered++;
            }
        }

        return new PersistOutcome(insertedTotal, deadLettered);
    }

    /// <summary>Retries transient failures with capped exponential backoff; non-transient failures propagate.</summary>
    private async Task<int> SaveWithRetryAsync(IReadOnlyCollection<CasinoWagerMessage> messages, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromMilliseconds(Math.Max(1, _options.RetryInitialDelayMilliseconds));
        var maxDelay = TimeSpan.FromMilliseconds(Math.Max(1, _options.RetryMaxDelayMilliseconds));

        while (true)
        {
            try
            {
                return await _store.SaveBatchAsync(messages, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && TransientErrorClassifier.IsTransient(ex))
            {
                _logger.LogWarning(ex, "Transient storage failure; retrying {Count} wagers in {Delay}", messages.Count, delay);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, maxDelay.TotalMilliseconds));
            }
        }
    }

    private readonly record struct ParsedDelivery(ulong DeliveryTag, CasinoWagerMessage Message);

    private readonly record struct PersistOutcome(int Inserted, int DeadLettered);
}
