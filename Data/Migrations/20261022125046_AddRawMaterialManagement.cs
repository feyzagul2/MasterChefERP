using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterChefTakip.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRawMaterialManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RawMaterials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    MinimumStock = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawMaterials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RawMaterialWarehouses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Location = table.Column<string>(type: "TEXT", maxLength: 250, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawMaterialWarehouses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RawMaterialStockMovements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RawMaterialId = table.Column<int>(type: "INTEGER", nullable: false),
                    WarehouseId = table.Column<int>(type: "INTEGER", nullable: false),
                    MovementType = table.Column<string>(type: "TEXT", maxLength: 15, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    OperationNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    ReferenceNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawMaterialStockMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RawMaterialStockMovements_RawMaterialWarehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "RawMaterialWarehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RawMaterialStockMovements_RawMaterials_RawMaterialId",
                        column: x => x.RawMaterialId,
                        principalTable: "RawMaterials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterials_Code",
                table: "RawMaterials",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterialStockMovements_OperationNumber",
                table: "RawMaterialStockMovements",
                column: "OperationNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterialStockMovements_RawMaterialId_WarehouseId_CreatedAtUtc",
                table: "RawMaterialStockMovements",
                columns: new[] { "RawMaterialId", "WarehouseId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterialStockMovements_WarehouseId",
                table: "RawMaterialStockMovements",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterialWarehouses_Code",
                table: "RawMaterialWarehouses",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RawMaterialStockMovements");

            migrationBuilder.DropTable(
                name: "RawMaterialWarehouses");

            migrationBuilder.DropTable(
                name: "RawMaterials");
        }
    }
}
