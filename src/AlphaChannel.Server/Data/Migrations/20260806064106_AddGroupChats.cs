using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlphaChannel.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupChats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DmConversations");

            migrationBuilder.DropColumn(
                name: "ReadAtUtc",
                table: "DmMessages");

            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                table: "DmMessages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "RecipientAccountId",
                table: "DmMessages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "ConversationMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    JoinedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastReadAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationMembers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Conversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IsGroup = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conversations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DmMessages_GroupId",
                table: "DmMessages",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_DmMessages_RecipientAccountId",
                table: "DmMessages",
                column: "RecipientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMembers_AccountId",
                table: "ConversationMembers",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMembers_ConversationId_AccountId",
                table: "ConversationMembers",
                columns: new[] { "ConversationId", "AccountId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConversationMembers");

            migrationBuilder.DropTable(
                name: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_DmMessages_GroupId",
                table: "DmMessages");

            migrationBuilder.DropIndex(
                name: "IX_DmMessages_RecipientAccountId",
                table: "DmMessages");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "DmMessages");

            migrationBuilder.DropColumn(
                name: "RecipientAccountId",
                table: "DmMessages");

            migrationBuilder.AddColumn<DateTime>(
                name: "ReadAtUtc",
                table: "DmMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DmConversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountAId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountBId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DmConversations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DmConversations_AccountAId_AccountBId",
                table: "DmConversations",
                columns: new[] { "AccountAId", "AccountBId" },
                unique: true);
        }
    }
}
