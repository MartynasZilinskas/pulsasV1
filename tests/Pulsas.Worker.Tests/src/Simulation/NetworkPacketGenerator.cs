using System.Collections.Immutable;
using System.Security.Cryptography;
using Funcky.Monads;
using NodaTime;
using Pulsas.MeshCore.Cryptography;
using Pulsas.MeshCore.Nodes;
using Pulsas.MeshCore.Packets;
using Pulsas.MeshCore.Payloads.Adverts;

namespace Pulsas.Worker.Tests.Simulation;

public class NetworkPacketGenerator : IDisposable
{
    private readonly Dictionary<string, NetworkNode> nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AdvertSigningKey> keys = new(StringComparer.Ordinal);
    private bool disposed;

    private NetworkPacketGenerator() { }

    public ImmutableArray<NetworkObserver> Observers { get; private set; } = [];
    public ImmutableArray<NetworkTraceEntry> LastTrace { get; private set; } = [];

    public static NetworkPacketGenerator Load(string path) => Load(NetworkInventory.Load(path));

    public static NetworkPacketGenerator Load(NetworkInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (inventory.Nodes.IsDefault)
        {
            throw new ArgumentException("Inventory nodes must be initialized.", nameof(inventory));
        }

        var generator = new NetworkPacketGenerator();
        try
        {
            var publicKeys = new HashSet<string>(StringComparer.Ordinal);
            var observers = ImmutableArray.CreateBuilder<NetworkObserver>();
            foreach (var node in inventory.Nodes)
            {
                ArgumentNullException.ThrowIfNull(node);
                ArgumentException.ThrowIfNullOrWhiteSpace(node.Id);
                ArgumentException.ThrowIfNullOrWhiteSpace(node.Name);
                if (!Enum.IsDefined(node.Role))
                {
                    throw new ArgumentException(
                        $"Invalid advertised role for '{node.Id}'.",
                        nameof(inventory)
                    );
                }

                if (!generator.nodes.TryAdd(node.Id, node))
                {
                    throw new ArgumentException(
                        $"Duplicate node ID '{node.Id}'.",
                        nameof(inventory)
                    );
                }

                _ = EncodeAppData(node).Match(left: error => throw error, right: bytes => bytes);
                if (
                    node.LatitudeMicrodegrees is < -90_000_000 or > 90_000_000
                    || node.LongitudeMicrodegrees is < -180_000_000 or > 180_000_000
                )
                {
                    throw new ArgumentException(
                        $"Invalid coordinates for '{node.Id}'.",
                        nameof(inventory)
                    );
                }

                var seed = Convert.FromHexString(node.SigningSeedHex);
                AdvertSigningKey key;
                try
                {
                    key = AdvertSigningKey.ImportSeed(seed);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(seed);
                }

                generator.keys.Add(node.Id, key);
                if (!publicKeys.Add(Convert.ToHexString(key.PublicKey.AsSpan())))
                {
                    throw new ArgumentException(
                        "Each simulated node requires a distinct signing identity.",
                        nameof(inventory)
                    );
                }

                if (node.Observer is { } observer)
                {
                    observers.Add(new(node.Id, key.PublicKey, node.Name, observer));
                }
            }

            foreach (var node in inventory.Nodes)
            {
                var endpoints = new HashSet<string>(StringComparer.Ordinal);
                foreach (var link in Links(node))
                {
                    if (
                        !generator.nodes.ContainsKey(link.NodeId)
                        || link.NodeId == node.Id
                        || link.DelayMilliseconds < 0
                        || !endpoints.Add(link.NodeId)
                    )
                    {
                        throw new ArgumentException(
                            $"Invalid or duplicate outgoing link from '{node.Id}' to '{link.NodeId}'.",
                            nameof(inventory)
                        );
                    }
                }
            }

            generator.Observers = observers.ToImmutable();
            return generator;
        }
        catch
        {
            generator.Dispose();
            throw;
        }
    }

    public ImmutableArray<ObserverHearing> Execute(string nodeId, Instant at, int prefixWidth = 1)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        LastTrace = [];
        if (!nodes.TryGetValue(nodeId, out var source))
        {
            throw new ArgumentException($"Unknown node ID '{nodeId}'.", nameof(nodeId));
        }

