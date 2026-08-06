using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlphaChannel.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVenues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Venues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    TerritoryTypeId = table.Column<int>(type: "integer", nullable: false),
                    ScreenX = table.Column<float>(type: "real", nullable: false),
                    ScreenY = table.Column<float>(type: "real", nullable: false),
                    ScreenZ = table.Column<float>(type: "real", nullable: false),
                    ScreenYaw = table.Column<float>(type: "real", nullable: false),
                    ScreenScale = table.Column<float>(type: "real", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Venues", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Venues_OwnerAccountId",
                table: "Venues",
                column: "OwnerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Venues_TerritoryTypeId",
                table: "Venues",
                column: "TerritoryTypeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Venues");
        }
    }
}
