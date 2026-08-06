using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlphaChannel.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLalafellOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LodestoneCheckedAtUtc",
                table: "Accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LodestoneRaceMismatch",
                table: "Accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SelfReportedRaces",
                table: "Accounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WantsToSeeLalafellContent",
                table: "Accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LodestoneCheckedAtUtc",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "LodestoneRaceMismatch",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "SelfReportedRaces",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "WantsToSeeLalafellContent",
                table: "Accounts");
        }
    }
}
