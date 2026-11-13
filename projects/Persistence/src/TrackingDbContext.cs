using Microsoft.EntityFrameworkCore;
using Pulsas.Persistence.Analytics;
using Pulsas.Persistence.Brokers;
using Pulsas.Persistence.Nodes;
using Pulsas.Persistence.Observers;
using Pulsas.Persistence.Packets;
using Pulsas.Persistence.Paths;
using Pulsas.Persistence.Receptions;
using Pulsas.Persistence.Regions;
using Pulsas.Persistence.Scopes;

namespace Pulsas.Persistence;

public class TrackingDbContext(DbContextOptions<TrackingDbContext> options) : DbContext(options)
{
    public DbSet<MeshPacket> MeshPackets => Set<MeshPacket>();
    public DbSet<StoredPacketContent> PacketContents => Set<StoredPacketContent>();
    public DbSet<PacketContentTag> PacketContentTags => Set<PacketContentTag>();
    public DbSet<PathStep> PathSteps => Set<PathStep>();
    public DbSet<PathStepCandidate> PathStepCandidates => Set<PathStepCandidate>();
    public DbSet<Chat.Channel> Channels => Set<Chat.Channel>();
    public DbSet<Chat.ChatMessage> ChatMessages => Set<Chat.ChatMessage>();
    public DbSet<MeshPacketReception> MeshPacketReceptions => Set<MeshPacketReception>();
    public DbSet<MeshNode> MeshNodes => Set<MeshNode>();
    public DbSet<MeshNodeRegionHearing> MeshNodeRegionHearings => Set<MeshNodeRegionHearing>();
    public DbSet<PacketPathHop> PathHops => Set<PacketPathHop>();
    public DbSet<PathHopCandidate> PathHopCandidates => Set<PathHopCandidate>();
    public DbSet<StoredNeighbourObservation> NeighbourObservations =>
        Set<StoredNeighbourObservation>();
    public DbSet<StoredCandidateNeighbourSupport> CandidateNeighbourSupports =>
        Set<StoredCandidateNeighbourSupport>();
    public DbSet<StoredObserver> Observers => Set<StoredObserver>();
    public DbSet<StoredBroker> Brokers => Set<StoredBroker>();
    public DbSet<StoredRegion> Regions => Set<StoredRegion>();
    public DbSet<StoredReceptionTypeCount> ReceptionTypeCounts => Set<StoredReceptionTypeCount>();
    public DbSet<StoredDailyReceptionTypeCount> DailyReceptionTypeCounts =>
        Set<StoredDailyReceptionTypeCount>();
    public DbSet<TransportScope> TransportScopes => Set<TransportScope>();
    public DbSet<PacketScopeCandidate> PacketScopeCandidates => Set<PacketScopeCandidate>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        var packet = model.Entity<MeshPacket>();
        packet.ToTable("packets", table =>
        {
            table.HasCheckConstraint(
                "ck_packet_hash_lengths",
                "octet_length(\"PacketHash\") = 32 AND octet_length(\"PayloadHash\") = 32"
            );
            table.HasCheckConstraint(
                "ck_packet_transport_codes",
                "\"TransportCodes\" IS NULL OR octet_length(\"TransportCodes\") = 4"
            );
            table.HasCheckConstraint(
                "ck_packet_path_prefix_width",
                "\"PathPrefixWidth\" BETWEEN 1 AND 4"
            );
            table.HasCheckConstraint("ck_packet_type", "\"Type\" BETWEEN 0 AND 15");
            table.HasCheckConstraint("ck_packet_route_type", "\"RouteType\" BETWEEN 0 AND 3");
            table.HasCheckConstraint(
                "ck_packet_short_hash",
                "\"ShortHash\" ~ '^[A-Za-z0-9_-]{8,43}$'"
            );
            table.HasCheckConstraint(
                "ck_packet_scope_selection",
                "(\"ScopeResolutionStatus\" = 'matched') = (\"ScopeId\" IS NOT NULL)"
            );
        });
        packet.HasKey(x => x.Id);
        packet.Property(x => x.Id).ValueGeneratedNever();
        packet
            .HasOne(x => x.Content)
            .WithMany()
            .HasForeignKey(x => x.ContentId)
            .OnDelete(DeleteBehavior.Restrict);
        packet
            .HasOne<PathStep>()
            .WithMany()
            .HasForeignKey(x => new { x.LastPathStepId, x.ContentId })
            .HasPrincipalKey(x => new { x.Id, x.ContentId })
            .OnDelete(DeleteBehavior.Restrict);
        packet.HasIndex(x => x.PacketHash).IsUnique().HasDatabaseName("ux_packets_hash");
        packet.Property(x => x.ShortHash).HasMaxLength(43).UseCollation("C");
        packet.HasIndex(x => x.ShortHash).IsUnique().HasDatabaseName("ux_packets_short_hash");
        packet.Property(x => x.Type).HasConversion<short>().HasColumnType("smallint");
        packet.Property(x => x.RouteType).HasConversion<short>().HasColumnType("smallint");
        packet.ComplexProperty(
            x => x.SignatureVerification,
            verification =>
            {
                verification.IsRequired();
                verification
                    .Property(x => x.Status)
                    .HasColumnName("SignatureStatus")
                    .HasColumnType("advert_signature_status");
                verification
                    .Property(x => x.FailureReason)
                    .HasColumnName("SignatureFailureReason")
                    .HasColumnType("advert_signature_error_kind");
                verification
                    .Property(x => x.CheckedAt)
                    .HasColumnName("SignatureCheckedAt")
                    .HasColumnType("timestamp with time zone");
            }
        );
        packet.Property(x => x.ScopeResolutionStatus).HasColumnType("scope_resolution_status");
        packet.HasIndex(x => new { x.Type, x.PayloadHash });
        packet.Property(x => x.DecodedPayload).HasColumnType("jsonb");
        packet
            .HasOne(x => x.ChatMessage)
            .WithMany()
            .HasForeignKey(x => x.ChatMessageId)
            .OnDelete(DeleteBehavior.Restrict);
        packet
            .HasOne(x => x.Scope)
            .WithMany()
            .HasForeignKey(x => x.ScopeId)
            .OnDelete(DeleteBehavior.Restrict);

