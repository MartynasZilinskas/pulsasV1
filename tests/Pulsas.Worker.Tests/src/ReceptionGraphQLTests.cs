using System.Buffers;
using System.Collections.Immutable;
using System.Text.Json;
using Funcky.Monads;
using HotChocolate.Execution;
using HotChocolate.Transport.Formatters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodaTime;
using Pulsas.Gateway.ApiService;
using Pulsas.MeshCore.Packets;
using Pulsas.PacketProcessing.Contracts.Paths;
using Xunit;
using static Pulsas.PacketProcessing.Contracts.Paths.IMeshPacketPayloadReadManager;
using static Pulsas.PacketProcessing.Contracts.Paths.IReceptionBrowserManager;
using static Pulsas.PacketProcessing.Contracts.Paths.IReceptionReadManager;

namespace Pulsas.Worker.Tests;

public class ReceptionGraphQLTests
{
    private static readonly Guid ReceptionId = Guid.Parse("9224bdbf-7892-415a-8a25-72beb2f68cbc");
    private static readonly Instant At =
        Instant.FromUtc(2026, 11, 18, 12, 0) + Duration.FromNanoseconds(123);
    private const string Read = """
        query ($id: ID!) {
          meshPacketReceptionById(id: $id) {
            id arrivedAt heardAt rssi snr packetId
          }
        }
        """;
    private const string ReadAssessment = """
        query ReceptionDetail($id: ID!) {
          meshPacketReceptionById(id: $id) {
            id packetId arrivedAt heardAt
            pathAssessment {
              assessedAt policyVersion pathKind status
              hops {
                position prefix outcome
                selectedCandidate { nodeId publicKey confidence reasons supportCount }
                candidates(first: 8) { items { publicKey confidence } totalCount hasMore }
              }
            }
          }
        }
        """;

    [Theory]
    [InlineData("Production", true)]
    [InlineData("Development", false)]
    public async Task GraphQL_is_not_registered_unless_explicitly_enabled_in_development(
        string environment,
        bool enable
    )
    {
        await using var app = Build(environment, enable);
        Assert.Null(app.Services.GetService<IRequestExecutorProvider>());
        Assert.DoesNotContain(
            ((IEndpointRouteBuilder)app)
                .DataSources.SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>(),
            endpoint =>
                endpoint
                    .RoutePattern.RawText?.TrimStart('/')
                    .StartsWith("graphql", StringComparison.OrdinalIgnoreCase) == true
        );
    }

    [Fact]
    public async Task Legacy_id_and_scalar_contract_distinguishes_unknown_and_invalid_ids()
    {
        var reads = new ReceptionReads();
        await using var app = Build(Environments.Development, true, reads);
        var executor = await GetExecutorAsync(app, CancellationToken.None);
        var schema = executor.Schema.ToString();
        Assert.Contains(
            "meshPacketReceptionById(id: ID): MeshPacketReception @cost(weight: \"10\")",
            schema
        );
        Assert.DoesNotContain("serviceInfo", schema);
        Assert.DoesNotContain("type ServiceInfo", schema);
        Assert.Contains("arrivedAt: Instant!", schema);
        Assert.Contains("heardAt: Instant", schema);
        Assert.DoesNotContain("sourceReceptionId", schema);
        Assert.DoesNotContain("fromKey", schema);

        using var present = await ExecuteAsync(
            executor,
            Read,
            ReceptionId.ToString(),
            CancellationToken.None
        );
        var reception = Reception(present);
        Assert.Equal(ReceptionId, reception.GetProperty("id").GetGuid());
        Assert.Equal(
            "2026-11-18T12:00:00.000000123Z",
            reception.GetProperty("arrivedAt").GetString()
        );
        Assert.Equal(JsonValueKind.Null, reception.GetProperty("heardAt").ValueKind);
        Assert.Equal(-85, reception.GetProperty("rssi").GetInt32());
        Assert.Equal(4.25, reception.GetProperty("snr").GetDouble());
        Assert.Equal(0, reads.AssessmentReads);

        using var unknown = await ExecuteAsync(
            executor,
            Read,
            Guid.Empty.ToString(),
            CancellationToken.None
        );
        Assert.Equal(JsonValueKind.Null, Reception(unknown).ValueKind);
        using var omitted = await ExecuteAsync(
            executor,
            "{ meshPacketReceptionById { id } }",
            null,
            CancellationToken.None
        );
        Assert.Equal(JsonValueKind.Null, Reception(omitted).ValueKind);
        using var malformed = await ExecuteAsync(executor, Read, "bad", CancellationToken.None);
        Assert.Equal("INVALID_INPUT", ErrorCode(malformed));
        Assert.Equal(2, reads.ReceivedLookups);
    }

