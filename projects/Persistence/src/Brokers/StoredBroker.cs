using NodaTime;

namespace Pulsas.Persistence.Brokers;

public class StoredBroker
{
    public Instant? CreatedAt { get; set; }
    public required Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Endpoint { get; set; }
    public Instant ModifiedAt { get; set; }
    public Instant? LastOnlineAt { get; set; }
    public Instant? RecycledAt { get; set; }
}
