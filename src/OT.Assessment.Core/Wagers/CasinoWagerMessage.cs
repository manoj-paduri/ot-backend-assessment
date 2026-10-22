namespace OT.Assessment.Core.Wagers;

/// <summary>
/// A casino wager event as it travels over the message bus.
/// It carries only the fields that are persisted; the raw HTTP payload has a few more
/// (brand, external reference, transaction type, session data) that no requirement needs.
/// </summary>
/// <param name="CreatedDateTimeUtc">When the wager was created, always expressed in UTC.</param>
public sealed record CasinoWagerMessage(
    Guid WagerId,
    Guid AccountId,
    string Username,
    string Provider,
    string GameName,
    string? Theme,
    Guid TransactionId,
    string? CountryCode,
    decimal Amount,
    int NumberOfBets,
    long Duration,
    DateTime CreatedDateTimeUtc);
