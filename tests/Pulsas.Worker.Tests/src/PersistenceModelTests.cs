using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using NodaTime;
using Pulsas.MeshCore.Nodes;
using Pulsas.MeshCore.Packets;
using Pulsas.MeshCore.Payloads.Adverts;
using Pulsas.PacketProcessing.Contracts.Chat;
using Pulsas.Persistence;
using Pulsas.Persistence.Chat;
using Pulsas.Persistence.Nodes;
using Pulsas.Persistence.Packets;
using Pulsas.Persistence.Receptions;
using Pulsas.Persistence.Scopes;
using Xunit;

namespace Pulsas.Worker.Tests;

// No server is opened: exercise the real configured PostgreSQL provider/model.
public class PersistenceModelTests
{
    [Fact]
    public void Entity_ids_are_required_and_are_not_generated_by_EF()
    {
        using var db = CreateContext();
        var entities = db
            .Model.GetEntityTypes()
            .Where(entity => entity.FindProperty("Id") is not null)
            .ToArray();
        Assert.Equal(17, entities.Length);
        foreach (var entity in entities)
        {
            Assert.Equal(ValueGenerated.Never, entity.FindProperty("Id")!.ValueGenerated);
            Assert.True(
                entity.ClrType.GetProperty("Id")!.IsDefined(typeof(RequiredMemberAttribute), false)
            );
        }
        var id = GuidV7.Create(new FixedClock());
        var channel = new Channel { Id = id, Name = "Public" };
        db.Channels.Add(channel);
        Assert.Equal(id, channel.Id);
        Assert.False(db.Entry(channel).Property(c => c.Id).IsTemporary);
    }

    [Fact]
    public void EF_does_not_generate_a_fallback_id()
    {
        using var db = CreateContext();
        var channel = new Channel { Id = Guid.Empty, Name = "Public" };
        db.Channels.Add(channel);
        Assert.Equal(Guid.Empty, channel.Id);
    }

    private class FixedClock : IClock
    {
        public Instant GetCurrentInstant() => Instant.FromUtc(2026, 11, 12, 10, 0);
    }

    [Fact]
    public void New_models_do_not_claim_signature_verification_scope_resolution_or_publication()
    {
        var packet = new MeshPacket
        {
            Id = GuidV7.Create(new FixedClock()),
            PacketHash = new byte[32],
            PayloadHash = new byte[32],
            ShortHash = "example1",
            RawPacket = [1, 0, 1],
        };
        Assert.Equal(AdvertSignatureStatus.NotChecked, packet.SignatureVerification.Status);
        Assert.Null(packet.SignatureVerification.FailureReason);
        Assert.Null(packet.SignatureVerification.CheckedAt);
        Assert.Null(packet.ScopeId);
        Assert.Equal(ScopeResolutionStatus.NotAssessed, packet.ScopeResolutionStatus);
        var channel = new Channel { Id = GuidV7.Create(new FixedClock()), Name = "Public" };
        Assert.False(channel.Active);
        Assert.False(channel.Public);
        Assert.Equal(ChannelKind.Unknown, channel.Kind);
        Assert.Equal(
            MeshNodeSourceType.Unknown,
            new MeshNode
            {
                Id = GuidV7.Create(new FixedClock()),
                PublicKey = new byte[32],
            }.SourceType
        );
    }

    private static TrackingDbContext CreateContext() =>
        new(
            TrackingDatabase
                .Configure(
                    new DbContextOptionsBuilder<TrackingDbContext>(),
                    "Host=127.0.0.1;Port=1;Database=model_only;Username=model_only;Password=unused"
                )
                .Options
        );

