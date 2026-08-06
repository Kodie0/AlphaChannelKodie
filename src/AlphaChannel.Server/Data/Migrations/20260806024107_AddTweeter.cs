using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlphaChannel.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTweeter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReportedPostId",
                table: "Reports",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Follows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FollowerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    FolloweeAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Follows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PostLikes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostLikes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Posts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Posts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Follows_FolloweeAccountId",
                table: "Follows",
                column: "FolloweeAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Follows_FollowerAccountId_FolloweeAccountId",
                table: "Follows",
                columns: new[] { "FollowerAccountId", "FolloweeAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PostLikes_PostId_AccountId",
                table: "PostLikes",
                columns: new[] { "PostId", "AccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Posts_AuthorAccountId_CreatedAtUtc",
                table: "Posts",
                columns: new[] { "AuthorAccountId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Follows");

            migrationBuilder.DropTable(
                name: "PostLikes");

            migrationBuilder.DropTable(
                name: "Posts");

            migrationBuilder.DropColumn(
                name: "ReportedPostId",
                table: "Reports");
        }
    }
}
