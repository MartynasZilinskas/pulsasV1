using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pulsas.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReceptionIdentityAndRegionIdentifiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(name: "ck_region_codes", table: "regions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reception_region_code",
                table: "packet_receptions"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_mesh_node_region_hearing_region_code",
                table: "mesh_node_region_hearings"
            );

            migrationBuilder.AddColumn<byte[]>(
                name: "ReceptionIdentity",
                table: "packet_receptions",
                type: "bytea",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "ReportTimestampClaim",
                table: "packet_receptions",
                type: "text",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "ReportTimestampStatus",
                table: "packet_receptions",
                type: "integer",
                nullable: true
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_region_codes",
                table: "regions",
                sql: "length(\"Code\") > 0 AND \"CountryCode\" ~ '^[A-Z]{2}$'"
            );

            migrationBuilder.CreateIndex(
                name: "ux_receptions_identity",
                table: "packet_receptions",
                column: "ReceptionIdentity",
                unique: true,
                filter: "\"ReceptionIdentity\" IS NOT NULL"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_reception_identity",
                table: "packet_receptions",
                sql: "octet_length(\"ReceptionIdentity\") = 32"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_reception_region_code",
                table: "packet_receptions",
                sql: "length(\"RegionCode\") > 0"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_mesh_node_region_hearing_region_code",
                table: "mesh_node_region_hearings",
                sql: "length(\"RegionCode\") > 0"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(name: "ck_region_codes", table: "regions");

            migrationBuilder.DropIndex(name: "ux_receptions_identity", table: "packet_receptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reception_identity",
                table: "packet_receptions"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_reception_region_code",
                table: "packet_receptions"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_mesh_node_region_hearing_region_code",
                table: "mesh_node_region_hearings"
            );

            migrationBuilder.DropColumn(name: "ReceptionIdentity", table: "packet_receptions");

            migrationBuilder.DropColumn(name: "ReportTimestampClaim", table: "packet_receptions");

            migrationBuilder.DropColumn(name: "ReportTimestampStatus", table: "packet_receptions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_region_codes",
                table: "regions",
                sql: "\"Code\" ~ '^[A-Z]{3,4}$' AND \"CountryCode\" ~ '^[A-Z]{2}$'"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_reception_region_code",
                table: "packet_receptions",
                sql: "\"RegionCode\" ~ '^[A-Z]{3,4}$'"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_mesh_node_region_hearing_region_code",
                table: "mesh_node_region_hearings",
                sql: "\"RegionCode\" ~ '^[A-Z]{3,4}$'"
            );
        }
    }
}