        var content = model.Entity<StoredPacketContent>();
        content.ToTable("packet_contents", table =>
        {
            table.HasCheckConstraint("ck_content_hash", "octet_length(\"PayloadHash\") = 32");
            table.HasCheckConstraint(
                "ck_content_type_version",
                "\"Type\" BETWEEN 0 AND 15 AND \"PayloadVersion\" BETWEEN 0 AND 3"
            );
        });
        content.HasKey(x => x.Id);
        content.Property(x => x.Id).ValueGeneratedNever();
        content.Property(x => x.Type).HasConversion<short>().HasColumnType("smallint");
        content
            .HasIndex(x => new
            {
                x.Type,
                x.PayloadVersion,
                x.PayloadHash,
            })
            .IsUnique();
        content
            .HasMany(x => x.Tags)
            .WithOne()
            .HasForeignKey(x => x.ContentId)
            .OnDelete(DeleteBehavior.Cascade);
        var tag = model.Entity<PacketContentTag>();
        tag.ToTable("packet_content_tags", table =>
            table.HasCheckConstraint("ck_content_tag", "\"Tag\" ~ '^[a-z0-9][a-z0-9-]{0,63}$'")
        );
        tag.HasKey(x => new { x.ContentId, x.Tag });
        tag.Property(x => x.Tag).HasMaxLength(64).UseCollation("C");
        tag.HasIndex(x => new { x.Tag, x.ContentId });

        var step = model.Entity<PathStep>();
        step.ToTable("path_steps", table =>
        {
            table.HasCheckConstraint(
                "ck_step_prefix",
                "\"Width\" BETWEEN 1 AND 3 AND octet_length(\"Prefix\") = \"Width\""
            );
            table.HasCheckConstraint(
                "ck_step_position",
                "(\"ParentStepId\" IS NULL AND \"Position\" = 0) OR (\"ParentStepId\" IS NOT NULL AND \"Position\" > 0)"
            );
        });
        step.HasKey(x => x.Id);
        step.Property(x => x.Id).ValueGeneratedNever();
        step.HasOne<StoredPacketContent>()
            .WithMany()
            .HasForeignKey(x => x.ContentId)
            .OnDelete(DeleteBehavior.Restrict);
        step.HasOne<PathStep>()
            .WithMany()
            .HasForeignKey(x => new { x.ParentStepId, x.ContentId })
            .HasPrincipalKey(x => new { x.Id, x.ContentId })
            .OnDelete(DeleteBehavior.Restrict);
        step.HasIndex(x => new
            {
                x.ContentId,
                x.ParentStepId,
                x.Width,
                x.Prefix,
            })
            .IsUnique()
            .AreNullsDistinct(false);
        step.HasIndex(x => x.Prefix);
        var membership = model.Entity<PathStepCandidate>();
        membership.ToTable("path_step_candidates");
        membership.HasKey(x => new { x.PathStepId, x.MeshNodeId });
        membership
            .HasOne<PathStep>()
            .WithMany()
            .HasForeignKey(x => x.PathStepId)
            .OnDelete(DeleteBehavior.Restrict);
        membership
            .HasOne<MeshNode>()
            .WithMany()
            .HasForeignKey(x => x.MeshNodeId)
            .OnDelete(DeleteBehavior.Restrict);

