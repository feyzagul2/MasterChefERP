using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterChefTakip.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductionRecipes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: true),
                    BatchQuantityKg = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionRecipes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionRecipes_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrderNumber = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ProductionRecipeId = table.Column<int>(type: "INTEGER", nullable: false),
                    WarehouseId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlannedQuantityKg = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionOrders_ProductionRecipes_ProductionRecipeId",
                        column: x => x.ProductionRecipeId,
                        principalTable: "ProductionRecipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionOrders_RawMaterialWarehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "RawMaterialWarehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionRecipeLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductionRecipeId = table.Column<int>(type: "INTEGER", nullable: false),
                    RawMaterialId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionRecipeLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionRecipeLines_ProductionRecipes_ProductionRecipeId",
                        column: x => x.ProductionRecipeId,
                        principalTable: "ProductionRecipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductionRecipeLines_RawMaterials_RawMaterialId",
                        column: x => x.RawMaterialId,
                        principalTable: "RawMaterials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionConsumptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductionOrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    RawMaterialId = table.Column<int>(type: "INTEGER", nullable: false),
                    RawMaterialStockMovementId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionConsumptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionConsumptions_ProductionOrders_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalTable: "ProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionConsumptions_RawMaterialStockMovements_RawMaterialStockMovementId",
                        column: x => x.RawMaterialStockMovementId,
                        principalTable: "RawMaterialStockMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionConsumptions_RawMaterials_RawMaterialId",
                        column: x => x.RawMaterialId,
                        principalTable: "RawMaterials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionConsumptions_ProductionOrderId_RawMaterialId",
                table: "ProductionConsumptions",
                columns: new[] { "ProductionOrderId", "RawMaterialId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionConsumptions_RawMaterialId",
                table: "ProductionConsumptions",
                column: "RawMaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionConsumptions_RawMaterialStockMovementId",
                table: "ProductionConsumptions",
                column: "RawMaterialStockMovementId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_OrderNumber",
                table: "ProductionOrders",
                column: "OrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_ProductionRecipeId",
                table: "ProductionOrders",
                column: "ProductionRecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_WarehouseId",
                table: "ProductionOrders",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRecipeLines_ProductionRecipeId_RawMaterialId",
                table: "ProductionRecipeLines",
                columns: new[] { "ProductionRecipeId", "RawMaterialId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRecipeLines_RawMaterialId",
                table: "ProductionRecipeLines",
                column: "RawMaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRecipes_Code",
                table: "ProductionRecipes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRecipes_ProductId",
                table: "ProductionRecipes",
                column: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductionConsumptions");

            migrationBuilder.DropTable(
                name: "ProductionRecipeLines");

            migrationBuilder.DropTable(
                name: "ProductionOrders");

            migrationBuilder.DropTable(
                name: "ProductionRecipes");
        }
    }
}
