using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NodaTime;
using NodaTime.Text;

namespace Pulsas.PacketProcessing.GraphQL;

public class ReceptionBrowserCursor
{
    // Keep a signed cursor small enough for the legacy histogram request to also resend its filter.
    private const int MaximumPayloadBytes = 6 * 1024;
    private const int SignatureCharacters = 43;
    private const int MaximumCursorLength = MaximumPayloadBytes * 4 / 3 + 1 + SignatureCharacters;
    private readonly byte[] key = RandomNumberGenerator.GetBytes(32);

    internal record State(
        string Collection,
        string Scope,
        Guid Id,
        string? Time = null,
        int? Interval = null,
        ReceptionBrowserFilter? Filter = null,
        string? RegionCode = null
    );

    internal string Encode(State state) =>
        TryEncode(state)
        ?? throw new InvalidOperationException("Browser cursor exceeds its payload budget.");

    internal string? TryEncode(State state)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(state);
        if (payload.Length > MaximumPayloadBytes)
        {
            return null;
        }
        return Base64Url.EncodeToString(payload)
            + "."
            + Base64Url.EncodeToString(HMACSHA256.HashData(key, payload));
    }

    internal State Decode(string cursor, string collection, string? scope = null)
    {
        if (
            cursor.Length <= MaximumCursorLength
            && cursor.IndexOf('.') is var separator
            && separator > 0
            && separator <= MaximumPayloadBytes * 4 / 3
            && cursor.Length - separator - 1 == SignatureCharacters
        )
        {
            try
            {
                var payload = Base64Url.DecodeFromChars(cursor.AsSpan(0, separator));
                var signature = Base64Url.DecodeFromChars(cursor.AsSpan(separator + 1));
                if (
                    CryptographicOperations.FixedTimeEquals(
                        signature,
                        HMACSHA256.HashData(key, payload)
                    )
                    && JsonSerializer.Deserialize<State>(payload) is { } state
                    && state.Collection == collection
                    && (scope is null || state.Scope == scope)
                )
                {
                    return state;
                }
            }
            catch (Exception error) when (error is FormatException or JsonException)
            {
                throw InvalidCursor();
            }
        }

        throw InvalidCursor();
    }

    internal static string Format(Instant instant) => InstantPattern.ExtendedIso.Format(instant);

    internal static string Fingerprint(string scope) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(scope)));

    internal static Instant Parse(string? text) =>
        text is not null && InstantPattern.ExtendedIso.Parse(text) is { Success: true } parsed
            ? parsed.Value
            : throw InvalidCursor();

    internal static GraphQLException InvalidCursor() =>
        Error("INVALID_CURSOR", "Invalid or expired browser cursor.");

    internal static GraphQLException Error(string code, string message) =>
        new(ErrorBuilder.New().SetCode(code).SetMessage(message).Build());
}