        var channel = model.Entity<Chat.Channel>();
        channel.ToTable("channels", table =>
        {
            table.HasCheckConstraint(
                "ck_channel_identity",
                "(\"Kind\" = 'standard_public' AND \"Name\" = 'Public' AND \"ConfigurationId\" IS NULL) OR (\"Kind\" = 'hashtag' AND \"ConfigurationId\" IS NULL AND left(\"Name\", 1) = '#' AND length(\"Name\") > 1) OR (\"Kind\" = 'explicit_key' AND \"ConfigurationId\" IS NOT NULL AND length(btrim(\"ConfigurationId\")) > 0)"
            );
            table.HasCheckConstraint("ck_channel_name", "length(btrim(\"Name\")) > 0");
        });
        channel.HasKey(x => x.Id);
        channel.Property(x => x.Id).ValueGeneratedNever();
        channel.Property(x => x.Kind).HasColumnType("channel_kind");
        channel.Property(x => x.Name).UseCollation("C");
        channel.Property(x => x.ConfigurationId).UseCollation("C");
        channel.HasIndex(x => x.Kind).IsUnique().HasFilter("\"Kind\" = 'standard_public'");
        channel.HasIndex(x => x.Name).IsUnique().HasFilter("\"Kind\" = 'hashtag'");
        channel.HasIndex(x => x.ConfigurationId).IsUnique().HasFilter("\"Kind\" = 'explicit_key'");
        var message = model.Entity<Chat.ChatMessage>();
        message.ToTable("chat_messages", table =>
            table.HasCheckConstraint(
                "ck_message_payload_hash",
                "octet_length(\"PayloadHash\") = 32"
            )
        );
        message.HasKey(x => x.Id);
        message.Property(x => x.Id).ValueGeneratedNever();
        message.HasIndex(x => new { x.ChannelId, x.PayloadHash }).IsUnique();
        message
            .HasOne(x => x.Channel)
            .WithMany()
            .HasForeignKey(x => x.ChannelId)
            .OnDelete(DeleteBehavior.Restrict);

        var reception = model.Entity<MeshPacketReception>();
        reception.ToTable("packet_receptions", table =>
            table.HasCheckConstraint("ck_reception_region_code", "\"RegionCode\" ~ '^[A-Z]{3,4}$'")
        );
        reception.HasKey(x => x.Id);
        reception.Property(x => x.Id).ValueGeneratedNever();
        reception.Property(x => x.Association).HasColumnType("packet_association");
        reception.ComplexProperty(
            x => x.Signal,
            signal =>
            {
                signal.IsRequired();
                signal.Property(x => x.Rssi).HasColumnName("Rssi");
                signal.Property(x => x.Snr).HasColumnName("Snr");
            }
        );
        reception
            .HasOne(x => x.Packet)
            .WithMany()
            .HasForeignKey(x => x.PacketId)
            .OnDelete(DeleteBehavior.Restrict);
        reception
            .HasOne(x => x.Observer)
            .WithMany()
            .HasForeignKey(x => x.ObserverId)
            .OnDelete(DeleteBehavior.Restrict);
        reception
            .HasOne(x => x.Broker)
            .WithMany()
            .HasForeignKey(x => x.BrokerId)
            .OnDelete(DeleteBehavior.Restrict);
        reception
            .HasOne(x => x.Region)
            .WithMany()
            .HasForeignKey(x => x.RegionCode)
            .HasPrincipalKey(x => x.Code)
            .OnDelete(DeleteBehavior.Restrict);
        reception.HasIndex(x => new
        {
            x.PacketId,
            x.ArrivedAt,
            x.Id,
        });
        reception.HasIndex(x => new
        {
            x.ObserverId,
            x.ArrivedAt,
            x.Id,
        });
        reception.HasIndex(x => new { x.ArrivedAt, x.Id });

