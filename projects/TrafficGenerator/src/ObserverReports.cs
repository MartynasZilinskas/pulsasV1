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
        var appData = AdvertAppDataCodec.Encode(new(NodeRole.Repeater, Name: $"synthetic-{round}"));
        var advert = AdvertCodec.CreateSigned(
            sender,
            checked((uint)at.ToUnixTimeSeconds()),
            appData
        );
        return Convert.ToHexStringLower(
            PacketCodec.Encode(
                PacketCodec.Create(RouteType.Flood, PacketType.Advert, AdvertCodec.Encode(advert))
            )
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