    [Theory]
    [InlineData(0, "NONE")]
    [InlineData(3, "ROOM_SERVER")]
    [InlineData(5, "RESERVED5")]
    [InlineData(15, "RESERVED15")]
    public async Task Legacy_advert_role_values_are_preserved(byte role, string expected)
    {
        // The no-path packet carries an ADVERT with a one-byte app-data flags field.
        var packet = new byte[103];
        packet[0] = (byte)((int)PacketType.Advert << 2 | (int)RouteType.Flood);
        packet[^1] = role;
        var reads = new ReceptionReads
        {
            Details = new(
                null,
                null,
                null,
                new(
                    Guid.NewGuid(),
                    "short",
                    new byte[32],
                    packet,
                    PacketSignatureStatus.NotChecked,
                    null
                )
            ),
        };
        await using var app = Build(Environments.Development, true, reads);
        var executor = await GetExecutorAsync(app, CancellationToken.None);
        const string query = """
            query ($id: ID!) {
              meshPacketReceptionById(id: $id) {
                packet { ... on MeshPacket { payload { ... on AdvertPayload { role } } } }
              }
            }
            """;
        using var result = await ExecuteAsync(
            executor,
            query,
            ReceptionId.ToString(),
            CancellationToken.None
        );
        Assert.Equal(
            expected,
            Reception(result)
                .GetProperty("packet")
                .GetProperty("payload")
                .GetProperty("role")
                .GetString()
        );
    }

    [Fact]
    public async Task Bounded_candidates_do_not_hide_selection_and_missing_assessment_is_null()
    {
        var selected = new PathCandidateAssessment(
            Guid.NewGuid(),
            "selected",
            PathCandidateConfidence.Strong,
            true,
            [PathCandidateReason.NeighbourSupport],
            [
                new(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "from",
                    "to",
                    default,
                    default
                ),
            ]
        );
        var weak = selected with
        {
            PublicKey = "weak",
            Confidence = PathCandidateConfidence.Weak,
            Selected = false,
            Reasons = [PathCandidateReason.PrefixMatch],
            Supports = [],
        };
        var reads = new ReceptionReads
        {
            Assessment = new(
                At,
                "test-policy",
                new(
                    PathAssessmentKind.FloodHistory,
                    PathAssessmentStatus.Resolved,
                    [
                        new(
                            0,
                            1,
                            "0a",
                            PathHopOutcome.Resolved,
                            [.. Enumerable.Repeat(weak, 8), selected]
                        ),
                    ]
                )
            ),
        };
        await using var app = Build(Environments.Development, true, reads);
        var executor = await GetExecutorAsync(app, CancellationToken.None);
        const string query = """
            query ($id: ID!) {
              meshPacketReceptionById(id: $id) {
                pathAssessment {
                  assessedAt policyVersion status pathKind
                  hops {
                    position prefix width outcome
                    selectedCandidate { nodeId publicKey confidence selected reasons supportCount }
                    candidates { items { publicKey } totalCount hasMore }
                    next: candidates(first: 1, offset: 8) { items { publicKey } hasMore }
                  }
                }
              }
            }
            """;
        using var found = await ExecuteAsync(
            executor,
            query,
            ReceptionId.ToString(),
            CancellationToken.None
        );
        var assessment = Reception(found).GetProperty("pathAssessment");
        Assert.Equal("test-policy", assessment.GetProperty("policyVersion").GetString());
        Assert.Equal("RESOLVED", assessment.GetProperty("status").GetString());
        var hop = Assert.Single(assessment.GetProperty("hops").EnumerateArray());
        Assert.Equal("0a", hop.GetProperty("prefix").GetString());
        var first = hop.GetProperty("candidates");
        Assert.Equal(9, first.GetProperty("totalCount").GetInt32());
        Assert.Equal(8, first.GetProperty("items").GetArrayLength());
        Assert.True(first.GetProperty("hasMore").GetBoolean());
        var winner = hop.GetProperty("selectedCandidate");
        Assert.Equal(selected.NodeId, winner.GetProperty("nodeId").GetGuid());
        Assert.Equal("selected", winner.GetProperty("publicKey").GetString());
        Assert.Equal("NEIGHBOUR_SUPPORT", winner.GetProperty("reasons")[0].GetString());
        Assert.Equal(1, winner.GetProperty("supportCount").GetInt32());
        Assert.Equal(
            "selected",
            hop.GetProperty("next").GetProperty("items")[0].GetProperty("publicKey").GetString()
        );

        reads.Assessment = null;
        using var missing = await ExecuteAsync(
            executor,
            ReadAssessment,
            ReceptionId.ToString(),
            CancellationToken.None
        );
        Assert.Equal(
            JsonValueKind.Null,
            Reception(missing).GetProperty("pathAssessment").ValueKind
        );
    }