        var node = model.Entity<MeshNode>();
        node.ToTable("node_information", table =>
        {
            table.HasCheckConstraint(
                "ck_node_public_key_length",
                "octet_length(\"PublicKey\") = 32"
            );
            table.HasCheckConstraint(
                "ck_node_role",
                "\"Role\" IS NULL OR \"Role\" BETWEEN 0 AND 15"
            );
        });
        node.HasKey(x => x.Id);
        node.Property(x => x.Id).ValueGeneratedNever();
        node.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        node.Property(x => x.ModifiedAt).HasColumnType("timestamp with time zone");
        node.HasIndex(x => x.PublicKey).IsUnique();
        node.HasOne(x => x.LocationRegion)
            .WithMany()
            .HasForeignKey(x => x.LocationRegionId)
            .OnDelete(DeleteBehavior.Restrict);
        node.Property(x => x.Location).HasColumnType("geometry(Point,4326)");
        node.Property(x => x.Role).HasConversion<short>().HasColumnType("smallint");
        node.Property(x => x.SourceType).HasColumnType("mesh_node_source_type");
        node.Property(x => x.LocationRegionId).HasColumnName("LocationRegionId");
        node.HasOne(x => x.LatestAdvertPacket)
            .WithMany()
            .HasForeignKey(x => x.LatestAdvertPacketId)
            .OnDelete(DeleteBehavior.Restrict);

        var hearing = model.Entity<MeshNodeRegionHearing>();
        hearing.ToTable("mesh_node_region_hearings", table =>
            table.HasCheckConstraint(
                "ck_mesh_node_region_hearing_region_code",
                "\"RegionCode\" ~ '^[A-Z]{3,4}$'"
            )
        );
        hearing.HasKey(x => new { x.MeshNodeId, x.RegionCode });
        hearing
            .HasOne(x => x.MeshNode)
            .WithMany(x => x.HeardInRegions)
            .HasForeignKey(x => x.MeshNodeId)
            .OnDelete(DeleteBehavior.Cascade);
        hearing
            .HasOne(x => x.Region)
            .WithMany()
            .HasForeignKey(x => x.RegionCode)
            .HasPrincipalKey(x => x.Code)
            .OnDelete(DeleteBehavior.Restrict);
        hearing.Property(x => x.LastHeardAt).HasColumnType("timestamp with time zone");

