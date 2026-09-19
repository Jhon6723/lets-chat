using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LetsChat.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialPendingEnvelopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pending_envelopes",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    recipient_address = table.Column<string>(type: "text", nullable: false),
                    sender_address = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    created_at_ms = table.Column<long>(type: "bigint", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pending_envelopes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pending_envelopes_recipient_address",
                table: "pending_envelopes",
                column: "recipient_address");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pending_envelopes");
        }
    }
}
