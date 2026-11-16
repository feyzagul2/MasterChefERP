using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterChefTakip.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionPackagingLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductionPackagingLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PackagingBatchId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ProductionOrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductLotId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionPackagingLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionPackagingLinks_PackagingBatches_PackagingBatchId",
                        column: x => x.PackagingBatchId,
                        principalTable: "PackagingBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionPackagingLinks_ProductLots_ProductLotId",
                        column: x => x.ProductLotId,
                        principalTable: "ProductLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionPackagingLinks_ProductionOrders_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalTable: "ProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionPackagingLinks_PackagingBatchId",
                table: "ProductionPackagingLinks",
                column: "PackagingBatchId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionPackagingLinks_ProductionOrderId",
                table: "ProductionPackagingLinks",
                column: "ProductionOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionPackagingLinks_ProductLotId",
                table: "ProductionPackagingLinks",
                column: "ProductLotId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductionPackagingLinks");
        }
    }
}
