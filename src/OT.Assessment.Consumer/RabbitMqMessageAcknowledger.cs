using RabbitMQ.Client;

namespace OT.Assessment.Consumer;

public sealed class RabbitMqMessageAcknowledger : IMessageAcknowledger
{
    private readonly IChannel _channel;

    public RabbitMqMessageAcknowledger(IChannel channel)
    {
        _channel = channel;
    }

    public ValueTask AcknowledgeUpToAsync(ulong deliveryTag, CancellationToken cancellationToken) =>
        _channel.BasicAckAsync(deliveryTag, true, cancellationToken);

    public ValueTask RejectAsync(ulong deliveryTag, CancellationToken cancellationToken) =>
        _channel.BasicNackAsync(deliveryTag, false, false, cancellationToken);
}
