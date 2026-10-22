namespace OT.Assessment.Core.Players;

/// <summary>A player together with the total amount they have wagered.</summary>
public sealed record TopSpenderDto(
    Guid AccountId,
    string Username,
    decimal TotalAmountSpend);
