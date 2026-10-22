namespace OT.Assessment.Consumer;

/// <summary>Tuning knobs of the consumer (section <c>Consumer</c>).</summary>
public sealed class WagerConsumerOptions
{
    public const string SectionName = "Consumer";

    /// <summary>Maximum number of wagers written to SQL Server in one transaction.</summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>
    /// How long to wait for a batch to fill before writing what has arrived. This bounds the latency of a
    /// wager when traffic is low; under load batches fill up long before it elapses.
    /// </summary>
    public int BatchMaxWaitMilliseconds { get; set; } = 200;

    /// <summary>
    /// Unacknowledged messages the broker may have in flight to this consumer. Must be at least
    /// <see cref="BatchSize"/>; twice the batch size lets the next batch fill while the current one is written.
    /// </summary>
    public ushort PrefetchCount { get; set; } = 1000;

    /// <summary>First delay when SQL Server is unavailable; doubles on every attempt up to <see cref="RetryMaxDelayMilliseconds"/>.</summary>
    public int RetryInitialDelayMilliseconds { get; set; } = 500;

    public int RetryMaxDelayMilliseconds { get; set; } = 15_000;

    /// <summary>Delay before the consumer re-establishes its RabbitMQ session after a failure.</summary>
    public int ReconnectDelayMilliseconds { get; set; } = 5_000;
}
