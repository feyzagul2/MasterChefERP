using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterChefTakip.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFlexiblePackaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShipmentItems_PalletItems_PalletItemId",
                table: "ShipmentItems");

            migrationBuilder.DropForeignKey(
                name: "FK_ShipmentItems_Shipments_ShipmentId",
                table: "ShipmentItems");

            migrationBuilder.DropIndex(
                name: "IX_ShipmentItems_ShipmentId",
                table: "ShipmentItems");

            migrationBuilder.DropIndex(
                name: "IX_PalletItems_PalletId_ProductId",
                table: "PalletItems");

            migrationBuilder.AddColumn<string>(
                name: "PackagingBatchId",
                table: "StockMovements",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PackagingBatches",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackagingBatches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_PackagingBatchId",
                table: "StockMovements",
                column: "PackagingBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_Number",
                table: "Shipments",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_Status_CreatedAtUtc",
                table: "Shipments",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentItems_ShipmentId_PalletItemId",
                table: "ShipmentItems",
                columns: new[] { "ShipmentId", "PalletItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PalletItems_PalletId_ProductId",
                table: "PalletItems",
                columns: new[] { "PalletId", "ProductId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ShipmentItems_PalletItems_PalletItemId",
                table: "ShipmentItems",
                column: "PalletItemId",
                principalTable: "PalletItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ShipmentItems_Shipments_ShipmentId",
                table: "ShipmentItems",
                column: "ShipmentId",
                principalTable: "Shipments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_PackagingBatches_PackagingBatchId",
                table: "StockMovements",
                column: "PackagingBatchId",
                principalTable: "PackagingBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShipmentItems_PalletItems_PalletItemId",
                table: "ShipmentItems");

            migrationBuilder.DropForeignKey(
                name: "FK_ShipmentItems_Shipments_ShipmentId",
                table: "ShipmentItems");

            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_PackagingBatches_PackagingBatchId",
                table: "StockMovements");

            migrationBuilder.DropTable(
                name: "PackagingBatches");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_PackagingBatchId",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_Shipments_Number",
                table: "Shipments");

            migrationBuilder.DropIndex(
                name: "IX_Shipments_Status_CreatedAtUtc",
                table: "Shipments");

            migrationBuilder.DropIndex(
                name: "IX_ShipmentItems_ShipmentId_PalletItemId",
                table: "ShipmentItems");

            migrationBuilder.DropIndex(
                name: "IX_PalletItems_PalletId_ProductId",
                table: "PalletItems");

            migrationBuilder.DropColumn(
                name: "PackagingBatchId",
                table: "StockMovements");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentItems_ShipmentId",
                table: "ShipmentItems",
                column: "ShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PalletItems_PalletId_ProductId",
                table: "PalletItems",
                columns: new[] { "PalletId", "ProductId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ShipmentItems_PalletItems_PalletItemId",
                table: "ShipmentItems",
                column: "PalletItemId",
                principalTable: "PalletItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ShipmentItems_Shipments_ShipmentId",
                table: "ShipmentItems",
                column: "ShipmentId",
                principalTable: "Shipments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