    [Fact]
    public void Signature_verification_keeps_existing_columns_without_an_extra_entity()
    {
        using var db = CreateContext();
        var packet = db.Model.FindEntityType(typeof(MeshPacket))!;
        var verification = packet.FindComplexProperty(nameof(MeshPacket.SignatureVerification))!;
        Assert.False(verification.IsNullable);
        Assert.Null(db.Model.FindEntityType(typeof(AdvertSignatureVerification)));
        var table = StoreObjectIdentifier.Table("packets", null);
        foreach (
            var (member, column, storeType, nullable) in new[]
            {
                ("Status", "SignatureStatus", "advert_signature_status", false),
                ("FailureReason", "SignatureFailureReason", "advert_signature_error_kind", true),
                ("CheckedAt", "SignatureCheckedAt", "timestamp with time zone", true),
            }
        )
        {
            var property = verification.ComplexType.FindProperty(member)!;
            Assert.Equal(column, property.GetColumnName(table));
            Assert.Equal(storeType, property.GetColumnType());
            Assert.Equal(nullable, property.IsNullable);
            Assert.False(property.IsShadowProperty());
            Assert.Equal(ValueGenerated.Never, property.ValueGenerated);
            Assert.Null(property.GetDefaultValueSql());
        }
        var sql = db.Database.GenerateCreateScript();
        Assert.Contains("\"SignatureStatus\" advert_signature_status NOT NULL", sql);
        Assert.Contains("\"SignatureFailureReason\" advert_signature_error_kind", sql);
        Assert.Contains("\"SignatureCheckedAt\" timestamp with time zone", sql);
        Assert.DoesNotContain("SignatureVerification_", sql);
    }

    [Fact]
    public void Signature_verification_nested_filters_and_projections_translate_to_existing_columns()
    {
        using var db = CreateContext();
        var checkedAt = new FixedClock().GetCurrentInstant();
        var sql = db
            .MeshPackets.Where(packet =>
                packet.SignatureVerification.Status == AdvertSignatureStatus.InvalidSignature
                && packet.SignatureVerification.FailureReason
                    == AdvertSignatureErrorKind.SignatureMismatch
                && packet.SignatureVerification.CheckedAt >= checkedAt
            )
            .Select(packet => new
            {
                packet.SignatureVerification.Status,
                packet.SignatureVerification.FailureReason,
                packet.SignatureVerification.CheckedAt,
            })
            .ToQueryString();
        Assert.Contains("\"SignatureStatus\" = 'invalid_signature'", sql);
        Assert.Contains("\"SignatureFailureReason\" = 'signature_mismatch'", sql);
        Assert.Contains("\"SignatureCheckedAt\" >=", sql);
        Assert.DoesNotContain("JOIN", sql);
    }

    [Fact]
    public void Replacing_signature_verification_is_tracked_without_a_separate_entity()
    {
        using var db = CreateContext();
        var packet = new MeshPacket
        {
            Id = GuidV7.Create(new FixedClock()),
            PacketHash = new byte[32],
            PayloadHash = new byte[32],
            ShortHash = "example1",
            RawPacket = [1, 0, 1],
        };
        db.Attach(packet);
        var original = packet.SignatureVerification;
        var checkedAt = new FixedClock().GetCurrentInstant();
        packet.SignatureVerification = original with
        {
            Status = AdvertSignatureStatus.InvalidSignature,
            FailureReason = AdvertSignatureErrorKind.SignatureMismatch,
            CheckedAt = checkedAt,
        };
        db.ChangeTracker.DetectChanges();
        var entry = db.Entry(packet).ComplexProperty(x => x.SignatureVerification);
        Assert.True(entry.Property(x => x.Status).IsModified);
        Assert.True(entry.Property(x => x.FailureReason).IsModified);
        Assert.True(entry.Property(x => x.CheckedAt).IsModified);
        Assert.Equal(AdvertSignatureStatus.NotChecked, original.Status);
        Assert.Null(original.FailureReason);
        Assert.Null(original.CheckedAt);
        Assert.Single(db.ChangeTracker.Entries());
    }

