namespace OT.Assessment.Consumer;

/// <summary>Settles deliveries with the broker. Abstracted so batch processing can be tested without RabbitMQ.</summary>
public interface IMessageAcknowledger
{
    /// <summary>Acknowledges the delivery with this tag and every earlier unsettled one.</summary>
    ValueTask AcknowledgeUpToAsync(ulong deliveryTag, CancellationToken cancellationToken);

    /// <summary>Rejects one delivery without requeueing it, which routes it to the dead-letter queue.</summary>
    ValueTask RejectAsync(ulong deliveryTag, CancellationToken cancellationToken);
}
