using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterChefTakip.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRawStockCountApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RawStockCountApprovals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RawStockCountId = table.Column<int>(type: "INTEGER", nullable: false),
                    Decision = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    StockMovementId = table.Column<int>(type: "INTEGER", nullable: true),
                    ApprovedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawStockCountApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RawStockCountApprovals_RawMaterialStockMovements_StockMovementId",
                        column: x => x.StockMovementId,
                        principalTable: "RawMaterialStockMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RawStockCountApprovals_RawStockCounts_RawStockCountId",
                        column: x => x.RawStockCountId,
                        principalTable: "RawStockCounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RawStockCountApprovals_RawStockCountId",
                table: "RawStockCountApprovals",
                column: "RawStockCountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawStockCountApprovals_StockMovementId",
                table: "RawStockCountApprovals",
                column: "StockMovementId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RawStockCountApprovals");
        }
    }
}
