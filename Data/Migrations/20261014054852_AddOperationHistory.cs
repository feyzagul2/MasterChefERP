using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterChefTakip.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HistoryEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventKey = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    UserName = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    ProductName = table.Column<string>(type: "TEXT", nullable: false),
                    ProductCode = table.Column<string>(type: "TEXT", nullable: false),
                    Quantity = table.Column<string>(type: "TEXT", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    BeforeValue = table.Column<string>(type: "TEXT", nullable: false),
                    AfterValue = table.Column<string>(type: "TEXT", nullable: false),
                    SearchText = table.Column<string>(type: "TEXT", nullable: false),
                    PalletId = table.Column<int>(type: "INTEGER", nullable: true),
                    ShipmentId = table.Column<int>(type: "INTEGER", nullable: true),
                    ReturnReceiptId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoryEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HistoryEntries_CreatedAtUtc",
                table: "HistoryEntries",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_HistoryEntries_EventKey",
                table: "HistoryEntries",
                column: "EventKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HistoryEntries_UserId_CreatedAtUtc",
                table: "HistoryEntries",
                columns: new[] { "UserId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HistoryEntries");
        }
    }
}