    [Fact]
    public void Reception_signal_keeps_nullable_columns_and_arrival_indexes()
    {
        using var db = CreateContext();
        var reception = db.Model.FindEntityType(typeof(MeshPacketReception))!;
        var signal = reception.FindComplexProperty(nameof(MeshPacketReception.Signal))!;
        Assert.False(signal.IsNullable);
        Assert.Null(db.Model.FindEntityType(typeof(ReceptionSignal)));
        var table = StoreObjectIdentifier.Table("packet_receptions", null);
        foreach (
            var (member, storeType) in new[] { ("Rssi", "integer"), ("Snr", "double precision") }
        )
        {
            var property = signal.ComplexType.FindProperty(member)!;
            Assert.Equal(member, property.GetColumnName(table));
            Assert.Equal(storeType, property.GetColumnType());
            Assert.True(property.IsNullable);
            Assert.False(property.IsShadowProperty());
        }
        var indexes = reception
            .GetIndexes()
            .Select(index => string.Join(",", index.Properties.Select(p => p.Name)))
            .ToArray();
        Assert.Contains("PacketId,ArrivedAt,Id", indexes);
        Assert.Contains("ObserverId,ArrivedAt,Id", indexes);
        Assert.Contains("ArrivedAt,Id", indexes);
        var sql = db.Database.GenerateCreateScript();
        Assert.Contains("\"Rssi\" integer", sql);
        Assert.Contains("\"Snr\" double precision", sql);
        Assert.DoesNotContain("Signal_", sql);
    }

    [Fact]
    public void Reception_signal_nested_filters_and_projections_translate_to_existing_columns()
    {
        using var db = CreateContext();
        var sql = db
            .MeshPacketReceptions.Where(reception =>
                reception.Signal.Rssi >= -90 && reception.Signal.Snr > 0
            )
            .Select(reception => new { reception.Signal.Rssi, reception.Signal.Snr })
            .ToQueryString();
        Assert.Contains("\"Rssi\" >= -90", sql);
        Assert.Contains("\"Snr\" >", sql);
        Assert.DoesNotContain("JOIN", sql);
    }

    [Fact]
    public void Reception_signal_defaults_to_unknown_and_record_replacement_is_tracked()
    {
        using var db = CreateContext();
        var reception = new MeshPacketReception
        {
            Id = GuidV7.Create(new FixedClock()),
            RegionCode = "VNO",
        };
        Assert.Null(reception.Signal.Rssi);
        Assert.Null(reception.Signal.Snr);
        db.Attach(reception);
        var original = reception.Signal;
        reception.Signal = original with { Rssi = -72, Snr = 4.25 };
        db.ChangeTracker.DetectChanges();
        var entry = db.Entry(reception).ComplexProperty(x => x.Signal);
        Assert.True(entry.Property(x => x.Rssi).IsModified);
        Assert.True(entry.Property(x => x.Snr).IsModified);
        Assert.Null(original.Rssi);
        Assert.Null(original.Snr);
        Assert.Single(db.ChangeTracker.Entries());
        db.ChangeTracker.AcceptAllChanges();
        reception.Signal = new();
        db.ChangeTracker.DetectChanges();
        Assert.True(entry.Property(x => x.Rssi).IsModified);
        Assert.True(entry.Property(x => x.Snr).IsModified);
        Assert.Null(reception.Signal.Rssi);
        Assert.Null(reception.Signal.Snr);
    }

