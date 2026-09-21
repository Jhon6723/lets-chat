using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LetsChat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddContactEdges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contact_edges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    requester_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    addressee_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contact_edges", x => x.id);
                    table.ForeignKey(
                        name: "FK_contact_edges_accounts_addressee_account_id",
                        column: x => x.addressee_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_contact_edges_accounts_requester_account_id",
                        column: x => x.requester_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_contact_edges_addressee_account_id",
                table: "contact_edges",
                column: "addressee_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_contact_edges_requester_account_id_addressee_account_id",
                table: "contact_edges",
                columns: new[] { "requester_account_id", "addressee_account_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contact_edges");
        }
    }
}
