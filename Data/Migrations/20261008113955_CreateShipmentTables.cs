using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterChefTakip.Data.Migrations
{
    /// <inheritdoc />
    public partial class CreateShipmentTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Shipments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Number = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    RecipientName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CargoCompany = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    TrackingNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ConfirmedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shipments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShipmentItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ShipmentId = table.Column<int>(type: "INTEGER", nullable: false),
                    PalletItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    BoxCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LooseUnitCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UnitsPerBox = table.Column<int>(type: "INTEGER", nullable: true),
                    ProductName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ProductCode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    PalletBarcode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentItems_PalletItems_PalletItemId",
                        column: x => x.PalletItemId,
                        principalTable: "PalletItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShipmentItems_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentItems_PalletItemId",
                table: "ShipmentItems",
                column: "PalletItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentItems_ShipmentId",
                table: "ShipmentItems",
                column: "ShipmentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShipmentItems");

            migrationBuilder.DropTable(
                name: "Shipments");
        }
    }
}
