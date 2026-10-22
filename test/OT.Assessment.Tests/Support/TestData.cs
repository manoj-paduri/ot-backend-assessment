using OT.Assessment.Consumer;
using OT.Assessment.Core.Wagers;
using OT.Assessment.Infrastructure.Messaging;

namespace OT.Assessment.Tests.Support;

internal static class TestData
{
    public static CasinoWagerMessage Wager(string username = "player", Guid? wagerId = null, decimal amount = 100m) =>
        new(
            wagerId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            username,
            "Provider A",
            "Game A",
            "adventure",
            Guid.NewGuid(),
            "ZA",
            amount,
            3,
            1_000,
            new DateTime(2024, 5, 3, 0, 25, 5, DateTimeKind.Utc));

    public static WagerDelivery Delivery(ulong tag, CasinoWagerMessage message) =>
        new(tag, WagerMessageSerializer.Serialize(message));
}
