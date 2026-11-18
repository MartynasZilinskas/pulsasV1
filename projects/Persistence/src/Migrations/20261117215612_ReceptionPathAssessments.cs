using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Pulsas.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReceptionPathAssessments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CandidateCatalogueFingerprint",
                table: "path_steps",
                type: "text",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid[]>(
                name: "CandidateNodeIds",
                table: "path_steps",
                type: "uuid[]",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "reception_path_assessments",
                columns: table => new
                {
                    ReceptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssessedAt = table.Column<Instant>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    PolicyVersion = table.Column<string>(type: "text", nullable: false),
                    AdmitsNeighbourEvidence = table.Column<bool>(type: "boolean", nullable: false),
                    Result = table.Column<string>(type: "jsonb", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reception_path_assessments", x => x.ReceptionId);
                    table.ForeignKey(
                        name: "FK_reception_path_assessments_packet_receptions_ReceptionId",
                        column: x => x.ReceptionId,
                        principalTable: "packet_receptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "reception_path_assessments");

            migrationBuilder.DropColumn(name: "CandidateCatalogueFingerprint", table: "path_steps");

            migrationBuilder.DropColumn(name: "CandidateNodeIds", table: "path_steps");
        }
    }
}
