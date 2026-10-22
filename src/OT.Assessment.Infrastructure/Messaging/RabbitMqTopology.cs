using RabbitMQ.Client;

namespace OT.Assessment.Infrastructure.Messaging;

/// <summary>
/// Declares the queues the system needs. Both the API and the consumer call this, so whichever
/// starts first creates the topology and the other finds it already in place (declarations are idempotent
/// as long as the arguments match, which is why they live in one place).
/// </summary>
public static class RabbitMqTopology
{
    public static async Task DeclareAsync(
        IChannel channel, RabbitMqOptions options, CancellationToken cancellationToken)
    {
        // Messages that cannot be processed (poison messages) are rejected without requeue and
        // end up here through the dead-letter exchange, where they can be inspected and replayed.
        await channel.ExchangeDeclareAsync(
            exchange: options.DeadLetterExchangeName,
            type: ExchangeType.Fanout,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await channel.QueueDeclareAsync(
            queue: options.DeadLetterQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await channel.QueueBindAsync(
            options.DeadLetterQueueName,
            options.DeadLetterExchangeName,
            string.Empty,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var wagerQueueArguments = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = options.DeadLetterExchangeName
        };

        await channel.QueueDeclareAsync(
            queue: options.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: wagerQueueArguments,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
