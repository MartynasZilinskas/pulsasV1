using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pulsas.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdvertMetadataTimestamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AdvertMetadataTimestamp",
                table: "node_information",
                type: "bigint",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AdvertMetadataTimestamp", table: "node_information");
        }
    }
}
