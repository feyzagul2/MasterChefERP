using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterChefTakip.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReturnInspections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReturnInspections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RequestId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ShipmentItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    ReceivedUnits = table.Column<int>(type: "INTEGER", nullable: false),
                    UnitsPerBox = table.Column<int>(type: "INTEGER", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    GoodBoxCount = table.Column<int>(type: "INTEGER", nullable: false),
                    GoodUnitCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DamagedUnitCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DestinationPalletId = table.Column<int>(type: "INTEGER", nullable: true),
                    InspectionNotes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    InspectedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    InspectedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnInspections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReturnInspections_Pallets_DestinationPalletId",
                        column: x => x.DestinationPalletId,
                        principalTable: "Pallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnInspections_ShipmentItems_ShipmentItemId",
                        column: x => x.ShipmentItemId,
                        principalTable: "ShipmentItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReturnInspections_DestinationPalletId",
                table: "ReturnInspections",
                column: "DestinationPalletId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnInspections_RequestId",
                table: "ReturnInspections",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReturnInspections_ShipmentItemId",
                table: "ReturnInspections",
                column: "ShipmentItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnInspections_Status_CreatedAtUtc",
                table: "ReturnInspections",
                columns: new[] { "Status", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReturnInspections");
        }
    }
}
