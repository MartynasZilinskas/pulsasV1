using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Pulsas.MeshCore.Nodes;
using Pulsas.MeshCore.Packets;
using Pulsas.MeshCore.Payloads.Acks;
using Pulsas.MeshCore.Payloads.Adverts;
using Pulsas.MeshCore.Payloads.Control;
using Pulsas.MeshCore.Payloads.GroupText;
using Pulsas.MeshCore.Payloads.Multipart;
using Pulsas.PacketProcessing.Contracts.Chat;
using Pulsas.PacketProcessing.Contracts.Reports;
using Pulsas.Persistence;
using Pulsas.Persistence.Nodes;
using Pulsas.Persistence.Paths;
using Pulsas.Persistence.Receptions;
using Pulsas.Persistence.Scopes;
using Xunit;
using ProcessingSignatureStatus = Pulsas.PacketProcessing.Contracts.Processing.AdvertSignatureStatus;
using StoredSignatureStatus = Pulsas.Persistence.Packets.AdvertSignatureStatus;

namespace Pulsas.Worker.Tests;

public class EnumContractTests
{
    [Theory]
    [InlineData(typeof(ChannelKind), nameof(ChannelKind.Unknown))]
    [InlineData(typeof(ProcessingSignatureStatus), nameof(ProcessingSignatureStatus.NotChecked))]
    [InlineData(typeof(StoredSignatureStatus), nameof(StoredSignatureStatus.NotChecked))]
    [InlineData(typeof(ReportError), nameof(ReportError.UninitializedReport))]
    [InlineData(typeof(ReportTimestampStatus), nameof(ReportTimestampStatus.Missing))]
    [InlineData(typeof(MeshNodeSourceType), nameof(MeshNodeSourceType.Unknown))]
    [InlineData(typeof(PacketCandidateConfidence), nameof(PacketCandidateConfidence.Unknown))]
    [InlineData(typeof(PacketCandidateReason), nameof(PacketCandidateReason.Unknown))]
    [InlineData(typeof(PacketAssociation), nameof(PacketAssociation.Unverified))]
    [InlineData(typeof(ScopeResolutionStatus), nameof(ScopeResolutionStatus.NotAssessed))]
    [InlineData(typeof(PacketDecodeErrorKind), nameof(PacketDecodeErrorKind.Unknown))]
    [InlineData(typeof(AckErrorKind), nameof(AckErrorKind.Unknown))]
    [InlineData(typeof(ControlEncodeErrorKind), nameof(ControlEncodeErrorKind.Unknown))]
    [InlineData(typeof(MultipartEncodeErrorKind), nameof(MultipartEncodeErrorKind.Unknown))]
    [InlineData(typeof(AdvertSignatureErrorKind), nameof(AdvertSignatureErrorKind.Unknown))]
    [InlineData(typeof(AdvertEncodeErrorKind), nameof(AdvertEncodeErrorKind.Unknown))]
    [InlineData(typeof(GroupTextDecodeErrorKind), nameof(GroupTextDecodeErrorKind.Unknown))]
    [InlineData(typeof(GroupTextEncodeErrorKind), nameof(GroupTextEncodeErrorKind.Unknown))]
    [InlineData(typeof(AdvertAppDataDecodeErrorKind), nameof(AdvertAppDataDecodeErrorKind.Unknown))]
    [InlineData(typeof(AdvertAppDataEncodeErrorKind), nameof(AdvertAppDataEncodeErrorKind.Unknown))]
    public void Application_enum_default_is_uninitialized(Type type, string expected)
    {
        Assert.Equal(expected, Enum.GetName(type, Activator.CreateInstance(type)!));
    }

    [Theory]
    [InlineData(
        typeof(PacketType),
        "Request,Response,Text,Ack,Advert,GroupText,GroupData,AnonymousRequest,Path,Trace,Multipart,Control,RawCustom",
        new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 15 }
    )]
    [InlineData(
        typeof(RouteType),
        "TransportFlood,Flood,Direct,TransportDirect",
        new int[] { 0, 1, 2, 3 }
    )]
    [InlineData(typeof(NodeRole), "Client,Repeater,RoomServer,Sensor", new int[] { 1, 2, 3, 4 })]
    [InlineData(typeof(ControlSubtype), "DiscoveryRequest,DiscoveryResponse", new int[] { 8, 9 })]
    public void MeshCore_wire_values_remain_exact(Type type, string names, int[] values)
    {
        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(type));
        Assert.Equal(names.Split(','), Enum.GetNames(type));
        Assert.Equal(values, Enum.GetValues(type).Cast<object>().Select(Convert.ToInt32));
    }

    [Fact]
    public void PostgreSQL_native_enum_labels_match_application_states()
    {
        using var db = new TrackingDbContext(
            TrackingDatabase
                .Configure(
                    new DbContextOptionsBuilder<TrackingDbContext>(),
                    "Host=127.0.0.1;Port=1;Database=model_only;Username=model_only;Password=unused"
                )
                .Options
        );
        var sql = db.Database.GenerateCreateScript();
        foreach (
            var (name, labels) in new[]
            {
                (
                    "packet_association",
                    "unverified,confirmed_fixture,offline_import,publisher_asserted_rx"
                ),
                ("candidate_confidence", "unknown,weak,strong"),
                (
                    "candidate_reason",
                    "unknown,prefix_match,unique_known_prefix_match,region_match,region_mismatch,neighbour_support"
                ),
                ("mesh_node_source_type", "unknown,mesh_core_map,observed_advert"),
                ("advert_signature_status", "not_checked,malformed,invalid_signature,verified"),
                (
                    "advert_signature_error_kind",
                    "unknown,uninitialized_public_key,invalid_public_key_length,uninitialized_signature,invalid_signature_length,uninitialized_app_data,invalid_app_data_length,public_key_import_rejected,signature_mismatch"
                ),
                ("channel_kind", "unknown,standard_public,hashtag,explicit_key"),
                (
                    "scope_resolution_status",
                    "not_assessed,unscoped,wildcard,matched,unresolved,ambiguous,share"
                ),
            }
        )
        {
            var declaration = Regex.Match(sql, $@"CREATE TYPE {name} AS ENUM \(([^)]*)\);");
            Assert.True(declaration.Success, $"Missing PostgreSQL enum {name}.");
            var actual = declaration
                .Groups[1]
                .Value.Split(',')
                .Select(label => label.Trim().Trim('\''));
            Assert.Equal(labels.Split(',').Order(), actual.Order());
        }
    }
}
