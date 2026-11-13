using NodaTime;

namespace Pulsas.Persistence.Observers;

public class StoredObserver
{
    public Instant? CreatedAt { get; set; }
    public Instant ModifiedAt { get; set; }
    public required Guid Id { get; set; }
    public required byte[] PublicKey { get; set; }
    public string? Name { get; set; }
    public string? FirmwareVersion { get; set; }
    public string? HardwareModel { get; set; }
    public Instant? LastOnlineAt { get; set; }
}
