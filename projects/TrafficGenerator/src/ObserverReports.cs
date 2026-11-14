using System.Collections.Immutable;
using System.Text.Json;
using Pulsas.MeshCore.Cryptography;
using Pulsas.MeshCore.Nodes;
using Pulsas.MeshCore.Packets;
using Pulsas.MeshCore.Payloads.Adverts;

namespace Pulsas.TrafficGenerator;

// MQTT JSON belongs to the generator, not to the wire-format library.
public static class ObserverReports
{
    public static ImmutableArray<ObserverIdentity> CreateObservers(
        int count,
        CancellationToken cancellationToken
    ) =>
        [
            .. Enumerable
                .Range(1, count)
                .Select(index =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var key = AdvertSigningKey.Generate();
                    return new ObserverIdentity(
                        Convert.ToHexStringLower(key.PublicKey.AsSpan()),
                        $"synthetic-observer-{index}"
                    );
                }),
        ];

    public static string CreateAdvert(AdvertSigningKey sender, int round, DateTimeOffset at)
    {
        // Explicit synthetic fixtures inside the four retained Lithuanian regions.
        // These are demo positions, not region boundaries or production constraints.
        var (latitude, longitude) = (round % 4) switch
        {
            1 => (54_736_798, 23_655_660), // KUN
            2 => (55_729_519, 21_619_736), // PLQ
            3 => (55_915_511, 24_325_359), // SQQ
            _ => (55_035_748, 25_429_177), // VNO
        };
        var appData = AdvertAppDataCodec
            .Encode(new(NodeRole.Repeater, latitude, longitude, Name: $"synthetic-{round}"))
            .Match(
                left: error =>
                    throw new InvalidOperationException(
                        $"Invalid synthetic ADVERT app data: {error.Kind}."
                    ),
                right: bytes => bytes
            );
        var advert = AdvertCodec.CreateSigned(
            sender,
            checked((uint)at.ToUnixTimeSeconds()),
            appData
        );
        var payload = AdvertCodec
            .Encode(advert)
            .Match(
                left: error =>
                    throw new InvalidOperationException($"Invalid synthetic ADVERT: {error.Kind}."),
                right: bytes => bytes
            );
        return Convert.ToHexStringLower(
            PacketCodec.Encode(PacketCodec.Create(RouteType.Flood, PacketType.Advert, payload))
        );
    }

    public static byte[] Status(ObserverIdentity observer, string status, DateTimeOffset at) =>
        JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                status,
                timestamp = at,
                origin = observer.Name,
                origin_id = observer.PublicKey,
                model = "synthetic",
                firmware_version = "pulsas-generator-1",
            }
        );

    public static byte[] Packet(ObserverIdentity observer, string raw, DateTimeOffset at) =>
        JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                origin = observer.Name,
                origin_id = observer.PublicKey,
                type = "PACKET",
                direction = "rx",
                timestamp = at,
                raw,
                RSSI = -75,
                SNR = 8.5,
            }
        );
}
