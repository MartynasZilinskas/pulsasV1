using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Funcky;
using Funcky.Monads;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Pulsas.MeshCore.Nodes;
using Pulsas.MeshCore.Packets;
using Pulsas.MeshCore.Payloads.Adverts;
using Pulsas.PacketProcessing.Contracts.Paths;
using Pulsas.Persistence;
using Pulsas.Persistence.Paths;
using Pulsas.Persistence.Receptions;
using static Pulsas.PacketProcessing.Contracts.Paths.IPathAssessmentManager;

namespace Pulsas.PacketProcessing.Core.Paths;

public class PathAssessmentManager(TrackingDbContext database, IClock clock)
    : IPathAssessmentManager
{
    public const string PolicyVersion = "neighbours-reception-v1";

    public async Task<Either<AssessReceptionError, Unit>> AssessReceptionAsync(
        AssessReceptionInput input,
        CancellationToken cancellationToken
    )
    {
        if (database.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Path assessment requires the delivery transaction."
            );
        }
        var alreadyAssessed = await database.ReceptionPathAssessments.AnyAsync(
            r => r.ReceptionId == input.ReceptionId,
            cancellationToken
        );
        return await Option
            .Some(input)
            .Where(_ => !alreadyAssessed)
            .Select(pending => AssessPendingReceptionAsync(pending, cancellationToken))
            .GetOrElse(() => Task.FromResult<Either<AssessReceptionError, Unit>>(Unit.Value));
    }

    private async Task<Either<AssessReceptionError, Unit>> AssessPendingReceptionAsync(
        AssessReceptionInput input,
        CancellationToken cancellationToken
    )
    {
        var reception = await database
            .MeshPacketReceptions.AsNoTracking()
            .Include(r => r.Observer)
            .Include(r => r.Packet)
            .SingleOrDefaultAsync(r => r.Id == input.ReceptionId, cancellationToken);
        return await Option
            .FromNullable(reception)
            .SelectMany(row =>
                Option
                    .FromNullable(row.Packet)
                    .Select(storedPacket =>
                        (
                            Reception: row,
                            Packet: new ReceptionPacket(
                                storedPacket.Id,
                                storedPacket.ContentId,
                                [.. storedPacket.RawPacket],
                                Convert.ToHexStringLower(row.Observer.PublicKey),
                                row.Association != PacketAssociation.Unverified
                            )
                        )
                    )
            )
            .ToEither(AssessReceptionError.MissingReceptionPacket)
            .Select(context =>
                PacketCodec
                    .Decode(context.Packet.RawPacket.AsSpan())
                    .Select(frame =>
                        AssessDecodedReceptionAsync(
                            context.Reception,
                            context.Packet,
                            frame,
                            cancellationToken
                        )
                    )
                    .GetOrElse(_ =>
                        throw new InvalidOperationException("Stored packet cannot be decoded.")
                    )
            )
            .Sequence();
    }

    private async Task<Unit> AssessDecodedReceptionAsync(
        MeshPacketReception reception,
        ReceptionPacket packet,
        MeshCorePacket frame,
        CancellationToken cancellationToken
    )
    {
        var kind = GetPathKind(frame);
        var unsupported = GetUnsupportedStatus(frame, kind);
        var isSupported = unsupported == Option<PathAssessmentStatus>.None;
        var knowledge = await LoadKnowledgeAsync(cancellationToken);
        var advertiser =
            packet.HasRecordedAssociation && isSupported
                ? VerifiedAdvertiser(frame)
                : Option<string>.None;
        var hops = isSupported
            ? await AssessPathAsync(packet, frame, knowledge, advertiser, cancellationToken)
            : ImmutableArray<PathHopAssessment>.Empty;

        await SaveAssessmentAsync(
            reception,
            frame,
            knowledge.AssessedAt,
            new PathAssessmentResult(kind, unsupported.GetOrElse(() => GetStatus(hops)), hops),
            advertiser,
            cancellationToken
        );
        return Unit.Value;
    }

    private async Task SaveAssessmentAsync(
        MeshPacketReception reception,
        MeshCorePacket frame,
        DateTimeOffset assessedAt,
        PathAssessmentResult result,
        Option<string> advertiser,
        CancellationToken cancellationToken
    )
    {
        await advertiser.Match(
            none: () => Task.CompletedTask,
            some: key => SaveObservationAsync(reception, frame, key, cancellationToken)
        );
        database.ReceptionPathAssessments.Add(
            new ReceptionPathAssessment
            {
                ReceptionId = reception.Id,
                AssessedAt = Instant.FromDateTimeOffset(assessedAt),
                PolicyVersion = PolicyVersion,
                AdmitsNeighbourEvidence = advertiser != Option<string>.None,
                Result = JsonSerializer.Serialize(result),
            }
        );
        await database.SaveChangesAsync(cancellationToken);
    }

    private static PathAssessmentKind GetPathKind(MeshCorePacket frame) =>
        frame.PacketType == PacketType.Trace ? PathAssessmentKind.TraceProgress
        : frame.RouteType is RouteType.Flood or RouteType.TransportFlood
            ? PathAssessmentKind.FloodHistory
        : PathAssessmentKind.DirectRemainingRoute;

    private static Option<PathAssessmentStatus> GetUnsupportedStatus(
        MeshCorePacket frame,
        PathAssessmentKind kind
    ) =>
        !frame.IsSupportedVersion ? PathAssessmentStatus.UnsupportedVersion
        : frame.PacketType == PacketType.Trace ? PathAssessmentStatus.UnsupportedTraceAssessment
        : !frame.IsSupportedPathWidth ? PathAssessmentStatus.UnsupportedWidth
        : kind != PathAssessmentKind.FloodHistory ? PathAssessmentStatus.UnsupportedDirectAssessment
        : Option<PathAssessmentStatus>.None;

    private async Task<ImmutableArray<PathHopAssessment>> AssessPathAsync(
        ReceptionPacket packet,
        MeshCorePacket frame,
        AssessmentKnowledge knowledge,
        Option<string> advertiser,
        CancellationToken cancellationToken
    )
    {
        var prefixes = Enumerable
            .Range(0, frame.PathCount)
            .Select(i =>
                Convert.ToHexStringLower(frame.Path.AsSpan(i * frame.PathWidth, frame.PathWidth))
            )
            .ToImmutableArray();
        var steps = await AttachPathAsync(packet, frame, prefixes, cancellationToken);
        var matches = new Dictionary<string, CatalogueNode[]>(StringComparer.Ordinal);
        CatalogueNode[] Candidates(string prefix)
        {
            if (!matches.TryGetValue(prefix, out var result))
            {
                matches[prefix] = result = [
                    .. knowledge
                        .Nodes.Where(n => n.Key.StartsWith(prefix, StringComparison.Ordinal))
                        .OrderBy(n => n.Key, StringComparer.Ordinal),
                ];
            }
            return result;
        }

        var sets = MatchPathCandidates(steps, prefixes, knowledge.Nodes, Candidates);
        var edges = GetIndependentEdges(knowledge.Observations, packet.ContentId, Candidates);
        var observer = packet.HasRecordedAssociation
            ? Option.Some(packet.ObserverKey)
            : Option<string>.None;
        ImmutableArray<PathHopAssessment> hops =
        [
            .. prefixes.Select(
                (prefix, position) =>
                    AssessHop(position, frame.PathWidth, prefix, sets, edges, advertiser, observer)
            ),
        ];
        return hops;
    }

    private static CatalogueNode[][] MatchPathCandidates(
        ImmutableArray<PathStep> steps,
        ImmutableArray<string> prefixes,
        ImmutableArray<CatalogueNode> nodes,
        Func<string, CatalogueNode[]> candidates
    )
    {
        var fingerprint = Fingerprint(nodes);
        var nodesById = nodes.ToDictionary(n => n.Id);
        var sets = steps
            .Select(
                (step, position) =>
                    step.CandidateCatalogueFingerprint == fingerprint
                    && step.CandidateNodeIds is { } ids
                        ? ids.Select(id => nodesById[id]).ToArray()
                        : Refresh(step, prefixes[position], fingerprint)
            )
            .ToArray();
        return sets;

        CatalogueNode[] Refresh(PathStep step, string prefix, string currentFingerprint)
        {
            var current = candidates(prefix);
            step.CandidateCatalogueFingerprint = currentFingerprint;
            step.CandidateNodeIds = [.. current.Select(n => n.Id)];
            return current;
        }
    }

    private static DirectedObservation[] GetIndependentEdges(
        ImmutableArray<Observation> observations,
        Guid contentId,
        Func<string, CatalogueNode[]> candidates
    ) =>
        observations
            .Where(o => o.ContentId != contentId)
            .SelectMany(o =>
                o.Prefix.Select(prefix =>
                        candidates(prefix) is [var only]
                            ? Option.Some(only.Key)
                            : Option<string>.None
                    )
                    .GetOrElse(() => Option.Some(o.ObserverKey))
                    .Where(to => o.FromKey != to)
                    .Select(to => new DirectedObservation(o, to))
                    .ToEnumerable()
            )
            .ToArray();

    internal static PathAssessmentStatus GetStatus(ImmutableArray<PathHopAssessment> hops) =>
        hops.IsEmpty ? PathAssessmentStatus.Empty
        : hops.All(h => h.Outcome == PathHopOutcome.Resolved) ? PathAssessmentStatus.Resolved
        : hops.Any(h => h.Outcome == PathHopOutcome.Resolved) ? PathAssessmentStatus.Partial
        : PathAssessmentStatus.Unresolved;

    private static string Fingerprint(ImmutableArray<CatalogueNode> nodes) =>
        Convert.ToHexStringLower(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    string.Join(
                        ";",
                        nodes
                            .OrderBy(n => n.Key, StringComparer.Ordinal)
                            .Select(n => $"{n.Id}:{n.Key}")
                    )
                )
            )
        );

    private async Task<AssessmentKnowledge> LoadKnowledgeAsync(CancellationToken cancellationToken)
    {
        var catalogue = database
            .MeshNodes.Where(n => n.Role == NodeRole.Repeater)
            .Select(n => new
            {
                IsCatalogue = true,
                Id = n.Id,
                Key = n.PublicKey,
                PacketId = (Guid?)null,
                ReceptionId = (Guid?)null,
                ContentId = (Guid?)null,
                ObserverKey = (byte[]?)null,
                Prefix = (byte[]?)null,
            });
        var admitted =
            from observation in database.NeighbourObservations
            join assessment in database.ReceptionPathAssessments
                on observation.SourceReceptionId equals assessment.ReceptionId
            join packet in database.MeshPackets on observation.SourcePacketId equals packet.Id
            join node in database.MeshNodes on observation.AdvertiserNodeId equals node.Id
            join observer in database.Observers on observation.ObserverId equals observer.Id
            join reception in database.MeshPacketReceptions
                on observation.SourceReceptionId equals reception.Id
            where
                assessment.AdmitsNeighbourEvidence
                && reception.Association != PacketAssociation.Unverified
            select new
            {
                IsCatalogue = false,
                Id = observation.Id,
                Key = node.PublicKey,
                PacketId = (Guid?)observation.SourcePacketId,
                ReceptionId = (Guid?)observation.SourceReceptionId,
                ContentId = (Guid?)packet.ContentId,
                ObserverKey = (byte[]?)observer.PublicKey,
                Prefix = observation.FirstHopPrefix,
            };
        var assessedAt = clock.GetCurrentInstant().ToDateTimeOffset();
        var rows = await catalogue.Concat(admitted).ToArrayAsync(cancellationToken);
        return new(
            assessedAt,
            [
                .. rows.Where(r => r.IsCatalogue)
                    .Select(r => new CatalogueNode(r.Id, Convert.ToHexStringLower(r.Key))),
            ],
            [
                .. rows.Where(r => !r.IsCatalogue)
                    .Select(r => new Observation(
                        r.Id,
                        r.PacketId!.Value,
                        r.ReceptionId!.Value,
                        r.ContentId!.Value,
                        Convert.ToHexStringLower(r.Key),
                        Convert.ToHexStringLower(r.ObserverKey!),
                        Option.FromNullable(r.Prefix).Select(Convert.ToHexStringLower)
                    )),
            ]
        );
    }

    private async Task<ImmutableArray<PathStep>> AttachPathAsync(
        ReceptionPacket packet,
        MeshCorePacket frame,
        ImmutableArray<string> prefixes,
        CancellationToken cancellationToken
    )
    {
        var steps = await database
            .PathSteps.Where(s => s.ContentId == packet.ContentId)
            .ToListAsync(cancellationToken);
        var existing = await database
            .PathHops.Where(h => h.PacketId == packet.PacketId)
            .ToListAsync(cancellationToken);
        var path = ImmutableArray.CreateBuilder<PathStep>(prefixes.Length);
        Guid? parent = null;
        for (short position = 0; position < prefixes.Length; position++)
        {
            var bytes = Convert.FromHexString(prefixes[position]);
            var step = steps.SingleOrDefault(s =>
                s.ParentStepId == parent
                && s.Width == frame.PathWidth
                && s.Prefix.AsSpan().SequenceEqual(bytes)
            );
            if (step is null)
            {
                step = new PathStep
                {
                    Id = GuidV7.Create(clock),
                    ContentId = packet.ContentId,
                    ParentStepId = parent,
                    Position = position,
                    Width = (short)frame.PathWidth,
                    Prefix = bytes,
                };
                steps.Add(step);
                database.PathSteps.Add(step);
            }
            if (!existing.Any(h => h.Position == position))
            {
                database.PathHops.Add(
                    new PacketPathHop
                    {
                        Id = GuidV7.Create(clock),
                        PacketId = packet.PacketId,
                        Position = position,
                        PathStepId = step.Id,
                    }
                );
            }
            path.Add(step);
            parent = step.Id;
        }
        var stored = await database.MeshPackets.SingleAsync(
            p => p.Id == packet.PacketId,
            cancellationToken
        );
        stored.LastPathStepId = parent;
        return path.ToImmutable();
    }

    private async Task SaveObservationAsync(
        MeshPacketReception reception,
        MeshCorePacket frame,
        string advertiser,
        CancellationToken cancellationToken
    )
    {
        var key = Convert.FromHexString(advertiser);
        var advertiserId = await database
            .MeshNodes.Where(n => n.PublicKey == key)
            .Select(n => n.Id)
            .SingleAsync(cancellationToken);
        if (
            !await database.NeighbourObservations.AnyAsync(
                o => o.SourcePacketId == reception.PacketId && o.ObserverId == reception.ObserverId,
                cancellationToken
            )
        )
        {
            database.NeighbourObservations.Add(
                new StoredNeighbourObservation
                {
                    Id = GuidV7.Create(clock),
                    SourcePacketId = reception.PacketId!.Value,
                    SourceReceptionId = reception.Id,
                    ObserverId = reception.ObserverId,
                    AdvertiserNodeId = advertiserId,
                    FirstHopPrefix =
                        frame.PathCount == 0
                            ? null
                            : frame.Path.AsSpan(..frame.PathWidth).ToArray(),
                    FirstHeardAt = reception.HeardAt,
                    LastHeardAt = reception.HeardAt,
                }
            );
        }
    }

    private static PathHopAssessment AssessHop(
        int position,
        int width,
        string prefix,
        CatalogueNode[][] sets,
        DirectedObservation[] edges,
        Option<string> advertiser,
        Option<string> observer
    )
    {
        var previous =
            position == 0 ? advertiser
            : sets[position - 1] is [var prev] ? Option.Some(prev.Key)
            : Option<string>.None;
        var next =
            position + 1 == sets.Length ? observer
            : sets[position + 1] is [var following] ? Option.Some(following.Key)
            : Option<string>.None;
        var candidates = sets[position]
            .Select(candidate =>
            {
                var supports = edges
                    .SelectMany(edge => SupportingSides(edge, previous, candidate.Key, next))
                    .OrderBy(s => s.ObservationId)
                    .ThenBy(s => s.Side)
                    .ToImmutableArray();
                var reasons = new List<PathCandidateReason> { PathCandidateReason.PrefixMatch };
                if (sets[position].Length == 1)
                {
                    reasons.Add(PathCandidateReason.UniqueKnownPrefixMatch);
                }
                if (!supports.IsEmpty)
                {
                    reasons.Add(PathCandidateReason.NeighbourSupport);
                }
                return new PathCandidateAssessment(
                    candidate.Id,
                    candidate.Key,
                    supports.IsEmpty
                        ? PathCandidateConfidence.Weak
                        : PathCandidateConfidence.Strong,
                    false,
                    [.. reasons],
                    supports
                );
            })
            .ToArray();
        var strong = candidates.Count(c => c.Confidence == PathCandidateConfidence.Strong);
        return new PathHopAssessment(
            position,
            width,
            prefix,
            candidates.Length == 0 ? PathHopOutcome.Unknown
                : strong == 1 ? PathHopOutcome.Resolved
                : candidates.Length == 1 ? PathHopOutcome.Unresolved
                : PathHopOutcome.Ambiguous,
            [
                .. candidates.Select(c =>
                    c with
                    {
                        Selected = strong == 1 && c.Confidence == PathCandidateConfidence.Strong,
                    }
                ),
            ]
        );
    }

    private static IEnumerable<PathNeighbourSupport> SupportingSides(
        DirectedObservation edge,
        Option<string> previous,
        string candidate,
        Option<string> next
    )
    {
        if (previous == Option.Some(edge.Source.FromKey) && candidate == edge.To)
        {
            yield return Support(PathNeighbourSupportSide.Incoming);
        }
        if (candidate == edge.Source.FromKey && next == Option.Some(edge.To))
        {
            yield return Support(PathNeighbourSupportSide.Outgoing);
        }
        PathNeighbourSupport Support(PathNeighbourSupportSide side) =>
            new(
                edge.Source.Id,
                edge.Source.PacketId,
                edge.Source.ReceptionId,
                edge.Source.ContentId,
                edge.Source.FromKey,
                edge.To,
                edge.Source.Prefix == Option<string>.None
                    ? PathNeighbourSupportBasis.AdvertToObserver
                    : PathNeighbourSupportBasis.AdvertToUniqueFirstForwarder,
                side
            );
    }

    internal static Option<string> VerifiedAdvertiser(MeshCorePacket frame) =>
        frame.PacketType != PacketType.Advert
            ? Option<string>.None
            : AdvertCodec
                .Decode(frame.Payload.AsSpan())
                .RightOrNone()
                .SelectMany(advert =>
                    AdvertCodec
                        .ValidateSignature(advert)
                        .RightOrNone()
                        .Select(_ => Convert.ToHexStringLower(advert.PublicKey.AsSpan()))
                );

    private record DirectedObservation(Observation Source, string To);

    private record ReceptionPacket(
        Guid PacketId,
        Guid ContentId,
        ImmutableArray<byte> RawPacket,
        string ObserverKey,
        bool HasRecordedAssociation
    );

    private record CatalogueNode(Guid Id, string Key);

    private record Observation(
        Guid Id,
        Guid PacketId,
        Guid ReceptionId,
        Guid ContentId,
        string FromKey,
        string ObserverKey,
        Option<string> Prefix
    );

    private record AssessmentKnowledge(
        DateTimeOffset AssessedAt,
        ImmutableArray<CatalogueNode> Nodes,
        ImmutableArray<Observation> Observations
    );
}
