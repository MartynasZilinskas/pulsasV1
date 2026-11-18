using System.Collections.Immutable;
using NodaTime;
using Pulsas.MeshCore.Nodes;
using Pulsas.MeshCore.Packets;
using Pulsas.MeshCore.Payloads.Adverts;
using Pulsas.Worker.Tests.Simulation;
using Xunit;

namespace Pulsas.Worker.Tests;

public class NetworkPacketGeneratorTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Branching_fixture_preserves_signed_content_paths_and_deterministic_delivery(
        int width
    )
    {
        using var generator = NetworkPacketGenerator.Load(Fixture("branching.json"));
        var at = Instant.FromUtc(2026, 11, 1, 12, 0);
        var first = generator.Execute("client", at, width);
        var trace = generator.LastTrace;
        Assert.Equal(new[] { "O2", "O4", "O5" }, first.Select(h => h.ObserverId));
        var packets = first
            .Select(h =>
                PacketCodec
                    .Decode(h.Raw.AsSpan())
                    .Match(
                        left: error => throw new InvalidDataException(error.ToString()),
                        right: packet => packet
                    )
            )
            .ToArray();
        Assert.Equal(new[] { 2, 4, 4 }, packets.Select(p => p.PathCount));
        Assert.All(packets, p =>
        {
            Assert.Equal(width, p.PathWidth);
            Assert.Equal(packets[0].Payload.ToArray(), p.Payload.ToArray());
            Assert.True(
                AdvertCodec
                    .Decode(p.Payload.AsSpan())
                    .Match(left: _ => false, right: AdvertCodec.Verify)
            );
        });
        var adapter = new NetworkReportAdapter(generator.Observers);
        var arrival = at.Plus(Duration.FromHours(1)).ToDateTimeOffset();
        var input = adapter.ToInput(first[0], arrival, Guid.NewGuid());
        Assert.Equal(arrival, input.ArrivedAt);
        Assert.StartsWith("meshcore/VNO/", input.Topic);
        Assert.Equal("ws://fixture.invalid/mqtt", input.BrokerEndpoint);
        var second = generator.Execute("client", at, width);
        Assert.Equal(
            first.Select(h => Convert.ToHexString(h.Raw.AsSpan())),
            second.Select(h => Convert.ToHexString(h.Raw.AsSpan()))
        );
        Assert.Equal(
            trace.Select(t => (t.TransmitterId, t.ReceiverId, t.HeardAt)),
            generator.LastTrace.Select(t => (t.TransmitterId, t.ReceiverId, t.HeardAt))
        );
    }

    [Fact]
    public void Prefix_collision_does_not_suppress_distinct_forwarders_or_duplicate_hearings()
    {
        using var generator = NetworkPacketGenerator.Load(Fixture("collision.json"));
        var hearings = generator.Execute("client", Instant.FromUtc(2026, 11, 1, 12, 0));
        Assert.Equal(2, hearings.Length);
        Assert.Equal(hearings[0].Raw.ToArray(), hearings[1].Raw.ToArray());
        Assert.Contains(generator.LastTrace, t =>
            t.TransmitterId == "repeater-a" && t.ReceiverId == "observer"
        );
        Assert.Contains(generator.LastTrace, t =>
            t.TransmitterId == "repeater-b" && t.ReceiverId == "observer"
        );
        Assert.True(hearings[0].HeardAt < hearings[1].HeardAt);
    }

    [Theory]
    [InlineData(1, 63)]
    [InlineData(2, 32)]
    [InlineData(3, 21)]
    public void Capacity_stops_forwarding_explicitly_without_truncating_history(
        int width,
        int maximum
    )
    {
        var template = NetworkInventory.Load(Fixture("branching.json")).Nodes[1];
        var nodes = Enumerable
            .Range(0, 68)
            .Select(i =>
                template with
                {
                    Id = $"n{i}",
                    SigningSeedHex = i.ToString("x64"),
                    Neighbours = i == 67 ? [] : [new NetworkNeighbour($"n{i + 1}")],
                    Observer = new("mqtt://fixture.invalid", "TST", "LT", "fixture", "fixture"),
                }
            )
            .ToImmutableArray();
        using var generator = NetworkPacketGenerator.Load(new NetworkInventory(nodes));
        var hearings = generator.Execute("n0", Instant.FromUtc(2026, 11, 1, 12, 0), width);
        Assert.Equal(maximum + 1, hearings.Length);
        var final = PacketCodec
            .Decode(hearings[^1].Raw.AsSpan())
            .Match(
                left: error => throw new InvalidDataException(error.ToString()),
                right: packet => packet
            );
        Assert.Equal(maximum, final.PathCount);
        Assert.Equal(maximum * width, final.Path.Length);
        Assert.Equal("path/frame capacity", generator.LastTrace[^1].ForwardingDrop);
    }

    [Fact]
    public void Zero_delay_cycle_records_receiving_repeater_before_forwarding_and_then_stops()
    {
        var template = NetworkInventory.Load(Fixture("branching.json")).Nodes[1];
        var config = new ObserverConfiguration(
            "mqtt://fixture.invalid",
            "TST",
            "LT",
            "fixture",
            "fixture"
        );
        using var generator = NetworkPacketGenerator.Load(
            new NetworkInventory([
                template with
                {
                    Id = "source",
                    Role = NodeRole.Client,
                    SigningSeedHex = 21.ToString("x64"),
                    Neighbours = [new("a")],
                },
                template with
                {
                    Id = "a",
                    SigningSeedHex = 22.ToString("x64"),
                    Neighbours = [new("b")],
                    Observer = config,
                },
                template with
                {
                    Id = "b",
                    SigningSeedHex = 23.ToString("x64"),
                    Neighbours = [new("a")],
                    Observer = config,
                },
            ])
        );
        var hearings = generator.Execute("source", Instant.FromUtc(2026, 11, 1, 12, 0));
        Assert.Equal(new[] { "a", "b", "a" }, hearings.Select(h => h.ObserverId));
        Assert.Equal(
            new[] { 0, 1, 2 },
            hearings.Select(h =>
                PacketCodec
                    .Decode(h.Raw.AsSpan())
                    .Match(
                        left: error => throw new InvalidDataException(error.ToString()),
                        right: p => p.PathCount
                    )
            )
        );
        Assert.All(hearings, h => Assert.Equal(hearings[0].HeardAt, h.HeardAt));
    }

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "network", name);
}
