namespace OT.Assessment.Consumer;

/// <summary>A raw message received from the broker, waiting to be processed.</summary>
/// <param name="DeliveryTag">The broker's handle for this delivery, needed to acknowledge or reject it.</param>
/// <param name="Body">A copy of the message body (the broker's buffer is recycled after the handler returns).</param>
public readonly record struct WagerDelivery(ulong DeliveryTag, byte[] Body);
