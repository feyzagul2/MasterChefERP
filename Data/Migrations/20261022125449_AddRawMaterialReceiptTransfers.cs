using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterChefTakip.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRawMaterialReceiptTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RawMaterialReceiptTransfers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PurchaseGoodsReceiptLineId = table.Column<int>(type: "INTEGER", nullable: false),
                    RawMaterialId = table.Column<int>(type: "INTEGER", nullable: false),
                    WarehouseId = table.Column<int>(type: "INTEGER", nullable: false),
                    RawMaterialStockMovementId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawMaterialReceiptTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RawMaterialReceiptTransfers_PurchaseGoodsReceiptLines_PurchaseGoodsReceiptLineId",
                        column: x => x.PurchaseGoodsReceiptLineId,
                        principalTable: "PurchaseGoodsReceiptLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RawMaterialReceiptTransfers_RawMaterialStockMovements_RawMaterialStockMovementId",
                        column: x => x.RawMaterialStockMovementId,
                        principalTable: "RawMaterialStockMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RawMaterialReceiptTransfers_RawMaterialWarehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "RawMaterialWarehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RawMaterialReceiptTransfers_RawMaterials_RawMaterialId",
                        column: x => x.RawMaterialId,
                        principalTable: "RawMaterials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterialReceiptTransfers_PurchaseGoodsReceiptLineId",
                table: "RawMaterialReceiptTransfers",
                column: "PurchaseGoodsReceiptLineId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterialReceiptTransfers_RawMaterialId",
                table: "RawMaterialReceiptTransfers",
                column: "RawMaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterialReceiptTransfers_RawMaterialStockMovementId",
                table: "RawMaterialReceiptTransfers",
                column: "RawMaterialStockMovementId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterialReceiptTransfers_WarehouseId",
                table: "RawMaterialReceiptTransfers",
                column: "WarehouseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RawMaterialReceiptTransfers");
        }
    }
}
