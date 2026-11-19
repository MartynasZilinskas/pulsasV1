using System.Collections.Immutable;
using Funcky.Monads;
using NodaTime;
using Pulsas.MeshCore.Packets;

namespace Pulsas.PacketProcessing.Contracts.Paths;

public interface IMeshTrafficReadManager
{
    Task<Summary> ReadSummaryAsync(SummaryInput input, CancellationToken cancellationToken);

    public record SummaryInput(
        Instant From,
        Instant To,
        ImmutableArray<Guid> ObserverIds,
        ImmutableArray<string> RegionCodes,
        ImmutableArray<PacketType> PacketTypes,
        bool IncludePacketTypeCounts
    );

    public record Summary(
        long ReceptionCount,
        long WirePacketCount,
        long SharedPayloadCount,
        long ActiveObserverCount,
        long ActiveRegionCount,
        SignalSummary Snr,
        SignalSummary Rssi,
        ImmutableArray<PacketTypeCount> PacketTypeCounts,
        ImmutableArray<TagCount> Tags
    );

    public record SignalSummary(long SampleCount, Option<double> Average);

    public record PacketTypeCount(Option<PacketType> PacketType, long Count);

    public record TagCount(string Tag, long SharedPayloadCount, long ReceptionCount);
}
