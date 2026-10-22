namespace OT.Assessment.Core.Players;

/// <summary>One row of a player's wager history.</summary>
public sealed record PlayerWagerDto(
    Guid WagerId,
    string Game,
    string Provider,
    decimal Amount,
    DateTime CreatedDate);
