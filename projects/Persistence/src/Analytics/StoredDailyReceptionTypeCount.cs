using NodaTime;
using Pulsas.MeshCore.Packets;

namespace Pulsas.Persistence.Analytics;

public class StoredDailyReceptionTypeCount
{
    public required Guid Id { get; set; }
    public Instant DayStart { get; set; }
    public Guid ObserverId { get; set; }
    public required string RegionCode { get; set; }
    public PacketType PacketType { get; set; }
    public long ReceptionCount { get; set; }
}