    [Fact]
    public async Task Oversized_snapshot_is_an_error_not_a_missing_assessment()
    {
        var reads = new ReceptionReads
        {
            AssessmentError = Option.Some(ReadPathAssessmentError.SnapshotTooLarge),
        };
        await using var app = Build(Environments.Development, true, reads);
        var executor = await GetExecutorAsync(app, CancellationToken.None);
        using var result = await ExecuteAsync(
            executor,
            ReadAssessment,
            ReceptionId.ToString(),
            CancellationToken.None
        );
        Assert.Equal("PATH_ASSESSMENT_TOO_LARGE", ErrorCode(result));
        Assert.Equal(
            ReceptionId,
            result
                .RootElement.GetProperty("data")
                .GetProperty("meshPacketReceptionById")
                .GetProperty("id")
                .GetGuid()
        );
    }

    [Theory]
    [InlineData("candidates(first: 21)")]
    [InlineData("candidates(offset: -1)")]
    public async Task Invalid_candidate_pages_are_rejected(string page)
    {
        var reads = new ReceptionReads
        {
            Assessment = new(
                At,
                "test-policy",
                new(
                    PathAssessmentKind.FloodHistory,
                    PathAssessmentStatus.Unresolved,
                    [new(0, 1, "0a", PathHopOutcome.Unresolved, [])]
                )
            ),
        };
        await using var app = Build(Environments.Development, true, reads);
        var executor = await GetExecutorAsync(app, CancellationToken.None);
        var query =
            "query ($id: ID!) { meshPacketReceptionById(id: $id) { pathAssessment { hops { "
            + page
            + " { totalCount } } } } }";
        using var result = await ExecuteAsync(
            executor,
            query,
            ReceptionId.ToString(),
            CancellationToken.None
        );
        Assert.Equal("INVALID_INPUT", ErrorCode(result));
    }

    [Fact]
    public async Task Excessive_aliased_assessment_reads_are_rejected_before_database_access()
    {
        var reads = new ReceptionReads();
        await using var app = Build(Environments.Development, true, reads);
        var executor = await GetExecutorAsync(app, CancellationToken.None);
        const string query = """
            query ($id: ID!) {
              meshPacketReceptionById(id: $id) {
                a: pathAssessment { status }
                b: pathAssessment { status }
                c: pathAssessment { status }
              }
            }
            """;
        using var result = await ExecuteAsync(
            executor,
            query,
            ReceptionId.ToString(),
            CancellationToken.None
        );
        Assert.True(result.RootElement.TryGetProperty("errors", out _));
        Assert.Equal(0, reads.ReceivedLookups);
        Assert.Equal(0, reads.AssessmentReads);
    }

    [Fact]
    public async Task Histogram_aliases_beyond_the_legacy_root_limit_are_rejected_before_database_access()
    {
        var reads = new HistogramReads();
        await using var app = Build(Environments.Development, true, browser: reads);
        var executor = await GetExecutorAsync(app, CancellationToken.None);
        const string fragment =
            "fragment TwoHistograms on Query { a: meshPacketReceptionHistogram { isSnapshot } b: meshPacketReceptionHistogram { isSnapshot } }";
        const string four =
            "...TwoHistograms c: meshPacketReceptionHistogram { isSnapshot } d: meshPacketReceptionHistogram { isSnapshot }";
        using var rejected = await ExecuteAsync(
            executor,
            "{ " + four + " e: meshPacketReceptionHistogram { isSnapshot } } " + fragment,
            null,
            CancellationToken.None
        );
        Assert.Equal("RESOURCE_LIMIT", ErrorCode(rejected));
        Assert.Equal(0, reads.Calls);

        using var accepted = await ExecuteAsync(
            executor,
            "{ " + four + " } " + fragment,
            null,
            CancellationToken.None
        );
        Assert.False(
            accepted.RootElement.TryGetProperty("errors", out _),
            accepted.RootElement.ToString()
        );
        Assert.Equal(4, reads.Calls);
    }

