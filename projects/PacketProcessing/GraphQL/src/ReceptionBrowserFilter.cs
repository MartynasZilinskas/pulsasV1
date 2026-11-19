using System.Collections.Immutable;
using System.Text.Json;
using NodaTime;
using Pulsas.MeshCore.Packets;
using static Pulsas.PacketProcessing.Contracts.Paths.IReceptionBrowserManager;
using static Pulsas.PacketProcessing.GraphQL.ReceptionBrowserCursor;

namespace Pulsas.PacketProcessing.GraphQL;

public record MeshPacketReceptionFilterInput(
    [property: GraphQLType(typeof(ListType<NonNullType<IdType>>))]
        IReadOnlyList<string>? ObserverIds,
    IReadOnlyList<string>? RegionCodes,
    IReadOnlyList<LegacyPacketType>? PacketTypes,
    Instant? ArrivedFrom,
    Instant? ArrivedTo
);

public record ObserverFilterInput(string? Name, bool? Online);

internal record ReceptionBrowserFilter(
    ImmutableArray<Guid> Observers,
    ImmutableArray<string> Regions,
    ImmutableArray<PacketType> Types,
    string? From,
    string? To
)
{
    internal static ReceptionBrowserFilter FromInput(MeshPacketReceptionFilterInput? input)
    {
        if (input is { ArrivedFrom: { } from, ArrivedTo: { } to } && from >= to)
        {
            throw Error("INVALID_INPUT", "The arrival range must be increasing.");
        }

        // Keep cursor payloads bounded even with repeated or multibyte filter values.
        if (
            input?.ObserverIds?.Count > 100
            || input?.RegionCodes?.Count > 100
            || input?.PacketTypes?.Count > 100
            || input?.RegionCodes is { } regions
                && (
                    regions.Any(string.IsNullOrEmpty)
                    || JsonSerializer.Serialize(regions).Length > 1400
                )
        )
        {
            throw Error("INVALID_INPUT", "The reception filter is invalid or too large.");
        }

        return new(
            [
                .. (input?.ObserverIds ?? [])
                    .Select(value =>
                        Guid.TryParse(value, out var id)
                            ? id
                            : throw Error("INVALID_INPUT", "Expected an observer UUID.")
                    )
                    .Distinct()
                    .Order(),
            ],
            [
                .. (input?.RegionCodes ?? [])
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal),
            ],
            [.. (input?.PacketTypes ?? []).Select(type => (PacketType)type).Distinct().Order()],
            input?.ArrivedFrom is { } lower ? Format(lower) : null,
            input?.ArrivedTo is { } upper ? Format(upper) : null
        );
    }

    internal string EncodeScope() => JsonSerializer.Serialize(this);

    internal ReceptionFilter ToFilter() =>
        new(
            Observers,
            Regions,
            Types,
            From is null ? null : Parse(From),
            To is null ? null : Parse(To)
        );
}
