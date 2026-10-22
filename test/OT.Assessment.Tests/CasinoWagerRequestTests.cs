using System.ComponentModel.DataAnnotations;
using OT.Assessment.App.Contracts;

namespace OT.Assessment.Tests;

public class CasinoWagerRequestTests
{
    private static CasinoWagerRequest ValidRequest() => new()
    {
        WagerId = Guid.NewGuid(),
        AccountId = Guid.NewGuid(),
        TransactionId = Guid.NewGuid(),
        Username = "Jay.Bernhard67",
        Provider = "Ergonomic Soft Fish",
        GameName = "Ergonomic Granite Cheese",
        Theme = "adventure",
        Amount = 38273.974454660885m,
        CreatedDateTime = new DateTime(2024, 5, 4, 0, 25, 5, DateTimeKind.Utc),
        NumberOfBets = 3,
        CountryCode = "bs",
        Duration = 1_827_254
    };

    private static List<ValidationResult> Validate(CasinoWagerRequest request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void A_complete_request_is_valid()
    {
        Assert.Empty(Validate(ValidRequest()));
    }

    [Fact]
    public void An_empty_wager_id_is_invalid()
    {
        var request = new CasinoWagerRequest
        {
            WagerId = Guid.Empty,
            AccountId = Guid.NewGuid(),
            TransactionId = Guid.NewGuid(),
            Username = "player",
            Provider = "Provider",
            GameName = "Game",
            CreatedDateTime = DateTime.UtcNow
        };

        Assert.NotEmpty(Validate(request));
    }

    [Fact]
    public void Mapping_converts_local_time_to_utc_and_normalises_text()
    {
        var local = DateTimeOffset.Parse("2024-05-04T02:25:05+02:00").LocalDateTime;
        var request = new CasinoWagerRequest
        {
            WagerId = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            TransactionId = Guid.NewGuid(),
            Username = "  player  ",
            Provider = "Provider",
            GameName = "Game",
            CountryCode = " bs ",
            CreatedDateTime = local
        };

        var message = request.ToMessage();

        Assert.Equal(new DateTime(2024, 5, 4, 0, 25, 5, DateTimeKind.Utc), message.CreatedDateTimeUtc);
        Assert.Equal(DateTimeKind.Utc, message.CreatedDateTimeUtc.Kind);
        Assert.Equal("player", message.Username);
        Assert.Equal("BS", message.CountryCode);
    }
}
