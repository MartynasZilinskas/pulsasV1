using System.Collections.Immutable;

namespace Pulsas.PacketProcessing.GraphQL;

public record ReceptionPathCandidatePage(
    ImmutableArray<ReceptionPathCandidate> Items,
    int TotalCount,
    bool HasMore
);
