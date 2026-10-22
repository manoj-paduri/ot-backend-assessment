using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OT.Assessment.Core.Wagers;
using RabbitMQ.Client;

namespace OT.Assessment.Infrastructure.Messaging;

/// <summary>
/// Publishes wagers to RabbitMQ with publisher confirms: <see cref="PublishAsync"/> only returns once the
/// broker has taken responsibility for the (persistent) message, so an HTTP 202 means the wager is safe.
/// A small pool of channels lets many requests publish in parallel without sharing a channel.
/// </summary>
public sealed class RabbitMqWagerPublisher : IWagerPublisher, IAsyncDisposable
{
    private static readonly CreateChannelOptions ConfirmingChannelOptions = new(
        publisherConfirmationsEnabled: true,
        publisherConfirmationTrackingEnabled: true);

    private readonly RabbitMqConnectionProvider _connectionProvider;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqWagerPublisher> _logger;

    private readonly ConcurrentBag<IChannel> _idleChannels = new();
    private readonly SemaphoreSlim _concurrencyLimit;
    private readonly SemaphoreSlim _topologyGate = new(1, 1);
    private volatile bool _topologyDeclared;

    public RabbitMqWagerPublisher(
        RabbitMqConnectionProvider connectionProvider,
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqWagerPublisher> logger)
    {
        _connectionProvider = connectionProvider;
        _options = options.Value;
        _logger = logger;
        _concurrencyLimit = new SemaphoreSlim(Math.Max(1, _options.PublisherChannelPoolSize));
    }

    public async Task PublishAsync(CasinoWagerMessage message, CancellationToken cancellationToken)
    {
        var body = WagerMessageSerializer.Serialize(message);
        var properties = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = message.WagerId.ToString("D")
        };

        await _concurrencyLimit.WaitAsync(cancellationToken).ConfigureAwait(false);
        IChannel? channel = null;
        try
        {
            channel = await RentChannelAsync(cancellationToken).ConfigureAwait(false);

            // Default exchange: the routing key is the queue name.
            await channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: _options.QueueName,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            _idleChannels.Add(channel);
            channel = null; // returned to the pool, must not be disposed below
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Publishing wager {WagerId} to RabbitMQ failed", message.WagerId);
            throw new MessagePublishException("The wager could not be published to the message bus.", ex);
        }
        finally
        {
            if (channel is not null)
            {
                // A channel that failed mid-publish may be in an undefined state: do not reuse it.
                await DisposeQuietlyAsync(channel).ConfigureAwait(false);
            }

            _concurrencyLimit.Release();
        }
    }

    private async Task<IChannel> RentChannelAsync(CancellationToken cancellationToken)
    {
        while (_idleChannels.TryTake(out var idle))
        {
            if (idle.IsOpen)
            {
                return idle;
            }

            await DisposeQuietlyAsync(idle).ConfigureAwait(false);
        }

        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        await EnsureTopologyAsync(connection, cancellationToken).ConfigureAwait(false);
        return await connection.CreateChannelAsync(ConfirmingChannelOptions, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureTopologyAsync(IConnection connection, CancellationToken cancellationToken)
    {
        if (_topologyDeclared)
        {
            return;
        }

        await _topologyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_topologyDeclared)
            {
                return;
            }

            await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            await RabbitMqTopology.DeclareAsync(channel, _options, cancellationToken).ConfigureAwait(false);
            _topologyDeclared = true;
        }
        finally
        {
            _topologyGate.Release();
        }
    }

    private static async Task DisposeQuietlyAsync(IChannel channel)
    {
        try
        {
            await channel.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // The channel is being discarded anyway.
        }
    }

    public async ValueTask DisposeAsync()
    {
        while (_idleChannels.TryTake(out var channel))
        {
            await DisposeQuietlyAsync(channel).ConfigureAwait(false);
        }

        _concurrencyLimit.Dispose();
        _topologyGate.Dispose();
    }
}
