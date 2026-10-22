using System.Text;
using OT.Assessment.Infrastructure.Messaging;
using OT.Assessment.Tests.Support;

namespace OT.Assessment.Tests;

public class WagerMessageSerializerTests
{
    [Fact]
    public void A_message_survives_a_round_trip()
    {
        var original = TestData.Wager(amount: 38273.9745m);

        var body = WagerMessageSerializer.Serialize(original);
        var ok = WagerMessageSerializer.TryDeserialize(body, out var restored);

        Assert.True(ok);
        Assert.Equal(original, restored);
    }

    [Fact]
    public void Malformed_json_is_rejected()
    {
        var ok = WagerMessageSerializer.TryDeserialize(Encoding.UTF8.GetBytes("this is not json"), out var message);

        Assert.False(ok);
        Assert.Null(message);
    }

    [Fact]
    public void A_message_without_mandatory_values_is_rejected()
    {
        var json = $$"""{"wagerId":"{{Guid.NewGuid()}}","accountId":"{{Guid.NewGuid()}}"}""";

        var ok = WagerMessageSerializer.TryDeserialize(Encoding.UTF8.GetBytes(json), out _);

        Assert.False(ok);
    }
}
