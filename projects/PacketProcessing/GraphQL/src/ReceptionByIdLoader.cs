using Pulsas.PacketProcessing.Contracts.Paths;

namespace Pulsas.PacketProcessing.GraphQL;

internal static class ReceptionByIdLoader
{
    [DataLoader]
    public static async Task<Dictionary<Guid, MeshPacketReception>> GetReceptionByIdAsync(
        IReadOnlyList<Guid> ids,
        IReceptionReadManager reads,
        CancellationToken cancellationToken
    ) =>
        (await reads.ReadReceptionsByIdAsync(ids, cancellationToken)).ToDictionary(
            row => row.Id,
            row => new MeshPacketReception(row)
        );
}