    private static WebApplication Build(
        string environment,
        bool enable,
        ReceptionReads? reads = null,
        HistogramReads? browser = null,
        PayloadReads? groups = null
    ) => GatewayApplication.Build(new() { EnvironmentName = environment }, builder =>
        {
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Gateway:EnableReceptionReads"] = enable.ToString(),
                    ["ConnectionStrings:pulsas"] =
                        reads is null && browser is null && groups is null
                            ? null
                            : "Host=127.0.0.1;Database=unused",
                }
            );
            if (reads is not null)
            {
                builder.Services.AddSingleton<IReceptionReadManager>(reads);
            }
            if (browser is not null)
            {
                builder.Services.AddSingleton<IReceptionBrowserManager>(browser);
            }
            if (groups is not null)
            {
                builder.Services.AddSingleton<IMeshPacketPayloadReadManager>(groups);
            }
        });

    [Theory]
    [InlineData(
        "{ a: meshPacketPayloads { pageInfo { hasNextPage } } b: meshPacketPayloads { pageInfo { hasNextPage } } c: meshPacketPayloads { pageInfo { hasNextPage } } d: meshPacketPayloads { pageInfo { hasNextPage } } }"
    )]
    [InlineData(
        "{ meshPacketPayloads(first: 4) { edges { node { receptions { pageInfo { hasNextPage } } } } } }"
    )]
    [InlineData(
        "{ meshPacketReceptions(first: 100) { edges { node { packet { ... on MeshPacket { receptionCount firstArrivedAt lastArrivedAt } } } } } }"
    )]
    public async Task Excessive_payload_reads_are_rejected_before_database_access(string query)
    {
        var groups = new PayloadReads();
        var browser = new HistogramReads();
        await using var app = Build(
            Environments.Development,
            true,
            browser: browser,
            groups: groups
        );
        var executor = await GetExecutorAsync(app, CancellationToken.None);
        using var result = await ExecuteAsync(executor, query, null, CancellationToken.None);
        Assert.Equal("HC0047", ErrorCode(result));
        Assert.Equal(0, groups.Calls);
    }

    [Fact]
    public async Task Traversing_shared_payload_cycle_is_bounded_before_database_access()
    {
        var groups = new PayloadReads();
        await using var app = Build(Environments.Development, true, groups: groups);
        var executor = await GetExecutorAsync(app, CancellationToken.None);
        const string query = """
            query ($id: ID!) {
              meshPacketPayloadById(id: $id) {
                firstPacket { sharedPayload { firstPacket { sharedPayload {
                  firstPacket { sharedPayload { id } }
                } } } }
              }
            }
            """;
        using var result = await ExecuteAsync(
            executor,
            query,
            ReceptionId.ToString(),
            CancellationToken.None
        );
        Assert.Equal("HC0047", ErrorCode(result));
        Assert.Equal(0, groups.Calls);
    }

    [Fact]
    public async Task Payload_lookup_batches_aliases_and_returns_null_for_missing_payloads()
    {
        var groups = new PayloadReads();
        await using var app = Build(Environments.Development, true, groups: groups);
        var executor = await GetExecutorAsync(app, CancellationToken.None);
        const string query = """
            query ($id: ID!, $other: ID!) {
              a: meshPacketPayloadById(id: $id) { firstPacket { id } }
              b: meshPacketPayloadById(id: $id) { id }
              c: meshPacketPayloadById(id: $other) { id }
            }
            """;
        var other = Guid.NewGuid();
        using var result = await ExecuteAsync(
            executor,
            query,
            null,
            CancellationToken.None,
            JsonSerializer.Serialize(new { id = ReceptionId, other })
        );
        Assert.False(
            result.RootElement.TryGetProperty("errors", out _),
            result.RootElement.ToString()
        );
        Assert.All(result.RootElement.GetProperty("data").EnumerateObject(), item =>
            Assert.Equal(JsonValueKind.Null, item.Value.ValueKind)
        );
        Assert.Equal(1, groups.Calls);
        Assert.Equal(new[] { ReceptionId, other }.Order(), groups.LookupIds.Order());
    }

    private class PayloadReads : IMeshPacketPayloadReadManager
    {
        private int calls;
        public int Calls => calls;
        public ImmutableArray<Guid> LookupIds { get; private set; } = [];

        public Task<ImmutableArray<PayloadRow>> ReadPayloadsAsync(
            int limit,
            Position? after,
            CancellationToken cancellationToken
        )
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(ImmutableArray<PayloadRow>.Empty);
        }

        public Task<ImmutableArray<PayloadRow>> ReadPayloadsByIdAsync(
            IReadOnlyList<Guid> ids,
            CancellationToken cancellationToken
        )
        {
            Interlocked.Increment(ref calls);
            LookupIds = [.. ids];
            return Task.FromResult(ImmutableArray<PayloadRow>.Empty);
        }

        public Task<ImmutableArray<PacketPageRow>> ReadPacketsAsync(
            IReadOnlyList<PacketPage> pages,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<ImmutableArray<ReceptionPageRow>> ReadHistoryAsync(
            IReadOnlyList<ReceptionPage> pages,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<ImmutableArray<PacketStatsRow>> ReadPacketStatsAsync(
            IReadOnlyList<Guid> ids,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();
    }

    private static async Task<IRequestExecutor> GetExecutorAsync(
        WebApplication app,
        CancellationToken cancellationToken
    ) =>
        await app
            .Services.GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync(cancellationToken: cancellationToken);

    private static async Task<JsonDocument> ExecuteAsync(
        IRequestExecutor executor,
        string query,
        string? id,
        CancellationToken cancellationToken,
        string? variables = null
    )
    {
        var request = OperationRequestBuilder
            .New()
            .SetDocument(query)
            .SetVariableValues(
                variables ?? (id is null ? "{}" : JsonSerializer.Serialize(new { id }))
            )
            .Build();
        await using var result = await executor.ExecuteAsync(request, cancellationToken);
        var buffer = new ArrayBufferWriter<byte>();
        new JsonResultFormatter().Format(Assert.IsType<OperationResult>(result), buffer);
        return JsonDocument.Parse(buffer.WrittenMemory);
    }

    private static JsonElement Reception(JsonDocument result)
    {
        Assert.False(
            result.RootElement.TryGetProperty("errors", out _),
            result.RootElement.ToString()
        );
        return result.RootElement.GetProperty("data").GetProperty("meshPacketReceptionById");
    }

    private static string? ErrorCode(JsonDocument result) =>
        result
            .RootElement.GetProperty("errors")[0]
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString();

    private class HistogramReads : IReceptionBrowserManager
    {
        private int histogramCalls;
        public int Calls => histogramCalls;

        public Task<ImmutableArray<HistogramCount>> ReadHistogramAsync(
            ReceptionFilter filter,
            Instant from,
            Instant to,
            int intervalSeconds,
            CancellationToken cancellationToken
        )
        {
            Interlocked.Increment(ref histogramCalls);
            return Task.FromResult(ImmutableArray<HistogramCount>.Empty);
        }

        public Task<ImmutableArray<ReadReceptionResult>> ReadReceptionsAsync(
            ReceptionFilter filter,
            int limit,
            Position? after,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<ImmutableArray<ObserverRow>> ReadObserversAsync(
            string? name,
            Instant? onlineAfter,
            bool? online,
            int limit,
            Position? after,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<ImmutableArray<RegionRow>> ReadRegionsAsync(
            int limit,
            string? afterCode,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<string?> ReadRegionCodeAsync(
            Guid regionId,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();
    }

    private class ReceptionReads : IReceptionReadManager
    {
        public int ReceivedLookups { get; private set; }
        public int AssessmentReads { get; private set; }
        public ReadPathAssessmentResult? Assessment { get; set; }
        public ReceptionReadDetails? Details { get; init; }
        public Option<ReadPathAssessmentError> AssessmentError { get; init; }

        public Task<Option<ReadReceptionResult>> ReadReceptionAsync(
            Guid id,
            CancellationToken cancellationToken
        )
        {
            ReceivedLookups++;
            return Task.FromResult(
                id == ReceptionId
                    ? Option.Some(
                        new ReadReceptionResult(
                            id,
                            At,
                            null,
                            -85,
                            4.25,
                            Details?.Packet?.Id,
                            Details
                        )
                    )
                    : Option<ReadReceptionResult>.None
            );
        }

        public Task<
            Either<ReadPathAssessmentError, Option<ReadPathAssessmentResult>>
        > ReadPathAssessmentAsync(Guid receptionId, CancellationToken cancellationToken)
        {
            AssessmentReads++;
            return Task.FromResult(
                AssessmentError.Match(
                    none: () =>
                        Either<ReadPathAssessmentError, Option<ReadPathAssessmentResult>>.Right(
                            Option.FromNullable(Assessment)
                        ),
                    some: Either<ReadPathAssessmentError, Option<ReadPathAssessmentResult>>.Left
                )
            );
        }
    }
}
