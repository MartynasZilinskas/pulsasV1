using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using NodaTime;
using Pulsas.MeshCore.Payloads.Adverts;
using Pulsas.PacketProcessing.Contracts.Chat;
using Pulsas.Persistence.Nodes;
using Pulsas.Persistence.Packets;
using Pulsas.Persistence.Paths;
using Pulsas.Persistence.Receptions;
using Pulsas.Persistence.Scopes;

#nullable disable

namespace Pulsas.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialTrackingSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder
                .AlterDatabase()
                .Annotation(
                    "Npgsql:Enum:advert_signature_error_kind",
                    "invalid_app_data_length,invalid_public_key_length,invalid_signature_length,public_key_import_rejected,signature_mismatch,uninitialized_app_data,uninitialized_public_key,uninitialized_signature,unknown"
                )
                .Annotation(
                    "Npgsql:Enum:advert_signature_status",
                    "invalid_signature,malformed,not_checked,verified"
                )
                .Annotation("Npgsql:Enum:candidate_confidence", "strong,unknown,weak")
                .Annotation(
                    "Npgsql:Enum:candidate_reason",
                    "neighbour_support,prefix_match,region_match,region_mismatch,unique_known_prefix_match,unknown"
                )
                .Annotation(
                    "Npgsql:Enum:channel_kind",
                    "explicit_key,hashtag,standard_public,unknown"
                )
                .Annotation(
                    "Npgsql:Enum:mesh_node_source_type",
                    "mesh_core_map,observed_advert,unknown"
                )
                .Annotation(
                    "Npgsql:Enum:packet_association",
                    "confirmed_fixture,offline_import,publisher_asserted_rx,unverified"
                )
                .Annotation(
                    "Npgsql:Enum:scope_resolution_status",
                    "ambiguous,matched,not_assessed,share,unresolved,unscoped,wildcard"
                )
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "brokers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Endpoint = table.Column<string>(type: "text", nullable: false),
                    ModifiedAt = table.Column<Instant>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    LastOnlineAt = table.Column<Instant>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    RecycledAt = table.Column<Instant>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brokers", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "channels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<ChannelKind>(type: "channel_kind", nullable: false),
                    ConfigurationId = table.Column<string>(
                        type: "text",
                        nullable: true,
                        collation: "C"
                    ),
                    Name = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    Public = table.Column<bool>(type: "boolean", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_channels", x => x.Id);
                    table.CheckConstraint(
                        "ck_channel_identity",
                        "(\"Kind\" = 'standard_public' AND \"Name\" = 'Public' AND \"ConfigurationId\" IS NULL) OR (\"Kind\" = 'hashtag' AND \"ConfigurationId\" IS NULL AND left(\"Name\", 1) = '#' AND length(\"Name\") > 1) OR (\"Kind\" = 'explicit_key' AND \"ConfigurationId\" IS NOT NULL AND length(btrim(\"ConfigurationId\")) > 0)"
                    );
                    table.CheckConstraint("ck_channel_name", "length(btrim(\"Name\")) > 0");
                }
            );

            migrationBuilder.CreateTable(
                name: "observers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    ModifiedAt = table.Column<Instant>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    PublicKey = table.Column<byte[]>(type: "bytea", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    FirmwareVersion = table.Column<string>(type: "text", nullable: true),
                    HardwareModel = table.Column<string>(type: "text", nullable: true),
                    LastOnlineAt = table.Column<Instant>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_observers", x => x.Id);
                    table.CheckConstraint(
                        "ck_observer_public_key_length",
                        "octet_length(\"PublicKey\") = 32"
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "packet_contents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<short>(type: "smallint", nullable: false),
                    PayloadVersion = table.Column<short>(type: "smallint", nullable: false),
                    PayloadHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    Payload = table.Column<byte[]>(type: "bytea", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_packet_contents", x => x.Id);
                    table.CheckConstraint("ck_content_hash", "octet_length(\"PayloadHash\") = 32");
                    table.CheckConstraint(
                        "ck_content_type_version",
                        "\"Type\" BETWEEN 0 AND 15 AND \"PayloadVersion\" BETWEEN 0 AND 3"
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "regions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    ModifiedAt = table.Column<Instant>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    CountryCode = table.Column<string>(type: "text", nullable: false),
                    Shape = table.Column<MultiPolygon>(
                        type: "geometry(MultiPolygon,4326)",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_regions", x => x.Id);
                    table.UniqueConstraint("ux_regions_code", x => x.Code);
                    table.CheckConstraint(
                        "ck_region_codes",
                        "\"Code\" ~ '^[A-Z]{3,4}$' AND \"CountryCode\" ~ '^[A-Z]{2}$'"
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "transport_scopes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false, collation: "C"),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transport_scopes", x => x.Id);
                    table.CheckConstraint("ck_scope_name", "length(btrim(\"Name\")) > 0");
                }
            );

            migrationBuilder.CreateTable(
                name: "chat_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    ClaimedSenderName = table.Column<string>(type: "text", nullable: true),
                    Text = table.Column<string>(type: "text", nullable: false),
                    SentAt = table.Column<Instant>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_messages", x => x.Id);
                    table.CheckConstraint(
                        "ck_message_payload_hash",
                        "octet_length(\"PayloadHash\") = 32"
                    );
                    table.ForeignKey(
                        name: "FK_chat_messages_channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "packet_content_tags",
                columns: table => new
                {
                    ContentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tag = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false,
                        collation: "C"
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_packet_content_tags", x => new { x.ContentId, x.Tag });
                    table.CheckConstraint(
                        "ck_content_tag",
                        "\"Tag\" ~ '^[a-z0-9][a-z0-9-]{0,63}$'"
                    );
                    table.ForeignKey(
                        name: "FK_packet_content_tags_packet_contents_ContentId",
                        column: x => x.ContentId,
                        principalTable: "packet_contents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "path_steps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentStepId = table.Column<Guid>(type: "uuid", nullable: true),
                    Position = table.Column<short>(type: "smallint", nullable: false),
                    Width = table.Column<short>(type: "smallint", nullable: false),
                    Prefix = table.Column<byte[]>(type: "bytea", nullable: false),
                    CandidatesInitialized = table.Column<bool>(type: "boolean", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_path_steps", x => x.Id);
                    table.UniqueConstraint("AK_path_steps_Id_ContentId", x => new
                    {
                        x.Id,
                        x.ContentId,
                    });
                    table.CheckConstraint(
                        "ck_step_position",
                        "(\"ParentStepId\" IS NULL AND \"Position\" = 0) OR (\"ParentStepId\" IS NOT NULL AND \"Position\" > 0)"
                    );
                    table.CheckConstraint(
                        "ck_step_prefix",
                        "\"Width\" BETWEEN 1 AND 3 AND octet_length(\"Prefix\") = \"Width\""
                    );
                    table.ForeignKey(
                        name: "FK_path_steps_packet_contents_ContentId",
                        column: x => x.ContentId,
                        principalTable: "packet_contents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_path_steps_path_steps_ParentStepId_ContentId",
                        columns: x => new { x.ParentStepId, x.ContentId },
                        principalTable: "path_steps",
                        principalColumns: new[] { "Id", "ContentId" },
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "daily_reception_type_counts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DayStart = table.Column<Instant>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    ObserverId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegionCode = table.Column<string>(type: "text", nullable: false),
                    PacketType = table.Column<short>(type: "smallint", nullable: false),
                    ReceptionCount = table.Column<long>(type: "bigint", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_reception_type_counts", x => x.Id);
                    table.CheckConstraint(
                        "ck_daily_reception_type_count_day_aligned",
                        "extract(hour from \"DayStart\" AT TIME ZONE 'UTC') = 0 AND extract(minute from \"DayStart\" AT TIME ZONE 'UTC') = 0 AND extract(second from \"DayStart\" AT TIME ZONE 'UTC') = 0"
                    );
                    table.CheckConstraint(
                        "ck_daily_reception_type_count_nonnegative",
                        "\"ReceptionCount\" >= 0"
                    );
                    table.CheckConstraint(
                        "ck_daily_reception_type_count_type",
                        "\"PacketType\" BETWEEN 0 AND 15"
                    );
                    table.ForeignKey(
                        name: "FK_daily_reception_type_counts_observers_ObserverId",
                        column: x => x.ObserverId,
                        principalTable: "observers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_daily_reception_type_counts_regions_RegionCode",
                        column: x => x.RegionCode,
                        principalTable: "regions",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "reception_type_counts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BucketStart = table.Column<Instant>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    ObserverId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegionCode = table.Column<string>(type: "text", nullable: false),
                    PacketType = table.Column<short>(type: "smallint", nullable: false),
                    ReceptionCount = table.Column<long>(type: "bigint", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reception_type_counts", x => x.Id);
                    table.CheckConstraint(
                        "ck_reception_type_count_nonnegative",
                        "\"ReceptionCount\" >= 0"
                    );
                    table.CheckConstraint(
                        "ck_reception_type_count_quarter_hour_aligned",
                        "extract(minute from \"BucketStart\" AT TIME ZONE 'UTC')::integer % 15 = 0 AND extract(second from \"BucketStart\" AT TIME ZONE 'UTC') = 0"
                    );
                    table.CheckConstraint(
                        "ck_reception_type_count_type",
                        "\"PacketType\" BETWEEN 0 AND 15"
                    );
                    table.ForeignKey(
                        name: "FK_reception_type_counts_observers_ObserverId",
                        column: x => x.ObserverId,
                        principalTable: "observers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_reception_type_counts_regions_RegionCode",
                        column: x => x.RegionCode,
                        principalTable: "regions",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "packets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastPathStepId = table.Column<Guid>(type: "uuid", nullable: true),
                    PacketHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    ShortHash = table.Column<string>(
                        type: "character varying(43)",
                        maxLength: 43,
                        nullable: false,
                        collation: "C"
                    ),
                    PayloadHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    Type = table.Column<short>(type: "smallint", nullable: false),
                    RouteType = table.Column<short>(type: "smallint", nullable: false),
                    TransportCodes = table.Column<byte[]>(type: "bytea", nullable: true),
                    PathPrefixWidth = table.Column<short>(type: "smallint", nullable: false),
                    RawPacket = table.Column<byte[]>(type: "bytea", nullable: false),
                    DecodedPayload = table.Column<string>(type: "jsonb", nullable: true),
                    DecoderRevision = table.Column<string>(type: "text", nullable: true),
                    ChatMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScopeResolutionStatus = table.Column<ScopeResolutionStatus>(
                        type: "scope_resolution_status",
                        nullable: false
                    ),
                    SignatureCheckedAt = table.Column<Instant>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    SignatureFailureReason = table.Column<AdvertSignatureErrorKind>(
                        type: "advert_signature_error_kind",
                        nullable: true
                    ),
                    SignatureStatus = table.Column<AdvertSignatureStatus>(
                        type: "advert_signature_status",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_packets", x => x.Id);
                    table.CheckConstraint(
                        "ck_packet_hash_lengths",
                        "octet_length(\"PacketHash\") = 32 AND octet_length(\"PayloadHash\") = 32"
                    );
                    table.CheckConstraint(
                        "ck_packet_path_prefix_width",
                        "\"PathPrefixWidth\" BETWEEN 1 AND 4"
                    );
                    table.CheckConstraint("ck_packet_route_type", "\"RouteType\" BETWEEN 0 AND 3");
                    table.CheckConstraint(
                        "ck_packet_scope_selection",
                        "(\"ScopeResolutionStatus\" = 'matched') = (\"ScopeId\" IS NOT NULL)"
                    );
                    table.CheckConstraint(
                        "ck_packet_short_hash",
                        "\"ShortHash\" ~ '^[A-Za-z0-9_-]{8,43}$'"
                    );
                    table.CheckConstraint(
                        "ck_packet_transport_codes",
                        "\"TransportCodes\" IS NULL OR octet_length(\"TransportCodes\") = 4"
                    );
                    table.CheckConstraint("ck_packet_type", "\"Type\" BETWEEN 0 AND 15");
                    table.ForeignKey(
                        name: "FK_packets_chat_messages_ChatMessageId",
                        column: x => x.ChatMessageId,
                        principalTable: "chat_messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_packets_packet_contents_ContentId",
                        column: x => x.ContentId,
                        principalTable: "packet_contents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_packets_path_steps_LastPathStepId_ContentId",
                        columns: x => new { x.LastPathStepId, x.ContentId },
                        principalTable: "path_steps",
                        principalColumns: new[] { "Id", "ContentId" },
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_packets_transport_scopes_ScopeId",
                        column: x => x.ScopeId,
                        principalTable: "transport_scopes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "node_information",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    ModifiedAt = table.Column<Instant>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    SourceType = table.Column<MeshNodeSourceType>(
                        type: "mesh_node_source_type",
                        nullable: false
                    ),
                    PublicKey = table.Column<byte[]>(type: "bytea", nullable: false),
                    LatestAdvertPacketId = table.Column<Guid>(type: "uuid", nullable: true),
                    LatestAdvertHeardAt = table.Column<Instant>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    LocationRegionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: true),
                    Role = table.Column<short>(type: "smallint", nullable: true),
                    Location = table.Column<Point>(type: "geometry(Point,4326)", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_node_information", x => x.Id);
                    table.CheckConstraint(
                        "ck_node_public_key_length",
                        "octet_length(\"PublicKey\") = 32"
                    );
                    table.CheckConstraint(
                        "ck_node_role",
                        "\"Role\" IS NULL OR \"Role\" BETWEEN 0 AND 15"
                    );
                    table.ForeignKey(
                        name: "FK_node_information_packets_LatestAdvertPacketId",
                        column: x => x.LatestAdvertPacketId,
                        principalTable: "packets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_node_information_regions_LocationRegionId",
                        column: x => x.LocationRegionId,
                        principalTable: "regions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "packet_receptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ObserverId = table.Column<Guid>(type: "uuid", nullable: false),
                    BrokerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PacketId = table.Column<Guid>(type: "uuid", nullable: true),
                    RegionCode = table.Column<string>(type: "text", nullable: false),
                    HeardAt = table.Column<Instant>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    ArrivedAt = table.Column<Instant>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    RawPacket = table.Column<byte[]>(type: "bytea", nullable: true),
                    DecodeError = table.Column<string>(type: "text", nullable: true),
                    Association = table.Column<PacketAssociation>(
                        type: "packet_association",
                        nullable: false
                    ),
                    Rssi = table.Column<int>(type: "integer", nullable: true),
                    Snr = table.Column<double>(type: "double precision", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_packet_receptions", x => x.Id);
                    table.CheckConstraint(
                        "ck_reception_region_code",
                        "\"RegionCode\" ~ '^[A-Z]{3,4}$'"
                    );
                    table.ForeignKey(
                        name: "FK_packet_receptions_brokers_BrokerId",
                        column: x => x.BrokerId,
                        principalTable: "brokers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_packet_receptions_observers_ObserverId",
                        column: x => x.ObserverId,
                        principalTable: "observers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_packet_receptions_packets_PacketId",
                        column: x => x.PacketId,
                        principalTable: "packets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_packet_receptions_regions_RegionCode",
                        column: x => x.RegionCode,
                        principalTable: "regions",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "packet_scope_candidates",
                columns: table => new
                {
                    PacketId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_packet_scope_candidates", x => new
                    {
                        x.PacketId,
                        x.ScopeId,
                    });
                    table.ForeignKey(
                        name: "FK_packet_scope_candidates_packets_PacketId",
                        column: x => x.PacketId,
                        principalTable: "packets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_packet_scope_candidates_transport_scopes_ScopeId",
                        column: x => x.ScopeId,
                        principalTable: "transport_scopes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "path_segments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PacketId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<short>(type: "smallint", nullable: false),
                    PathStepId = table.Column<Guid>(type: "uuid", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_path_segments", x => x.Id);
                    table.UniqueConstraint("AK_path_segments_Id_PathStepId", x => new
                    {
                        x.Id,
                        x.PathStepId,
                    });
                    table.CheckConstraint("ck_path_segment_position", "\"Position\" >= 0");
                    table.ForeignKey(
                        name: "FK_path_segments_packets_PacketId",
                        column: x => x.PacketId,
                        principalTable: "packets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_path_segments_path_steps_PathStepId",
                        column: x => x.PathStepId,
                        principalTable: "path_steps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "mesh_node_region_hearings",
                columns: table => new
                {
                    MeshNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegionCode = table.Column<string>(type: "text", nullable: false),
                    LastHeardAt = table.Column<Instant>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mesh_node_region_hearings", x => new
                    {
                        x.MeshNodeId,
                        x.RegionCode,
                    });
                    table.CheckConstraint(
                        "ck_mesh_node_region_hearing_region_code",
                        "\"RegionCode\" ~ '^[A-Z]{3,4}$'"
                    );
                    table.ForeignKey(
                        name: "FK_mesh_node_region_hearings_node_information_MeshNodeId",
                        column: x => x.MeshNodeId,
                        principalTable: "node_information",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_mesh_node_region_hearings_regions_RegionCode",
                        column: x => x.RegionCode,
                        principalTable: "regions",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "path_step_candidates",
                columns: table => new
                {
                    PathStepId = table.Column<Guid>(type: "uuid", nullable: false),
                    MeshNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_path_step_candidates", x => new
                    {
                        x.PathStepId,
                        x.MeshNodeId,
                    });
                    table.ForeignKey(
                        name: "FK_path_step_candidates_node_information_MeshNodeId",
                        column: x => x.MeshNodeId,
                        principalTable: "node_information",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_path_step_candidates_path_steps_PathStepId",
                        column: x => x.PathStepId,
                        principalTable: "path_steps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "neighbour_observations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcePacketId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceReceptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObserverId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdvertiserNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstHopPrefix = table.Column<byte[]>(type: "bytea", nullable: true),
                    FirstHeardAt = table.Column<Instant>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    LastHeardAt = table.Column<Instant>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_neighbour_observations", x => x.Id);
                    table.CheckConstraint(
                        "ck_neighbour_prefix",
                        "\"FirstHopPrefix\" IS NULL OR octet_length(\"FirstHopPrefix\") BETWEEN 1 AND 3"
                    );
                    table.CheckConstraint(
                        "ck_neighbour_times",
                        "\"FirstHeardAt\" IS NULL OR \"LastHeardAt\" IS NULL OR \"FirstHeardAt\" <= \"LastHeardAt\""
                    );
                    table.ForeignKey(
                        name: "FK_neighbour_observations_node_information_AdvertiserNodeId",
                        column: x => x.AdvertiserNodeId,
                        principalTable: "node_information",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_neighbour_observations_observers_ObserverId",
                        column: x => x.ObserverId,
                        principalTable: "observers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_neighbour_observations_packet_receptions_SourceReceptionId",
                        column: x => x.SourceReceptionId,
                        principalTable: "packet_receptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_neighbour_observations_packets_SourcePacketId",
                        column: x => x.SourcePacketId,
                        principalTable: "packets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "path_segment_candidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PathSegmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PathStepId = table.Column<Guid>(type: "uuid", nullable: false),
                    NodeInfoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Confidence = table.Column<PacketCandidateConfidence>(
                        type: "candidate_confidence",
                        nullable: false
                    ),
                    Reasons = table.Column<PacketCandidateReason[]>(
                        type: "candidate_reason[]",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_path_segment_candidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_path_segment_candidates_node_information_NodeInfoId",
                        column: x => x.NodeInfoId,
                        principalTable: "node_information",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_path_segment_candidates_path_segments_PathSegmentId",
                        columns: x => new { x.PathSegmentId, x.PathStepId },
                        principalTable: "path_segments",
                        principalColumns: new[] { "Id", "PathStepId" },
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_path_segment_candidates_path_step_candidates_PathStepId_Nod~",
                        columns: x => new { x.PathStepId, x.NodeInfoId },
                        principalTable: "path_step_candidates",
                        principalColumns: new[] { "PathStepId", "MeshNodeId" },
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "candidate_neighbour_supports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    NeighbourObservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnchorSegmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetReceptionId = table.Column<Guid>(type: "uuid", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_candidate_neighbour_supports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_candidate_neighbour_supports_neighbour_observations_Neighbo~",
                        column: x => x.NeighbourObservationId,
                        principalTable: "neighbour_observations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_candidate_neighbour_supports_packet_receptions_TargetRecept~",
                        column: x => x.TargetReceptionId,
                        principalTable: "packet_receptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_candidate_neighbour_supports_path_segment_candidates_Candid~",
                        column: x => x.CandidateId,
                        principalTable: "path_segment_candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_candidate_neighbour_supports_path_segments_AnchorSegmentId",
                        column: x => x.AnchorSegmentId,
                        principalTable: "path_segments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "ux_brokers_endpoint",
                table: "brokers",
                column: "Endpoint",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_candidate_neighbour_supports_AnchorSegmentId",
                table: "candidate_neighbour_supports",
                column: "AnchorSegmentId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_candidate_neighbour_supports_CandidateId",
                table: "candidate_neighbour_supports",
                column: "CandidateId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_candidate_neighbour_supports_NeighbourObservationId",
                table: "candidate_neighbour_supports",
                column: "NeighbourObservationId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_candidate_neighbour_supports_TargetReceptionId",
                table: "candidate_neighbour_supports",
                column: "TargetReceptionId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_channels_ConfigurationId",
                table: "channels",
                column: "ConfigurationId",
                unique: true,
                filter: "\"Kind\" = 'explicit_key'"
            );

            migrationBuilder.CreateIndex(
                name: "IX_channels_Kind",
                table: "channels",
                column: "Kind",
                unique: true,
                filter: "\"Kind\" = 'standard_public'"
            );

            migrationBuilder.CreateIndex(
                name: "IX_channels_Name",
                table: "channels",
                column: "Name",
                unique: true,
                filter: "\"Kind\" = 'hashtag'"
            );

            migrationBuilder.CreateIndex(
                name: "IX_chat_messages_ChannelId_PayloadHash",
                table: "chat_messages",
                columns: new[] { "ChannelId", "PayloadHash" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_daily_reception_type_counts_DayStart_ObserverId_RegionCode_~",
                table: "daily_reception_type_counts",
                columns: new[] { "DayStart", "ObserverId", "RegionCode", "PacketType" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_daily_reception_type_counts_ObserverId",
                table: "daily_reception_type_counts",
                column: "ObserverId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_daily_reception_type_counts_RegionCode",
                table: "daily_reception_type_counts",
                column: "RegionCode"
            );

            migrationBuilder.CreateIndex(
                name: "IX_mesh_node_region_hearings_RegionCode",
                table: "mesh_node_region_hearings",
                column: "RegionCode"
            );

            migrationBuilder.CreateIndex(
                name: "IX_neighbour_observations_AdvertiserNodeId",
                table: "neighbour_observations",
                column: "AdvertiserNodeId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_neighbour_observations_FirstHopPrefix",
                table: "neighbour_observations",
                column: "FirstHopPrefix"
            );

            migrationBuilder.CreateIndex(
                name: "IX_neighbour_observations_ObserverId",
                table: "neighbour_observations",
                column: "ObserverId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_neighbour_observations_SourcePacketId_ObserverId",
                table: "neighbour_observations",
                columns: new[] { "SourcePacketId", "ObserverId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_neighbour_observations_SourceReceptionId",
                table: "neighbour_observations",
                column: "SourceReceptionId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_node_information_LatestAdvertPacketId",
                table: "node_information",
                column: "LatestAdvertPacketId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_node_information_LocationRegionId",
                table: "node_information",
                column: "LocationRegionId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_node_information_PublicKey",
                table: "node_information",
                column: "PublicKey",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "ux_observers_key",
                table: "observers",
                column: "PublicKey",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_packet_content_tags_Tag_ContentId",
                table: "packet_content_tags",
                columns: new[] { "Tag", "ContentId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_packet_contents_Type_PayloadVersion_PayloadHash",
                table: "packet_contents",
                columns: new[] { "Type", "PayloadVersion", "PayloadHash" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_packet_receptions_ArrivedAt_Id",
                table: "packet_receptions",
                columns: new[] { "ArrivedAt", "Id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_packet_receptions_BrokerId",
                table: "packet_receptions",
                column: "BrokerId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_packet_receptions_ObserverId_ArrivedAt_Id",
                table: "packet_receptions",
                columns: new[] { "ObserverId", "ArrivedAt", "Id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_packet_receptions_PacketId_ArrivedAt_Id",
                table: "packet_receptions",
                columns: new[] { "PacketId", "ArrivedAt", "Id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_packet_receptions_RegionCode",
                table: "packet_receptions",
                column: "RegionCode"
            );

            migrationBuilder.CreateIndex(
                name: "IX_packet_scope_candidates_ScopeId",
                table: "packet_scope_candidates",
                column: "ScopeId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_packets_ChatMessageId",
                table: "packets",
                column: "ChatMessageId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_packets_ContentId",
                table: "packets",
                column: "ContentId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_packets_LastPathStepId_ContentId",
                table: "packets",
                columns: new[] { "LastPathStepId", "ContentId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_packets_ScopeId",
                table: "packets",
                column: "ScopeId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_packets_Type_PayloadHash",
                table: "packets",
                columns: new[] { "Type", "PayloadHash" }
            );

            migrationBuilder.CreateIndex(
                name: "ux_packets_hash",
                table: "packets",
                column: "PacketHash",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "ux_packets_short_hash",
                table: "packets",
                column: "ShortHash",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_path_segment_candidates_NodeInfoId",
                table: "path_segment_candidates",
                column: "NodeInfoId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_path_segment_candidates_PathSegmentId_PathStepId",
                table: "path_segment_candidates",
                columns: new[] { "PathSegmentId", "PathStepId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_path_segment_candidates_PathStepId_NodeInfoId",
                table: "path_segment_candidates",
                columns: new[] { "PathStepId", "NodeInfoId" }
            );

            migrationBuilder.CreateIndex(
                name: "ux_candidate_segment_node",
                table: "path_segment_candidates",
                columns: new[] { "PathSegmentId", "NodeInfoId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_path_segments_PacketId_Position",
                table: "path_segments",
                columns: new[] { "PacketId", "Position" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_path_segments_PathStepId",
                table: "path_segments",
                column: "PathStepId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_path_step_candidates_MeshNodeId",
                table: "path_step_candidates",
                column: "MeshNodeId"
            );

            migrationBuilder
                .CreateIndex(
                    name: "IX_path_steps_ContentId_ParentStepId_Width_Prefix",
                    table: "path_steps",
                    columns: new[] { "ContentId", "ParentStepId", "Width", "Prefix" },
                    unique: true
                )
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_path_steps_ParentStepId_ContentId",
                table: "path_steps",
                columns: new[] { "ParentStepId", "ContentId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_path_steps_Prefix",
                table: "path_steps",
                column: "Prefix"
            );

            migrationBuilder.CreateIndex(
                name: "IX_reception_type_counts_BucketStart_ObserverId_RegionCode_Pac~",
                table: "reception_type_counts",
                columns: new[] { "BucketStart", "ObserverId", "RegionCode", "PacketType" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_reception_type_counts_ObserverId",
                table: "reception_type_counts",
                column: "ObserverId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_reception_type_counts_RegionCode",
                table: "reception_type_counts",
                column: "RegionCode"
            );

            migrationBuilder.CreateIndex(
                name: "IX_transport_scopes_Name",
                table: "transport_scopes",
                column: "Name",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "candidate_neighbour_supports");

            migrationBuilder.DropTable(name: "daily_reception_type_counts");

            migrationBuilder.DropTable(name: "mesh_node_region_hearings");

            migrationBuilder.DropTable(name: "packet_content_tags");

            migrationBuilder.DropTable(name: "packet_scope_candidates");

            migrationBuilder.DropTable(name: "reception_type_counts");

            migrationBuilder.DropTable(name: "neighbour_observations");

            migrationBuilder.DropTable(name: "path_segment_candidates");

            migrationBuilder.DropTable(name: "packet_receptions");

            migrationBuilder.DropTable(name: "path_segments");

            migrationBuilder.DropTable(name: "path_step_candidates");

            migrationBuilder.DropTable(name: "brokers");

            migrationBuilder.DropTable(name: "observers");

            migrationBuilder.DropTable(name: "node_information");

            migrationBuilder.DropTable(name: "packets");

            migrationBuilder.DropTable(name: "regions");

            migrationBuilder.DropTable(name: "chat_messages");

            migrationBuilder.DropTable(name: "path_steps");

            migrationBuilder.DropTable(name: "transport_scopes");

            migrationBuilder.DropTable(name: "channels");

            migrationBuilder.DropTable(name: "packet_contents");
        }
    }
}
