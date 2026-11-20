using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NodaTime.Text;
using Pulsas.MeshCore.Cryptography;
using Pulsas.MeshCore.Packets;
using Xunit;

namespace Pulsas.Worker.Tests;

public partial class PathAssessmentIntegrationTests
{
    [PgmqFact]
    public async Task Shared_payloads_expose_canonical_packets_and_earliest_retained_representative()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var database = await TestDatabase.CreateAsync(token);
        await SeedRegionAsync(database, token);
        using var observer = AdvertSigningKey.Generate();
        using var advertiser = AdvertSigningKey.Generate();
        var at = DateTimeOffset.UtcNow;
        var initialPacket = SignedAdvertWithFirstHop(advertiser, at, [0x11]);
        var variant = SignedAdvertWithFirstHop(advertiser, at, [0x22, 0x33]);
        var original = await ReceiveAndProcessAsync(database, observer, initialPacket, at, token);
        await ReceiveAndProcessAsync(
            database,
            observer,
            PacketCodec.Encode(
                PacketCodec.Create(RouteType.Direct, PacketType.GroupText, [0x3a, 2, 3])
            ),
            at.AddSeconds(1),
            token
        );

        var reads = new TrafficReadInterceptor();
        await using var gateway = BuildTrafficGateway(database, reads);
        gateway.Urls.Add("http://127.0.0.1:0");
        await gateway.StartAsync(token);
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            BaseAddress = new Uri(gateway.Urls.Single()),
        };

        const string groups = """
            query ($after: Cursor) {
              meshPacketPayloads(first: 1, after: $after) {
                edges { cursor node { id payloadType firstArrivedAt lastArrivedAt packetCount receptionCount
                  firstPacket { id }
                  decodedPayload { __typename ... on DecodedAdvertPayload { publicKey } ... on DecodedGroupTextPayload { channelHash } }
                } }
                pageInfo { hasNextPage }
              }
            }
            """;
        var head = (await ReadAsync(groups, new { })).GetProperty("meshPacketPayloads");
        Assert.True(head.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean());
        var headEdge = Assert.Single(head.GetProperty("edges").EnumerateArray());
        var headCursor = headEdge.GetProperty("cursor").GetString();
        var newestNode = headEdge.GetProperty("node");
        Assert.Equal("GROUP_TEXT", newestNode.GetProperty("payloadType").GetString());
        Assert.Equal(
            "3a",
            newestNode.GetProperty("decodedPayload").GetProperty("channelHash").GetString()
        );
        var before = SingleNode(
            (await ReadAsync(groups, new { after = headCursor })).GetProperty("meshPacketPayloads")
        );
        var groupId = before.GetProperty("id").GetGuid();
        Assert.Equal(1, before.GetProperty("receptionCount").GetInt64());

        var second = await ReceiveAndProcessAsync(
            database,
            observer,
            initialPacket,
            at.AddSeconds(2),
            token
        );
        var third = await ReceiveAndProcessAsync(
            database,
            observer,
            variant,
            at.AddSeconds(2),
            token
        );
        Assert.Equal(
            newestNode.GetProperty("id").GetGuid(),
            SingleNode((await ReadAsync(groups, new { })).GetProperty("meshPacketPayloads"))
                .GetProperty("id")
                .GetGuid()
        );
        var updated = SingleNode(
            (await ReadAsync(groups, new { after = headCursor })).GetProperty("meshPacketPayloads")
        );
        Assert.Equal(groupId, updated.GetProperty("id").GetGuid());
        Assert.Equal(
            before.GetProperty("firstArrivedAt").GetString(),
            updated.GetProperty("firstArrivedAt").GetString()
        );
        Assert.True(
            InstantPattern
                .ExtendedIso.Parse(updated.GetProperty("lastArrivedAt").GetString()!)
                .Value
                > InstantPattern
                    .ExtendedIso.Parse(updated.GetProperty("firstArrivedAt").GetString()!)
                    .Value
        );
        Assert.Equal(2, updated.GetProperty("packetCount").GetInt64());
        Assert.Equal(3, updated.GetProperty("receptionCount").GetInt64());
        Assert.Equal(
            Convert.ToHexStringLower(advertiser.PublicKey.AsSpan()),
            updated.GetProperty("decodedPayload").GetProperty("publicKey").GetString()
        );

        const string details = """
            query ($id: ID!, $packet: Cursor, $reception: Cursor) {
              meshPacketPayloadById(id: $id) {
                __typename
                ... on MeshPacketPayload {
                  firstPacket { id }
                  packets(first: 1, after: $packet) {
                    edges { cursor node { __typename id receptionCount pathHops { prefix } } }
                  }
                  receptions(first: 1, after: $reception) {
                    edges { cursor node { __typename id packetId arrivedAt heardAt observer { id } region { code } } }
                  }
                }
              }
            }
            """;
        var group = (await ReadAsync(details, new { id = groupId })).GetProperty(
            "meshPacketPayloadById"
        );
        var v = group.GetProperty("packets").GetProperty("edges")[0];
        var r = group.GetProperty("receptions").GetProperty("edges")[0];
        var continued = (
            await ReadAsync(
                details,
                new
                {
                    id = groupId,
                    packet = v.GetProperty("cursor").GetString(),
                    reception = r.GetProperty("cursor").GetString(),
                }
            )
        ).GetProperty("meshPacketPayloadById");
        var variants = new[]
        {
            v.GetProperty("node"),
            SingleNode(continued.GetProperty("packets")),
        };
        var common = Assert.Single(variants, node =>
            node.GetProperty("receptionCount").GetInt64() == 2
        );
        var other = Assert.Single(variants, node =>
            node.GetProperty("receptionCount").GetInt64() == 1
        );
        var commonId = common.GetProperty("id").GetGuid();
        var otherId = other.GetProperty("id").GetGuid();
        Assert.All(variants, node =>
            Assert.Equal("MeshPacket", node.GetProperty("__typename").GetString())
        );
        Assert.Equal(commonId, RepresentativeId(updated));
        Assert.Equal(commonId, RepresentativeId(group));

        const string batch = """
            query ($first: ID!, $second: ID!, $missing: ID!) {
              first: meshPacketById(id: $first) { ... on MeshPacket { id } }
              second: meshPacketById(id: $second) { ... on MeshPacket { id } }
              missing: meshPacketById(id: $missing) { __typename }
            }
            """;
        var beforeBatch = reads.Count;
        var batched = await ReadAsync(
            batch,
            new
            {
                first = commonId,
                second = otherId,
                missing = Guid.NewGuid(),
            }
        );
        Assert.Equal(commonId, batched.GetProperty("first").GetProperty("id").GetGuid());
        Assert.Equal(otherId, batched.GetProperty("second").GetProperty("id").GetGuid());
        Assert.Equal(
            "MeshPacketNotFoundError",
            batched.GetProperty("missing").GetProperty("__typename").GetString()
        );
        Assert.Equal(beforeBatch + 1, reads.Count);

        const string statsOnly = """
            query ($id: ID!) {
              meshPacketById(id: $id) {
                ... on MeshPacket { receptionCount firstArrivedAt lastArrivedAt }
              }
            }
            """;
        var beforeStats = reads.Count;
        var stats = (await ReadAsync(statsOnly, new { id = otherId })).GetProperty(
            "meshPacketById"
        );
        Assert.Equal(beforeStats + 2, reads.Count);
        Assert.Equal(1, stats.GetProperty("receptionCount").GetInt64());
        Assert.Equal(
            at.AddSeconds(2).ToUnixTimeMilliseconds(),
            stats.GetProperty("firstArrivedAt").GetDateTimeOffset().ToUnixTimeMilliseconds()
        );
        Assert.Equal(
            stats.GetProperty("firstArrivedAt").GetDateTimeOffset(),
            stats.GetProperty("lastArrivedAt").GetDateTimeOffset()
        );

        const string selectedPacket = """
            query ($id: ID!, $first: Int!, $after: Cursor) {
              meshPacketById(id: $id) {
                __typename
                ... on MeshPacket {
                  id receptionCount firstArrivedAt lastArrivedAt pathHops { prefix }
                  receptions(first: $first, after: $after) { edges { node { id packetId } } }
                }
                ... on MeshPacketNotFoundError { message }
              }
            }
            """;
        var selectedNode = (
            await ReadAsync(selectedPacket, new { id = otherId, first = 10 })
        ).GetProperty("meshPacketById");
        Assert.Equal("MeshPacket", selectedNode.GetProperty("__typename").GetString());
        Assert.Equal(otherId, selectedNode.GetProperty("id").GetGuid());
        Assert.Equal(1, selectedNode.GetProperty("receptionCount").GetInt64());
        Assert.Equal(
            third,
            SingleNode(selectedNode.GetProperty("receptions")).GetProperty("id").GetGuid()
        );
        var unknown = (
            await ReadAsync(selectedPacket, new { id = Guid.NewGuid(), first = 1 })
        ).GetProperty("meshPacketById");
        Assert.Equal("MeshPacketNotFoundError", unknown.GetProperty("__typename").GetString());
        var invalid = await PostAsync(selectedPacket, new { id = "invalid", first = 1 });
        Assert.Equal(
            "INVALID_INPUT",
            invalid
                .GetProperty("errors")[0]
                .GetProperty("extensions")
                .GetProperty("code")
                .GetString()
        );

        const string packetStats = """
            query ($id: ID!) {
              meshPacketPayloadById(id: $id) { ... on MeshPacketPayload {
                packets {
                  edges { node { id receptionCount firstArrivedAt lastArrivedAt pathHops { prefix } } }
                }
              } }
            }
            """;
        var packetStatsEdges = (await ReadAsync(packetStats, new { id = groupId }))
            .GetProperty("meshPacketPayloadById")
            .GetProperty("packets")
            .GetProperty("edges");
        Assert.Equal(2, packetStatsEdges.GetArrayLength());
        Assert.Equal("11", common.GetProperty("pathHops")[0].GetProperty("prefix").GetString());
        Assert.Equal(
            new[] { "22", "33" },
            other
                .GetProperty("pathHops")
                .EnumerateArray()
                .Select(hop => hop.GetProperty("prefix").GetString())
        );
        var recent = new[]
        {
            r.GetProperty("node"),
            SingleNode(continued.GetProperty("receptions")),
        };
        Assert.Equal(
            new[] { second, third }.Order(),
            recent.Select(node => node.GetProperty("id").GetGuid()).Order()
        );
        Assert.All(recent, row =>
        {
            Assert.Equal(JsonValueKind.Null, row.GetProperty("heardAt").ValueKind);
            Assert.Equal("MeshPacketReception", row.GetProperty("__typename").GetString());
            Assert.NotEqual(Guid.Empty, row.GetProperty("observer").GetProperty("id").GetGuid());
            Assert.NotNull(row.GetProperty("region").GetProperty("code").GetString());
        });

        const string packetHistory = """
            query ($id: ID!, $first: Int!, $after: Cursor) {
              meshPacketPayloadById(id: $id) { ... on MeshPacketPayload {
                firstPacket {
                  id receptionCount firstArrivedAt lastArrivedAt
                  receptions(first: $first, after: $after) {
                    edges { cursor node { id packetId } }
                  }
                }
              } }
            }
            """;
        var firstPacket = (await ReadAsync(packetHistory, new { id = groupId, first = 1 }))
            .GetProperty("meshPacketPayloadById")
            .GetProperty("firstPacket");
        Assert.Equal(2, firstPacket.GetProperty("receptionCount").GetInt64());
        var firstHistoryEdge = Assert.Single(
            firstPacket.GetProperty("receptions").GetProperty("edges").EnumerateArray()
        );
        var packetHistoryCursor = firstHistoryEdge.GetProperty("cursor").GetString();
        Assert.Equal(second, firstHistoryEdge.GetProperty("node").GetProperty("id").GetGuid());
        var older = SingleNode((
                await ReadAsync(
                    packetHistory,
                    new
                    {
                        id = groupId,
                        first = 1,
                        after = packetHistoryCursor,
                    }
                )
            ).GetProperty("meshPacketPayloadById").GetProperty("firstPacket").GetProperty("receptions"));
        Assert.Equal(original, older.GetProperty("id").GetGuid());
        Assert.Equal(commonId, older.GetProperty("packetId").GetGuid());
        var directOlder = (
            await ReadAsync(
                selectedPacket,
                new
                {
                    id = commonId,
                    first = 1,
                    after = packetHistoryCursor,
                }
            )
        ).GetProperty("meshPacketById").GetProperty("receptions");
        Assert.Equal(original, SingleNode(directOlder).GetProperty("id").GetGuid());

        const string fromReception = """
            query ($id: ID!) { meshPacketReceptionById(id: $id) { ... on MeshPacketReception { packet { ... on MeshPacket {
              id receptionCount sharedPayload { id firstPacket { id } }
            } } } } }
            """;
        var seenPacket = (await ReadAsync(fromReception, new { id = third }))
            .GetProperty("meshPacketReceptionById")
            .GetProperty("packet");
        Assert.Equal(otherId, seenPacket.GetProperty("id").GetGuid());
        Assert.Equal(1, seenPacket.GetProperty("receptionCount").GetInt64());
        Assert.Equal(groupId, seenPacket.GetProperty("sharedPayload").GetProperty("id").GetGuid());
        Assert.Equal(commonId, RepresentativeId(seenPacket.GetProperty("sharedPayload")));
        Assert.Equal(
            groupId,
            (await ReadAsync(fromReception, new { id = original }))
                .GetProperty("meshPacketReceptionById")
                .GetProperty("packet")
                .GetProperty("sharedPayload")
                .GetProperty("id")
                .GetGuid()
        );
        var wrongScope = await PostAsync(
            details,
            new
            {
                id = newestNode.GetProperty("id").GetGuid(),
                packet = v.GetProperty("cursor").GetString(),
            }
        );
        Assert.Equal(
            "INVALID_CURSOR",
            wrongScope
                .GetProperty("errors")[0]
                .GetProperty("extensions")
                .GetProperty("code")
                .GetString()
        );

        var listedHistories = await ReadAsync(
            """
            {
              meshPacketPayloads(first: 2) {
                edges { node { id receptions(first: 1) { edges { node { id packetId } } } } }
              }
            }
            """,
            new { }
        );
        var listed = listedHistories
            .GetProperty("meshPacketPayloads")
            .GetProperty("edges")
            .EnumerateArray()
            .Select(edge => edge.GetProperty("node"))
            .ToDictionary(node => node.GetProperty("id").GetGuid());
        Assert.Equal(2, listed.Count);
        Assert.Equal(
            RepresentativeId(newestNode),
            SingleNode(listed[newestNode.GetProperty("id").GetGuid()].GetProperty("receptions"))
                .GetProperty("packetId")
                .GetGuid()
        );
        var listedAdvertPacket = SingleNode(listed[groupId].GetProperty("receptions"))
            .GetProperty("packetId")
            .GetGuid();
        Assert.Contains(listedAdvertPacket, new[] { commonId, otherId });

        var backfill = await ReceiveAndProcessAsync(
            database,
            observer,
            variant,
            at.AddSeconds(-1),
            token
        );
        var afterBackfill = SingleNode(
            (await ReadAsync(groups, new { after = headCursor })).GetProperty("meshPacketPayloads")
        );
        Assert.Equal(otherId, RepresentativeId(afterBackfill));
        var wrongPacket = await PostAsync(
            selectedPacket,
            new
            {
                id = otherId,
                first = 1,
                after = packetHistoryCursor,
            }
        );
        Assert.Equal(
            "INVALID_CURSOR",
            wrongPacket
                .GetProperty("errors")[0]
                .GetProperty("extensions")
                .GetProperty("code")
                .GetString()
        );

        await using var db = database.CreateDbContext();
        await RemoveFixtureReceptionsAsync([original, backfill]);
        var tied = (await ReadAsync(details, new { id = groupId })).GetProperty(
            "meshPacketPayloadById"
        );
        // Packet connection ordering is PostgreSQL UUID order, also used to break arrival ties.
        Assert.Equal(v.GetProperty("node").GetProperty("id").GetGuid(), RepresentativeId(tied));
        await RemoveFixtureReceptionsAsync([third]);
        var withoutReceptions = (
            await ReadAsync(selectedPacket, new { id = otherId, first = 1 })
        ).GetProperty("meshPacketById");
        Assert.Equal(otherId, withoutReceptions.GetProperty("id").GetGuid());
        Assert.Equal(0, withoutReceptions.GetProperty("receptionCount").GetInt64());
        Assert.Equal(JsonValueKind.Null, withoutReceptions.GetProperty("firstArrivedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, withoutReceptions.GetProperty("lastArrivedAt").ValueKind);
        Assert.Empty(
            withoutReceptions.GetProperty("receptions").GetProperty("edges").EnumerateArray()
        );
        await RemoveFixtureReceptionsAsync([second]);
        Assert.Equal(
            "MeshPacketNotFoundError",
            (await ReadAsync(selectedPacket, new { id = otherId, first = 1 }))
                .GetProperty("meshPacketById")
                .GetProperty("__typename")
                .GetString()
        );
        Assert.Equal(
            "MeshPacketPayloadNotFoundError",
            (await ReadAsync(details, new { id = groupId }))
                .GetProperty("meshPacketPayloadById")
                .GetProperty("__typename")
                .GetString()
        );
        Assert.Empty(
            (await ReadAsync(groups, new { after = headCursor }))
                .GetProperty("meshPacketPayloads")
                .GetProperty("edges")
                .EnumerateArray()
        );

        static Guid RepresentativeId(JsonElement payload) =>
            payload.GetProperty("firstPacket").GetProperty("id").GetGuid();

        async Task RemoveFixtureReceptionsAsync(Guid[] ids)
        {
            var observations = db.NeighbourObservations.Where(row =>
                ids.Contains(row.SourceReceptionId)
            );
            await db
                .CandidateNeighbourSupports.Where(row =>
                    (row.TargetReceptionId.HasValue && ids.Contains(row.TargetReceptionId.Value))
                    || observations
                        .Select(observation => observation.Id)
                        .Contains(row.NeighbourObservationId)
                )
                .ExecuteDeleteAsync(token);
            await observations.ExecuteDeleteAsync(token);
            await db
                .ReceptionPathAssessments.Where(row => ids.Contains(row.ReceptionId))
                .ExecuteDeleteAsync(token);
            await db
                .MeshPacketReceptions.Where(row => ids.Contains(row.Id))
                .ExecuteDeleteAsync(token);
        }
        async Task<JsonElement> ReadAsync(string query, object variables)
        {
            var result = await PostAsync(query, variables);
            Assert.False(result.TryGetProperty("errors", out _), result.ToString());
            return result.GetProperty("data");
        }

        async Task<JsonElement> PostAsync(string query, object variables)
        {
            using var response = await client.PostAsJsonAsync(
                "/graphql",
                new { query, variables },
                token
            );
            var json = await response.Content.ReadAsStringAsync(token);
            Assert.True(response.IsSuccessStatusCode, json);
            using var result = JsonDocument.Parse(json);
            return result.RootElement.Clone();
        }

        static JsonElement SingleNode(JsonElement connection) =>
            Assert.Single(connection.GetProperty("edges").EnumerateArray()).GetProperty("node");
    }
}
