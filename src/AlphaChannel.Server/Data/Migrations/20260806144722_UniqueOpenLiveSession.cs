using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlphaChannel.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class UniqueOpenLiveSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Close duplicate open sessions before the unique filter can be applied — keep the
            // earliest StartedAtUtc row live per account, end the rest.
            migrationBuilder.Sql(
                """
                UPDATE "LiveSessions" AS dup
                SET "EndedAtUtc" = NOW() AT TIME ZONE 'utc'
                FROM (
                    SELECT "Id",
                           ROW_NUMBER() OVER (PARTITION BY "AccountId" ORDER BY "StartedAtUtc", "Id") AS rn
                    FROM "LiveSessions"
                    WHERE "EndedAtUtc" IS NULL
                ) AS ranked
                WHERE dup."Id" = ranked."Id" AND ranked.rn > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_LiveSessions_AccountId",
                table: "LiveSessions",
                column: "AccountId",
                unique: true,
                filter: "\"EndedAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LiveSessions_AccountId",
                table: "LiveSessions");
        }
    }
}
