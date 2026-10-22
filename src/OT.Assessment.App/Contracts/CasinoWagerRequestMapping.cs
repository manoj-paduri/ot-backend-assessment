using OT.Assessment.Core.Wagers;

namespace OT.Assessment.App.Contracts;

public static class CasinoWagerRequestMapping
{
    /// <summary>Maps the HTTP contract to the bus message: trims text, normalises the country code and converts the time to UTC.</summary>
    public static CasinoWagerMessage ToMessage(this CasinoWagerRequest request) => new(
        request.WagerId,
        request.AccountId,
        request.Username.Trim(),
        request.Provider.Trim(),
        request.GameName.Trim(),
        NullIfBlank(request.Theme),
        request.TransactionId,
        NullIfBlank(request.CountryCode)?.ToUpperInvariant(),
        request.Amount,
        request.NumberOfBets,
        request.Duration,
        request.CreatedDateTime.ToUniversalTime());

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