        if (prefixWidth is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(prefixWidth));
        }

        if (
            at < Instant.FromUnixTimeSeconds(0)
            || at >= Instant.FromUnixTimeSeconds((long)uint.MaxValue + 1)
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(at),
                "ADVERT timestamp must fit an unsigned Unix-second value."
            );
        }

        var initial = EncodeAppData(source)
            .Select(appData =>
                AdvertCodec.CreateSigned(keys[nodeId], (uint)at.ToUnixTimeSeconds(), appData)
            )
            .SelectMany(advert =>
                AdvertCodec
                    .Encode(advert)
                    .SelectLeft(error => new InvalidOperationException(
                        $"Invalid synthetic ADVERT: {error.Kind}."
                    ))
            )
            .Select(payload =>
                PacketCodec.Create(
                    RouteType.Flood,
                    PacketType.Advert,
                    payload,
                    pathWidth: prefixWidth
                )
            )
            .Match(left: error => throw error, right: packet => packet);
        var hearings = ImmutableArray.CreateBuilder<ObserverHearing>();
        var trace = ImmutableArray.CreateBuilder<NetworkTraceEntry>();
        var queue = new PriorityQueue<Arrival, (Instant Time, long Sequence)>();
        // Execute originates exactly one content identity; node IDs suffice for its per-node seen set.
        var seen = new HashSet<string>(StringComparer.Ordinal) { nodeId };
        long sequence = 0,
            transmission = 0;
        Schedule(nodeId, initial, at);
        while (queue.TryDequeue(out var arrival, out var priority))
        {
            var receiver = nodes[arrival.ReceiverId];
            var raw = ImmutableArray.CreateRange(PacketCodec.Encode(arrival.Packet));
            int? hearingIndex = null;
            if (receiver.Observer is not null)
            {
                hearingIndex = hearings.Count;
                hearings.Add(new(receiver.Id, keys[receiver.Id].PublicKey, raw, priority.Time));
            }

            string? drop = null;
            if (receiver.Role == NodeRole.Repeater && seen.Add(receiver.Id))
            {
                var packet = arrival.Packet;
                if (
                    packet.PathCount == 63
                    || packet.Path.Length + prefixWidth > PacketCodec.MaximumPathLength
                    || raw.Length + prefixWidth > PacketCodec.MaximumPacketLength
                )
                {
                    drop = "path/frame capacity";
                }
                else
                {
                    var path = packet.Path.AddRange(keys[receiver.Id].PublicKey.Take(prefixWidth));
                    Schedule(
                        receiver.Id,
                        PacketCodec.Create(
                            RouteType.Flood,
                            PacketType.Advert,
                            packet.Payload.AsSpan(),
                            path.AsSpan(),
                            prefixWidth
                        ),
                        priority.Time
                    );
                }
            }

            trace.Add(
                new(
                    arrival.TransmissionId,
                    arrival.TransmitterId,
                    receiver.Id,
                    raw,
                    priority.Time,
                    hearingIndex,
                    drop
                )
            );
        }

        LastTrace = trace.ToImmutable();
        return hearings.ToImmutable();

        void Schedule(string transmitterId, MeshCorePacket packet, Instant time)
        {
            var id = transmission++;
            foreach (var link in Links(nodes[transmitterId]))
            {
                queue.Enqueue(
                    new(id, transmitterId, link.NodeId, packet),
                    (time + Duration.FromMilliseconds(link.DelayMilliseconds), sequence++)
                );
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        foreach (var key in keys.Values)
        {
            key.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private static ImmutableArray<NetworkNeighbour> Links(NetworkNode node) =>
        node.Neighbours.IsDefault ? [] : node.Neighbours;

    private static Either<InvalidOperationException, byte[]> EncodeAppData(NetworkNode node) =>
        AdvertAppDataCodec
            .Encode(
                new(
                    node.Role,
                    node.LatitudeMicrodegrees,
                    node.LongitudeMicrodegrees,
                    Name: node.Name
                )
            )
            .SelectLeft(error => new InvalidOperationException(
                $"Invalid synthetic ADVERT app data: {error.Kind}."
            ));

    private record Arrival(
        long TransmissionId,
        string TransmitterId,
        string ReceiverId,
        MeshCorePacket Packet
    );
}
