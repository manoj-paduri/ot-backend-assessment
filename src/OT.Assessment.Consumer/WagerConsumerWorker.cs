using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OT.Assessment.Infrastructure.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OT.Assessment.Consumer;

/// <summary>
/// Hosts the RabbitMQ consumer. Its only jobs are to receive deliveries, group them into batches and hand
/// each batch to <see cref="WagerBatchProcessor"/>; storing and acknowledging live there.
///
/// Flow: broker --(prefetch window)--&gt; bounded buffer --&gt; batch (size or time) --&gt; SQL --&gt; multi-ack.
/// The bounded buffer and the prefetch limit give natural back-pressure: if SQL Server is slow, the buffer fills,
/// the broker stops sending, and messages wait safely in the queue instead of in this process' memory.
/// </summary>
public sealed class WagerConsumerWorker : BackgroundService
{
    private readonly RabbitMqConnectionProvider _connectionProvider;
    private readonly RabbitMqOptions _rabbitOptions;
    private readonly WagerConsumerOptions _options;
    private readonly WagerBatchProcessor _processor;
    private readonly ILogger<WagerConsumerWorker> _logger;

    public WagerConsumerWorker(
        RabbitMqConnectionProvider connectionProvider,
        IOptions<RabbitMqOptions> rabbitOptions,
        IOptions<WagerConsumerOptions> options,
        WagerBatchProcessor processor,
        ILogger<WagerConsumerWorker> logger)
    {
        _connectionProvider = connectionProvider;
        _rabbitOptions = rabbitOptions.Value;
        _options = options.Value;
        _processor = processor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Broker not reachable yet, channel closed, ...: unacknowledged messages return to the queue,
                // so starting a fresh session loses nothing.
                _logger.LogError(
                    ex, "Consumer session failed; reconnecting in {Delay} ms", _options.ReconnectDelayMilliseconds);

                try
                {
                    await Task.Delay(_options.ReconnectDelayMilliseconds, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("Wager consumer stopped");
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await RabbitMqTopology.DeclareAsync(channel, _rabbitOptions, cancellationToken).ConfigureAwait(false);

        var prefetch = (ushort)Math.Max(_options.PrefetchCount, _options.BatchSize);
        await channel.BasicQosAsync(0, prefetch, false, cancellationToken).ConfigureAwait(false);

        // Capacity equals the prefetch window: the broker never sends more than this, so the writer never blocks.
        var buffer = Channel.CreateBounded<WagerDelivery>(new BoundedChannelOptions(prefetch)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            // The body buffer is recycled once this handler returns, so it must be copied.
            await buffer.Writer
                .WriteAsync(new WagerDelivery(delivery.DeliveryTag, delivery.Body.ToArray()), cancellationToken)
                .ConfigureAwait(false);
        };

        await channel.BasicConsumeAsync(_rabbitOptions.QueueName, false, consumer, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Consuming '{Queue}' (batch size {BatchSize}, max wait {MaxWait} ms, prefetch {Prefetch})",
            _rabbitOptions.QueueName, _options.BatchSize, _options.BatchMaxWaitMilliseconds, prefetch);

        var acknowledger = new RabbitMqMessageAcknowledger(channel);
        var maxWait = TimeSpan.FromMilliseconds(_options.BatchMaxWaitMilliseconds);

        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = await ChannelBatchReader
                .ReadBatchAsync(buffer.Reader, _options.BatchSize, maxWait, cancellationToken)
                .ConfigureAwait(false);

            if (batch.Count == 0)
            {
                continue;
            }

            await _processor.ProcessAsync(batch, acknowledger, cancellationToken).ConfigureAwait(false);

            if (channel.IsClosed)
            {
                // Tags of a closed channel are meaningless: leave, and let a new session take over.
                throw new InvalidOperationException("The RabbitMQ channel was closed.");
            }
        }
    }
}
