using System.ComponentModel.DataAnnotations;

namespace OT.Assessment.App.Contracts;

/// <summary>
/// The wager payload accepted by <c>POST api/player/casinowager</c>.
/// Property names bind case-insensitively, so both <c>username</c> and <c>Username</c> are accepted.
/// </summary>
public sealed class CasinoWagerRequest : IValidatableObject
{
    // Largest magnitude that fits the decimal(19,4) database column.
    private const decimal MaxAmount = 999_999_999_999_999m;

    public Guid WagerId { get; init; }

    [StringLength(100)]
    public string? Theme { get; init; }

    [Required, StringLength(200)]
    public string Provider { get; init; } = string.Empty;

    [Required, StringLength(200)]
    public string GameName { get; init; } = string.Empty;

    public Guid TransactionId { get; init; }

    // Accepted for contract compatibility; not needed by any requirement, so not forwarded or stored.
    public Guid? BrandId { get; init; }

    public Guid AccountId { get; init; }

    [Required, StringLength(100)]
    public string Username { get; init; } = string.Empty;

    public Guid? ExternalReferenceId { get; init; }

    public Guid? TransactionTypeId { get; init; }

    public decimal Amount { get; init; }

    public DateTime CreatedDateTime { get; init; }

    [Range(0, int.MaxValue)]
    public int NumberOfBets { get; init; }

    [StringLength(3)]
    public string? CountryCode { get; init; }

    public string? SessionData { get; init; }

    [Range(0, long.MaxValue)]
    public long Duration { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (WagerId == Guid.Empty)
        {
            yield return new ValidationResult("WagerId must be a non-empty GUID.", [nameof(WagerId)]);
        }

        if (AccountId == Guid.Empty)
        {
            yield return new ValidationResult("AccountId must be a non-empty GUID.", [nameof(AccountId)]);
        }

        if (TransactionId == Guid.Empty)
        {
            yield return new ValidationResult("TransactionId must be a non-empty GUID.", [nameof(TransactionId)]);
        }

        if (CreatedDateTime == default)
        {
            yield return new ValidationResult("CreatedDateTime is required.", [nameof(CreatedDateTime)]);
        }

        if (Math.Abs(Amount) > MaxAmount)
        {
            yield return new ValidationResult("Amount is out of range.", [nameof(Amount)]);
        }
    }
}
