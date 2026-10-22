namespace OT.Assessment.Core.Wagers;

/// <summary>Publishes wager events to the message bus.</summary>
public interface IWagerPublisher
{
    /// <summary>
    /// Publishes the wager and completes only once the broker has accepted it.
    /// </summary>
    /// <exception cref="MessagePublishException">The broker could not accept the message.</exception>
    Task PublishAsync(CasinoWagerMessage message, CancellationToken cancellationToken);
}
