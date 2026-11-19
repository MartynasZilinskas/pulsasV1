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
        ImmutableArray<PacketType> PacketTypes
    );

    public record Summary(
        long ReceptionCount,
        long WirePacketCount,
        long SharedPayloadCount,
        SignalSummary Snr,
        SignalSummary Rssi,
        ImmutableArray<TagCount> Tags
    );

    public record SignalSummary(long SampleCount, Option<double> Average);

    public record TagCount(string Tag, long SharedPayloadCount, long ReceptionCount);
}