        var segment = model.Entity<PacketPathHop>();
        segment.ToTable("path_segments", table =>
            table.HasCheckConstraint("ck_path_segment_position", "\"Position\" >= 0")
        );
        segment.HasKey(x => x.Id);
        segment.Property(x => x.Id).ValueGeneratedNever();
        segment
            .HasOne<MeshPacket>()
            .WithMany()
            .HasForeignKey(x => x.PacketId)
            .OnDelete(DeleteBehavior.Restrict);
        segment
            .HasOne(x => x.Step)
            .WithMany()
            .HasForeignKey(x => x.PathStepId)
            .OnDelete(DeleteBehavior.Restrict);
        segment.HasIndex(x => new { x.PacketId, x.Position }).IsUnique();
        var candidate = model.Entity<PathHopCandidate>();
        candidate.ToTable("path_segment_candidates");
        candidate.HasKey(x => x.Id);
        candidate.Property(x => x.Id).ValueGeneratedNever();
        candidate.Property(x => x.Confidence).HasColumnType("candidate_confidence");
        candidate.Property(x => x.Reasons).HasColumnType("candidate_reason[]");
        candidate.Property(x => x.PathHopId).HasColumnName("PathSegmentId");
        candidate.Property(x => x.MeshNodeId).HasColumnName("NodeInfoId");
        candidate
            .HasOne<PacketPathHop>()
            .WithMany()
            .HasForeignKey(x => new { x.PathHopId, x.PathStepId })
            .HasPrincipalKey(x => new { x.Id, x.PathStepId })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_path_segment_candidates_path_segments_PathSegmentId");
        candidate
            .HasOne(x => x.MeshNode)
            .WithMany()
            .HasForeignKey(x => x.MeshNodeId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_path_segment_candidates_node_information_NodeInfoId");
        candidate
            .HasIndex(x => new { x.PathHopId, x.MeshNodeId })
            .IsUnique()
            .HasDatabaseName("ux_candidate_segment_node");
        candidate
            .HasOne<PathStepCandidate>()
            .WithMany()
            .HasForeignKey(x => new { x.PathStepId, x.MeshNodeId })
            .OnDelete(DeleteBehavior.Restrict);

        var observation = model.Entity<StoredNeighbourObservation>();
        observation.ToTable("neighbour_observations", table =>
        {
            table.HasCheckConstraint(
                "ck_neighbour_prefix",
                "\"FirstHopPrefix\" IS NULL OR octet_length(\"FirstHopPrefix\") BETWEEN 1 AND 3"
            );
            table.HasCheckConstraint(
                "ck_neighbour_times",
                "\"FirstHeardAt\" IS NULL OR \"LastHeardAt\" IS NULL OR \"FirstHeardAt\" <= \"LastHeardAt\""
            );
        });
        observation.HasKey(x => x.Id);
        observation.Property(x => x.Id).ValueGeneratedNever();
        observation
            .HasOne<MeshPacket>()
            .WithMany()
            .HasForeignKey(x => x.SourcePacketId)
            .OnDelete(DeleteBehavior.Restrict);
        observation
            .HasOne<MeshPacketReception>()
            .WithMany()
            .HasForeignKey(x => x.SourceReceptionId)
            .OnDelete(DeleteBehavior.Restrict);
        observation
            .HasOne<StoredObserver>()
            .WithMany()
            .HasForeignKey(x => x.ObserverId)
            .OnDelete(DeleteBehavior.Restrict);
        observation
            .HasOne<MeshNode>()
            .WithMany()
            .HasForeignKey(x => x.AdvertiserNodeId)
            .OnDelete(DeleteBehavior.Restrict);
        observation.HasIndex(x => new { x.SourcePacketId, x.ObserverId }).IsUnique();
        observation.HasIndex(x => x.FirstHopPrefix);

        var support = model.Entity<StoredCandidateNeighbourSupport>();
        support.ToTable("candidate_neighbour_supports");
        support.HasKey(x => x.Id);
        support.Property(x => x.Id).ValueGeneratedNever();
        support
            .HasOne<PathHopCandidate>()
            .WithMany()
            .HasForeignKey(x => x.CandidateId)
            .OnDelete(DeleteBehavior.Cascade);
        support
            .HasOne<StoredNeighbourObservation>()
            .WithMany()
            .HasForeignKey(x => x.NeighbourObservationId)
            .OnDelete(DeleteBehavior.Restrict);
        support
            .HasOne<PacketPathHop>()
            .WithMany()
            .HasForeignKey(x => x.AnchorSegmentId)
            .OnDelete(DeleteBehavior.Restrict);
        support
            .HasOne<MeshPacketReception>()
            .WithMany()
            .HasForeignKey(x => x.TargetReceptionId)
            .OnDelete(DeleteBehavior.Restrict);
        support.HasIndex(x => x.NeighbourObservationId);

        var observer = model.Entity<StoredObserver>();
        observer.ToTable("observers", table =>
            table.HasCheckConstraint(
                "ck_observer_public_key_length",
                "octet_length(\"PublicKey\") = 32"
            )
        );
        observer.HasKey(x => x.Id);
        observer.Property(x => x.Id).ValueGeneratedNever();
        observer.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        observer.Property(x => x.ModifiedAt).HasColumnType("timestamp with time zone");
        observer.HasIndex(x => x.PublicKey).IsUnique().HasDatabaseName("ux_observers_key");
        var broker = model.Entity<StoredBroker>();
        broker.ToTable("brokers");
        broker.HasKey(x => x.Id);
        broker.Property(x => x.Id).ValueGeneratedNever();
        broker.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        broker.Property(x => x.ModifiedAt).HasColumnType("timestamp with time zone");
        broker.HasIndex(x => x.Endpoint).IsUnique().HasDatabaseName("ux_brokers_endpoint");
        var region = model.Entity<StoredRegion>();
        region.ToTable("regions", table =>
            table.HasCheckConstraint(
                "ck_region_codes",
                "\"Code\" ~ '^[A-Z]{3,4}$' AND \"CountryCode\" ~ '^[A-Z]{2}$'"
            )
        );
        region.HasKey(x => x.Id);
        region.Property(x => x.Id).ValueGeneratedNever();
        region.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        region.Property(x => x.ModifiedAt).HasColumnType("timestamp with time zone");
        region.HasAlternateKey(x => x.Code).HasName("ux_regions_code");
        region.Property(x => x.Shape).HasColumnType("geometry(MultiPolygon,4326)");

        var quarterly = model.Entity<StoredReceptionTypeCount>();
        quarterly.ToTable("reception_type_counts", table =>
        {
            table.HasCheckConstraint(
                "ck_reception_type_count_nonnegative",
                "\"ReceptionCount\" >= 0"
            );
            table.HasCheckConstraint(
                "ck_reception_type_count_quarter_hour_aligned",
                "extract(minute from \"BucketStart\" AT TIME ZONE 'UTC')::integer % 15 = 0 AND extract(second from \"BucketStart\" AT TIME ZONE 'UTC') = 0"
            );
            table.HasCheckConstraint(
                "ck_reception_type_count_type",
                "\"PacketType\" BETWEEN 0 AND 15"
            );
        });
        quarterly.HasKey(x => x.Id);
        quarterly.Property(x => x.Id).ValueGeneratedNever();
        quarterly.Property(x => x.PacketType).HasConversion<short>().HasColumnType("smallint");
        quarterly
            .HasOne<StoredObserver>()
            .WithMany()
            .HasForeignKey(x => x.ObserverId)
            .OnDelete(DeleteBehavior.Restrict);
        quarterly
            .HasOne<StoredRegion>()
            .WithMany()
            .HasForeignKey(x => x.RegionCode)
            .HasPrincipalKey(x => x.Code)
            .OnDelete(DeleteBehavior.Restrict);
        quarterly
            .HasIndex(x => new
            {
                x.BucketStart,
                x.ObserverId,
                x.RegionCode,
                x.PacketType,
            })
            .IsUnique();
        var daily = model.Entity<StoredDailyReceptionTypeCount>();
        daily.ToTable("daily_reception_type_counts", table =>
        {
            table.HasCheckConstraint(
                "ck_daily_reception_type_count_nonnegative",
                "\"ReceptionCount\" >= 0"
            );
            table.HasCheckConstraint(
                "ck_daily_reception_type_count_type",
                "\"PacketType\" BETWEEN 0 AND 15"
            );
            table.HasCheckConstraint(
                "ck_daily_reception_type_count_day_aligned",
                "extract(hour from \"DayStart\" AT TIME ZONE 'UTC') = 0 AND extract(minute from \"DayStart\" AT TIME ZONE 'UTC') = 0 AND extract(second from \"DayStart\" AT TIME ZONE 'UTC') = 0"
            );
        });
        daily.HasKey(x => x.Id);
        daily.Property(x => x.Id).ValueGeneratedNever();
        daily.Property(x => x.PacketType).HasConversion<short>().HasColumnType("smallint");
        daily
            .HasOne<StoredObserver>()
            .WithMany()
            .HasForeignKey(x => x.ObserverId)
            .OnDelete(DeleteBehavior.Restrict);
        daily
            .HasOne<StoredRegion>()
            .WithMany()
            .HasForeignKey(x => x.RegionCode)
            .HasPrincipalKey(x => x.Code)
            .OnDelete(DeleteBehavior.Restrict);
        daily
            .HasIndex(x => new
            {
                x.DayStart,
                x.ObserverId,
                x.RegionCode,
                x.PacketType,
            })
            .IsUnique();

        var scope = model.Entity<TransportScope>();
        scope.ToTable("transport_scopes", table =>
            table.HasCheckConstraint("ck_scope_name", "length(btrim(\"Name\")) > 0")
        );
        scope.HasKey(x => x.Id);
        scope.Property(x => x.Id).ValueGeneratedNever();
        scope.Property(x => x.Name).UseCollation("C");
        scope.HasIndex(x => x.Name).IsUnique();
        var scopeCandidate = model.Entity<PacketScopeCandidate>();
        scopeCandidate.ToTable("packet_scope_candidates");
        scopeCandidate.HasKey(x => new { x.PacketId, x.ScopeId });
        scopeCandidate
            .HasOne<MeshPacket>()
            .WithMany()
            .HasForeignKey(x => x.PacketId)
            .OnDelete(DeleteBehavior.Cascade);
        scopeCandidate
            .HasOne<TransportScope>()
            .WithMany()
            .HasForeignKey(x => x.ScopeId)
            .OnDelete(DeleteBehavior.Restrict);
        model.HasPostgresExtension("postgis");
    }
}
