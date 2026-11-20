using System.Buffers;
using System.Collections.Immutable;
using System.Text.Json;
using Funcky.Monads;
using HotChocolate.CostAnalysis.Types;
using HotChocolate.Execution;
using HotChocolate.Transport.Formatters;
using HotChocolate.Types;
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
            __typename
            ... on MeshPacketReception { id arrivedAt heardAt rssi snr packetId }
            ... on MeshPacketReceptionNotFoundError { message }
          }
        }
        """;
    private const string ReadAssessment = """
        query ReceptionDetail($id: ID!) {
          meshPacketReceptionById(id: $id) { ... on MeshPacketReception {
            id packetId arrivedAt heardAt
            pathAssessment {
              assessedAt policyVersion pathKind status
              hops {
                position prefix outcome
                selectedCandidate { nodeId publicKey confidence reasons supportCount }
                candidates(first: 8) { items { publicKey confidence } totalCount hasMore }
              }
            }
          } }
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
    public async Task Required_id_and_scalar_contract_distinguishes_unknown_and_invalid_ids()
    {
        var reads = new ReceptionReads();
        await using var app = Build(Environments.Development, true, reads);
        var executor = await GetExecutorAsync(app, CancellationToken.None);
        var schema = executor.Schema.ToString();
        Assert.DoesNotContain("serviceInfo", schema);
        Assert.DoesNotContain("type ServiceInfo", schema);
        Assert.Contains("arrivedAt: Instant!", schema);
        Assert.Contains("heardAt: Instant", schema);
        Assert.Contains("regionCodes: [String!]", schema);
        Assert.Contains("code: String!", schema);
        Assert.DoesNotContain("IATA", schema);
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
        Assert.Equal(
            "MeshPacketReceptionNotFoundError",
            Reception(unknown).GetProperty("__typename").GetString()
        );
        using var omitted = await ExecuteAsync(
            executor,
            "{ meshPacketReceptionById { __typename } }",
            null,
            CancellationToken.None
        );
        Assert.True(omitted.RootElement.TryGetProperty("errors", out _));
        using var malformed = await ExecuteAsync(executor, Read, "bad", CancellationToken.None);
        Assert.Equal("INVALID_INPUT", ErrorCode(malformed));
        Assert.Equal(2, reads.ReceivedLookups);
    }

    [Theory]
    [InlineData(
        "meshPacketReceptionById",
        "id",
        "MeshPacketReceptionByIdResult",
        "MeshPacketReception",
        "MeshPacketReceptionNotFoundError",
        10
    )]
    [InlineData(
        "meshPacketPayloadById",
        "id",
        "MeshPacketPayloadByIdResult",
        "MeshPacketPayload",
        "MeshPacketPayloadNotFoundError",
        9001
    )]
    [InlineData(
        "meshPacketById",
        "id",
        "MeshPacketByIdResult",
        "MeshPacket",
        "MeshPacketNotFoundError",
        9001
    )]
    [InlineData(
        "observerActivitySummary",
        "observerId",
        "ObserverActivitySummaryResult",
        "ObserverActivitySummary",
        "ObserverNotFoundError",
        12001
    )]
    public async Task Lookup_schema_requires_an_id_and_preserves_nullable_result_and_cost(
        string field,
        string argument,
        string result,
        string success,
        string miss,
        int cost
    )
    {
        await using var app = Build(Environments.Development, true, new ReceptionReads());
        var executor = await GetExecutorAsync(app, CancellationToken.None);
        var lookup = executor.Schema.QueryType.Fields[field];
        Assert.Equal("ID!", lookup.Arguments[argument].Type.ToString());
        var union = Assert.IsAssignableFrom<UnionType>(lookup.Type);
        Assert.Equal(result, union.Name);
        Assert.Equal(
            new[] { success, miss }.Order(),
            union.Types.Select(type => type.Name).Order()
        );
        var directive = lookup.Directives.FirstOrDefault<CostDirective>();
        Assert.NotNull(directive);
        Assert.Equal(cost, directive.ToValue<CostDirective>().Weight);
        Assert.DoesNotContain(
            executor.Schema.Types.GetType<ObjectType>("MeshPacketPayload").Fields,
            field => field.Name == "packet"
        );
    }

    [Fact]
    public async Task Invalid_lookup_and_reader_failure_do_not_erase_sibling_data()
    {
        var reads = new ReceptionReads { Fail = true };
        var groups = new PayloadReads();
        await using var app = Build(Environments.Development, true, reads, groups: groups);
        var executor = await GetExecutorAsync(app, CancellationToken.None);
        const string query = """
            query ($id: ID!) {
              malformed: meshPacketById(id: "bad") { __typename }
              unavailable: meshPacketReceptionById(id: $id) { __typename }
              sibling: meshPacketPayloadById(id: $id) { __typename }
            }
            """;
        using var result = await ExecuteAsync(
            executor,
            query,
            ReceptionId.ToString(),
            CancellationToken.None
        );
        var root = result.RootElement;
        var errors = root.GetProperty("errors")
            .EnumerateArray()
            .ToDictionary(error => error.GetProperty("path")[0].GetString()!);
        Assert.Equal(2, errors.Count);
        Assert.Equal(
            "INVALID_INPUT",
            errors["malformed"].GetProperty("extensions").GetProperty("code").GetString()
        );
        Assert.Contains("unavailable", errors.Keys);
        Assert.DoesNotContain("fixture read failure", root.ToString());
        var data = root.GetProperty("data");
        Assert.Equal(JsonValueKind.Null, data.GetProperty("malformed").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("unavailable").ValueKind);
        Assert.Equal(
            "MeshPacketPayloadNotFoundError",
            data.GetProperty("sibling").GetProperty("__typename").GetString()
        );
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
              meshPacketReceptionById(id: $id) { ... on MeshPacketReception {
                packet { ... on MeshPacket { payload { ... on AdvertPayload { role } } } }
              } }
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
    public async Task Region_codes_use_strings_and_preserve_case_and_whitespace()
    {
        const string code = " North-01 ";
        var reads = new HistogramReads { Region = new(Guid.NewGuid(), code, null, "LT") };
        await using var app = Build(Environments.Development, true, browser: reads);
        var executor = await GetExecutorAsync(app, CancellationToken.None);
        const string query = """
            query ($codes: [String!]) {
              meshPacketReceptionHistogram(filter: { regionCodes: $codes }) { isSnapshot }
              regions { edges { node { code } } }
            }
            """;
        using var result = await ExecuteAsync(
            executor,
            query,
            null,
            CancellationToken.None,
            JsonSerializer.Serialize(new { codes = new[] { code } })
        );
        Assert.False(
            result.RootElement.TryGetProperty("errors", out _),
            result.RootElement.ToString()
        );
        Assert.Equal(code, Assert.Single(reads.LastFilter!.RegionCodes));
        var edges = result
            .RootElement.GetProperty("data")
            .GetProperty("regions")
            .GetProperty("edges");
        Assert.Equal(
            code,
            Assert
                .Single(edges.EnumerateArray())
                .GetProperty("node")
                .GetProperty("code")
                .GetString()
        );

        using var invalid = await ExecuteAsync(
            executor,
            query,
            null,
            CancellationToken.None,
            """{ "codes": [""] }"""
        );
        Assert.Equal("INVALID_INPUT", ErrorCode(invalid));
        Assert.Equal(1, reads.Calls);
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
              meshPacketReceptionById(id: $id) { ... on MeshPacketReception {
                pathAssessment {
                  assessedAt policyVersion status pathKind
                  hops {
                    position prefix width outcome
                    selectedCandidate { nodeId publicKey confidence selected reasons supportCount }
                    candidates { items { publicKey } totalCount hasMore }
                    next: candidates(first: 1, offset: 8) { items { publicKey } hasMore }
                  }
                }
              } }
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
            "query ($id: ID!) { meshPacketReceptionById(id: $id) { ... on MeshPacketReception { pathAssessment { hops { "
            + page
            + " { totalCount } } } } } }";
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
              meshPacketReceptionById(id: $id) { ... on MeshPacketReception {
                a: pathAssessment { status }
                b: pathAssessment { status }
                c: pathAssessment { status }
              } }
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
    [InlineData(
        """
            query { ...TwoLookups c: meshPacketById(id: "a") { ...Miss } d: meshPacketById(id: "b") { ...Miss } }
            fragment TwoLookups on Query { a: meshPacketById(id: "c") { ...Miss } b: meshPacketById(id: "d") { ...Miss } }
            fragment Miss on MeshPacketByIdResult { ... on MeshPacketNotFoundError { message } }
            """
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
              meshPacketPayloadById(id: $id) { ... on MeshPacketPayload {
                firstPacket { sharedPayload { firstPacket { sharedPayload {
                  firstPacket { sharedPayload { id } }
                } } } }
              } }
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

    [Theory]
    [InlineData("meshPacketPayloadById", "MeshPacketPayloadNotFoundError")]
    [InlineData("meshPacketById", "MeshPacketNotFoundError")]
    public async Task Lookup_batches_aliases_and_returns_typed_misses(
        string field,
        string errorType
    )
    {
        var groups = new PayloadReads();
        await using var app = Build(Environments.Development, true, groups: groups);
        var executor = await GetExecutorAsync(app, CancellationToken.None);
        var query = $$"""
            query ($id: ID!, $other: ID!) {
              a: {{field}}(id: $id) { __typename ... on {{errorType}} { message } }
              b: {{field}}(id: $id) { __typename }
              c: {{field}}(id: $other) { __typename }
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
            Assert.Equal(errorType, item.Value.GetProperty("__typename").GetString())
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

        public Task<ImmutableArray<PacketRow>> ReadPacketsByIdAsync(
            IReadOnlyList<Guid> ids,
            CancellationToken cancellationToken
        )
        {
            Interlocked.Increment(ref calls);
            LookupIds = [.. ids];
            return Task.FromResult(ImmutableArray<PacketRow>.Empty);
        }

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
        public ReceptionFilter? LastFilter { get; private set; }
        public RegionRow? Region { get; init; }

        public Task<ImmutableArray<HistogramCount>> ReadHistogramAsync(
            ReceptionFilter filter,
            Instant from,
            Instant to,
            int intervalSeconds,
            CancellationToken cancellationToken
        )
        {
            Interlocked.Increment(ref histogramCalls);
            LastFilter = filter;
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
        ) => Task.FromResult<ImmutableArray<RegionRow>>(Region is { } row ? [row] : []);

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
        public bool Fail { get; init; }
        public Option<ReadPathAssessmentError> AssessmentError { get; init; }

        public Task<ImmutableArray<ReadReceptionResult>> ReadReceptionsByIdAsync(
            IReadOnlyList<Guid> ids,
            CancellationToken cancellationToken
        )
        {
            ReceivedLookups++;
            if (Fail)
            {
                throw new InvalidOperationException("fixture read failure");
            }
            return Task.FromResult<ImmutableArray<ReadReceptionResult>>(
                ids.Contains(ReceptionId)
                    ? [new(ReceptionId, At, null, -85, 4.25, Details?.Packet?.Id, Details)]
                    : []
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
