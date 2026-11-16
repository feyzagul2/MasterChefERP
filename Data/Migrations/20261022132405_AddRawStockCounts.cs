using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterChefTakip.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRawStockCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RawStockCounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CountNumber = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    RawMaterialId = table.Column<int>(type: "INTEGER", nullable: false),
                    WarehouseId = table.Column<int>(type: "INTEGER", nullable: false),
                    SystemQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    CountedQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Difference = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawStockCounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RawStockCounts_RawMaterialWarehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "RawMaterialWarehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RawStockCounts_RawMaterials_RawMaterialId",
                        column: x => x.RawMaterialId,
                        principalTable: "RawMaterials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RawStockCounts_CountNumber",
                table: "RawStockCounts",
                column: "CountNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawStockCounts_RawMaterialId",
                table: "RawStockCounts",
                column: "RawMaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_RawStockCounts_WarehouseId_RawMaterialId_CreatedAtUtc",
                table: "RawStockCounts",
                columns: new[] { "WarehouseId", "RawMaterialId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RawStockCounts");
        }
    }
}
