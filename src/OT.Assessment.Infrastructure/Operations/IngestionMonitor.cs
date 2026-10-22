using System.Data;
using Dapper;
using Microsoft.Extensions.Options;
using OT.Assessment.Core.Operations;
using OT.Assessment.Infrastructure.Messaging;
using OT.Assessment.Infrastructure.Persistence;
using RabbitMQ.Client;

namespace OT.Assessment.Infrastructure.Operations;

/// <summary>
/// Combines the two places a wager can be "in flight": the broker queues and the database.
/// When the queue is empty, the consumer is idle and the stored count equals the number of accepted
/// wagers, nothing has been lost.
/// </summary>
public sealed class IngestionMonitor : IIngestionMonitor
{
    private readonly ISqlConnectionFactory _connectionFactory;
    private readonly RabbitMqConnectionProvider _connectionProvider;
    private readonly RabbitMqOptions _rabbitOptions;
    private readonly int _commandTimeoutSeconds;

    public IngestionMonitor(
        ISqlConnectionFactory connectionFactory,
        RabbitMqConnectionProvider connectionProvider,
        IOptions<RabbitMqOptions> rabbitOptions,
        IOptions<DatabaseOptions> databaseOptions)
    {
        _connectionFactory = connectionFactory;
        _connectionProvider = connectionProvider;
        _rabbitOptions = rabbitOptions.Value;
        _commandTimeoutSeconds = databaseOptions.Value.CommandTimeoutSeconds;
    }

    public async Task<IngestionStats> GetStatsAsync(CancellationToken cancellationToken)
    {
        var database = await ReadDatabaseCountsAsync(cancellationToken).ConfigureAwait(false);
        var queues = await ReadQueueDepthsAsync(cancellationToken).ConfigureAwait(false);

        return new IngestionStats(
            database.StoredWagers,
            database.StoredPlayers,
            queues.Wagers,
            queues.DeadLettered,
            DateTime.UtcNow);
    }

    private async Task<DatabaseCounts> ReadDatabaseCountsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var command = new CommandDefinition(
            "dbo.usp_Ingestion_GetStats",
            commandType: CommandType.StoredProcedure,
            commandTimeout: _commandTimeoutSeconds,
            cancellationToken: cancellationToken);

        return await connection.QuerySingleAsync<DatabaseCounts>(command).ConfigureAwait(false);
    }

    private async Task<QueueDepths> ReadQueueDepthsAsync(CancellationToken cancellationToken)
    {
        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // Declaring first makes the endpoint safe to call before the first wager: a passive declare of a
        // queue that does not exist yet would fail and close the channel.
        await RabbitMqTopology.DeclareAsync(channel, _rabbitOptions, cancellationToken).ConfigureAwait(false);

        var wagers = await channel.QueueDeclarePassiveAsync(_rabbitOptions.QueueName, cancellationToken)
            .ConfigureAwait(false);
        var deadLettered = await channel.QueueDeclarePassiveAsync(_rabbitOptions.DeadLetterQueueName, cancellationToken)
            .ConfigureAwait(false);

        return new QueueDepths(wagers.MessageCount, deadLettered.MessageCount);
    }

    // Row shape for Dapper (column names come from dbo.usp_Ingestion_GetStats).
    internal sealed class DatabaseCounts
    {
        public long StoredWagers { get; set; }
        public long StoredPlayers { get; set; }
    }

    private readonly record struct QueueDepths(uint Wagers, uint DeadLettered);
}
