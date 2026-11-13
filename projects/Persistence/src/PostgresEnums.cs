using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Pulsas.MeshCore.Payloads.Adverts;
using Pulsas.PacketProcessing.Contracts.Chat;
using Pulsas.Persistence.Nodes;
using Pulsas.Persistence.Packets;
using Pulsas.Persistence.Paths;
using Pulsas.Persistence.Receptions;
using Pulsas.Persistence.Scopes;

namespace Pulsas.Persistence;

internal static class PostgresEnums
{
    // Npgsql 9+ configures both the EF model and its internally owned data source here.
    internal static void Configure(NpgsqlDbContextOptionsBuilder builder)
    {
        builder.MapEnum<PacketAssociation>("packet_association");
        builder.MapEnum<PacketCandidateConfidence>("candidate_confidence");
        builder.MapEnum<PacketCandidateReason>("candidate_reason");
        builder.MapEnum<MeshNodeSourceType>("mesh_node_source_type");
        builder.MapEnum<AdvertSignatureStatus>("advert_signature_status");
        builder.MapEnum<AdvertSignatureErrorKind>("advert_signature_error_kind");
        builder.MapEnum<ChannelKind>("channel_kind");
        builder.MapEnum<ScopeResolutionStatus>("scope_resolution_status");
    }
}
