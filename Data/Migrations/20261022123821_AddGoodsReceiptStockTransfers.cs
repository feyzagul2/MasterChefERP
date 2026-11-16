using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterChefTakip.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGoodsReceiptStockTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GoodsReceiptStockTransfers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PurchaseGoodsReceiptLineId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    PalletId = table.Column<int>(type: "INTEGER", nullable: false),
                    PalletItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    StockMovementId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoodsReceiptStockTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GoodsReceiptStockTransfers_PalletItems_PalletItemId",
                        column: x => x.PalletItemId,
                        principalTable: "PalletItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GoodsReceiptStockTransfers_Pallets_PalletId",
                        column: x => x.PalletId,
                        principalTable: "Pallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GoodsReceiptStockTransfers_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GoodsReceiptStockTransfers_PurchaseGoodsReceiptLines_PurchaseGoodsReceiptLineId",
                        column: x => x.PurchaseGoodsReceiptLineId,
                        principalTable: "PurchaseGoodsReceiptLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GoodsReceiptStockTransfers_StockMovements_StockMovementId",
                        column: x => x.StockMovementId,
                        principalTable: "StockMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptStockTransfers_PalletId",
                table: "GoodsReceiptStockTransfers",
                column: "PalletId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptStockTransfers_PalletItemId",
                table: "GoodsReceiptStockTransfers",
                column: "PalletItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptStockTransfers_ProductId",
                table: "GoodsReceiptStockTransfers",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptStockTransfers_PurchaseGoodsReceiptLineId",
                table: "GoodsReceiptStockTransfers",
                column: "PurchaseGoodsReceiptLineId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptStockTransfers_StockMovementId",
                table: "GoodsReceiptStockTransfers",
                column: "StockMovementId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GoodsReceiptStockTransfers");
        }
    }
}
