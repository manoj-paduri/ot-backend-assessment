namespace OT.Assessment.Core.Wagers;

/// <summary>Raised when a message could not be handed over to the message bus.</summary>
public sealed class MessagePublishException : Exception
{
    public MessagePublishException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