    [Fact]
    public void Configured_provider_generates_all_tables_native_enums_and_spatial_time_mappings()
    {
        using var db = CreateContext();
        var sql = db.Database.GenerateCreateScript();
        Assert.Contains("geometry(Point,4326)", sql);
        Assert.Contains("geometry(MultiPolygon,4326)", sql);
        Assert.Contains("timestamp with time zone", sql);
        Assert.Contains("candidate_reason[]", sql);
        Assert.Contains("CREATE TABLE packet_scope_candidates", sql);
        Assert.Contains("CREATE TABLE transport_scopes", sql);
        Assert.DoesNotContain("IdentityHash", sql);
        Assert.DoesNotContain("SignatureVerifierRevision", sql);
        Assert.DoesNotContain("\"Key\"", sql);
        Assert.DoesNotContain("scope_key_source", sql);
        Assert.DoesNotContain("\"KeySource\"", sql);
        Assert.All(db.Model.GetEntityTypes().SelectMany(e => e.GetProperties()), p =>
            Assert.False(
                p.IsShadowProperty(),
                $"Unexpected shadow property: {p.DeclaringType.Name}.{p.Name}"
            )
        );
    }

    [Fact]
    public void Wire_enum_values_roundtrip_through_smallint_including_unnamed_values()
    {
        using var db = CreateContext();
        foreach (
            var (type, name, values) in new[]
            {
                (
                    typeof(MeshPacket),
                    "Type",
                    new object[] { PacketType.Advert, (PacketType)12, (PacketType)14 }
                ),
                (
                    typeof(MeshPacket),
                    "RouteType",
                    new object[] { RouteType.TransportFlood, RouteType.TransportDirect }
                ),
                (
                    typeof(MeshNode),
                    "Role",
                    new object[] { (NodeRole)0, NodeRole.Client, (NodeRole)15 }
                ),
            }
        )
        {
            var property = db.Model.FindEntityType(type)!.FindProperty(name)!;
            Assert.Equal("smallint", property.GetColumnType());
            var converter = property.GetTypeMapping().Converter!;
            foreach (var value in values)
            {
                Assert.IsType<short>(converter.ConvertToProvider(value));
                Assert.Equal(
                    value,
                    converter.ConvertFromProvider(converter.ConvertToProvider(value))
                );
            }
        }
    }

    [Fact]
    public void Identity_indexes_and_scope_selection_keep_ambiguity_and_private_history_representable()
    {
        using var db = CreateContext();
        var model = db.GetService<IDesignTimeModel>().Model;
        var channel = model.FindEntityType(typeof(Channel))!;
        const string publicIdentity =
            "\"Kind\" = 'standard_public' AND \"Name\" = 'Public' AND \"ConfigurationId\" IS NULL";
        Assert.Contains(
            publicIdentity,
            channel.GetCheckConstraints().Single(c => c.Name == "ck_channel_identity").Sql
        );
        Assert.Contains(publicIdentity, db.Database.GenerateCreateScript());
        Assert.Equal(3, channel.GetIndexes().Count(i => i.IsUnique && i.GetFilter() is not null));
        Assert.All(channel.GetIndexes(), i => Assert.DoesNotContain("Active", i.GetFilter() ?? ""));
        Assert.Equal("C", channel.FindProperty("ConfigurationId")!.GetCollation());
        Assert.Empty(channel.GetDeclaredQueryFilters());
        var candidates = model.FindEntityType(typeof(PacketScopeCandidate))!;
        Assert.Equal(
            new[] { "PacketId", "ScopeId" },
            candidates.FindPrimaryKey()!.Properties.Select(p => p.Name)
        );
        Assert.DoesNotContain(candidates.GetIndexes(), i =>
            i.IsUnique && i.Properties.Count == 1 && i.Properties[0].Name == "PacketId"
        );
        var packet = model.FindEntityType(typeof(MeshPacket))!;
        Assert.Contains(packet.GetCheckConstraints(), c => c.Name == "ck_packet_scope_selection");
        Assert.Equal(
            DeleteBehavior.Restrict,
            packet
                .GetForeignKeys()
                .Single(f => f.Properties.Any(p => p.Name == "ScopeId"))
                .DeleteBehavior
        );
        Assert.True(packet.FindProperty("TransportCodes")!.IsNullable);
        Assert.Equal("bytea", packet.FindProperty("TransportCodes")!.GetColumnType());
    }
}
