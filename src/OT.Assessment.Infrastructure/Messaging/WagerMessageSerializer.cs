using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using OT.Assessment.Core.Wagers;

namespace OT.Assessment.Infrastructure.Messaging;

/// <summary>The wire format of wager messages. Shared by the publisher and the consumer so they can never drift apart.</summary>
public static class WagerMessageSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static byte[] Serialize(CasinoWagerMessage message) =>
        JsonSerializer.SerializeToUtf8Bytes(message, Options);

    /// <summary>
    /// Reads a message body. Returns <c>false</c> for anything that is not a usable wager
    /// (malformed JSON or missing mandatory values), so the caller can dead-letter it.
    /// </summary>
    public static bool TryDeserialize(ReadOnlySpan<byte> body, [NotNullWhen(true)] out CasinoWagerMessage? message)
    {
        try
        {
            message = JsonSerializer.Deserialize<CasinoWagerMessage>(body, Options);
        }
        catch (JsonException)
        {
            message = null;
            return false;
        }

        if (message is null || !HasMandatoryValues(message))
        {
            message = null;
            return false;
        }

        return true;
    }

    private static bool HasMandatoryValues(CasinoWagerMessage message) =>
        message.WagerId != Guid.Empty
        && message.AccountId != Guid.Empty
        && !string.IsNullOrWhiteSpace(message.Username)
        && !string.IsNullOrWhiteSpace(message.Provider)
        && !string.IsNullOrWhiteSpace(message.GameName);
}
