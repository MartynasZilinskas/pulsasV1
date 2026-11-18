using System.Collections.Immutable;
using System.Text.Json;
using NodaTime.Text;
using static Pulsas.Worker.ApiService.Delivery.IReportReceiver;

namespace Pulsas.Worker.Tests.Simulation;

public class NetworkReportAdapter
{
    private readonly Dictionary<string, NetworkObserver> observers;

    public NetworkReportAdapter(IEnumerable<NetworkObserver> observers)
    {
        this.observers = observers.ToDictionary(observer => observer.Id, StringComparer.Ordinal);
        foreach (var observer in this.observers.Values)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(observer.Configuration.Broker);
            ArgumentException.ThrowIfNullOrWhiteSpace(observer.Configuration.Region);
            ArgumentException.ThrowIfNullOrWhiteSpace(observer.Configuration.CountryCode);
            ArgumentException.ThrowIfNullOrWhiteSpace(observer.Configuration.Model);
            ArgumentException.ThrowIfNullOrWhiteSpace(observer.Configuration.FirmwareVersion);
        }
    }

    public ReceiveReportInput ToInput(
        ObserverHearing hearing,
        DateTimeOffset arrivedAt,
        Guid receptionId
    )
    {
        var observer = observers[hearing.ObserverId];
        if (!observer.PublicKey.AsSpan().SequenceEqual(hearing.ObserverPublicKey.AsSpan()))
        {
            throw new InvalidDataException(
                "Hearing identity does not match the configured observer."
            );
        }
        var key = Convert.ToHexStringLower(observer.PublicKey.AsSpan());
        var config = observer.Configuration;
        var report = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                origin = observer.Name,
                origin_id = key,
                type = "PACKET",
                direction = "rx",
                timestamp = InstantPattern.ExtendedIso.Format(hearing.HeardAt),
                raw = Convert.ToHexStringLower(hearing.Raw.AsSpan()),
            }
        );
        return new(
            receptionId,
            ImmutableArray.CreateRange(report),
            $"meshcore/{config.Region}/{key}/packets",
            "ws://fixture.invalid/mqtt",
            arrivedAt
        );
    }
}
