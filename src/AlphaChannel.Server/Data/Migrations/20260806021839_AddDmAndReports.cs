using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlphaChannel.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDmAndReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FrankingKeyBase64",
                table: "Reports",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FrankingVerified",
                table: "Reports",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevealedBody",
                table: "Reports",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "CommitmentTag",
                table: "DmMessages",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "Tag",
                table: "DmMessages",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FrankingKeyBase64",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "FrankingVerified",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "RevealedBody",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "CommitmentTag",
                table: "DmMessages");

            migrationBuilder.DropColumn(
                name: "Tag",
                table: "DmMessages");
        }
    }
}
