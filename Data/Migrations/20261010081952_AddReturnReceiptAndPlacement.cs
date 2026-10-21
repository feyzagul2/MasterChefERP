using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterChefTakip.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReturnReceiptAndPlacement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "InspectedGoodUnits",
                table: "ReturnInspections",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReceivedAdditionalUnitCount",
                table: "ReturnInspections",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReceivedBoxCount",
                table: "ReturnInspections",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReceivedUnitsPerBox",
                table: "ReturnInspections",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReturnReceiptId",
                table: "ReturnInspections",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReturnPlacements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RequestId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ReturnInspectionId = table.Column<int>(type: "INTEGER", nullable: false),
                    PalletId = table.Column<int>(type: "INTEGER", nullable: false),
                    BoxCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UnitsPerBox = table.Column<int>(type: "INTEGER", nullable: true),
                    LooseUnitCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PalletItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnPlacements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReturnPlacements_PalletItems_PalletItemId",
                        column: x => x.PalletItemId,
                        principalTable: "PalletItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnPlacements_Pallets_PalletId",
                        column: x => x.PalletId,
                        principalTable: "Pallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnPlacements_ReturnInspections_ReturnInspectionId",
                        column: x => x.ReturnInspectionId,
                        principalTable: "ReturnInspections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReturnReceipts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RequestId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ShipmentId = table.Column<int>(type: "INTEGER", nullable: false),
                    PackageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CancelledByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CancellationReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReturnReceipts_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReturnInspections_ReturnReceiptId_ShipmentItemId",
                table: "ReturnInspections",
                columns: new[] { "ReturnReceiptId", "ShipmentItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReturnPlacements_PalletId",
                table: "ReturnPlacements",
                column: "PalletId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnPlacements_PalletItemId",
                table: "ReturnPlacements",
                column: "PalletItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReturnPlacements_RequestId",
                table: "ReturnPlacements",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReturnPlacements_ReturnInspectionId",
                table: "ReturnPlacements",
                column: "ReturnInspectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnReceipts_RequestId",
                table: "ReturnReceipts",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReturnReceipts_ShipmentId",
                table: "ReturnReceipts",
                column: "ShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnReceipts_Status_CreatedAtUtc",
                table: "ReturnReceipts",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_ReturnInspections_ReturnReceipts_ReturnReceiptId",
                table: "ReturnInspections",
                column: "ReturnReceiptId",
                principalTable: "ReturnReceipts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReturnInspections_ReturnReceipts_ReturnReceiptId",
                table: "ReturnInspections");

            migrationBuilder.DropTable(
                name: "ReturnPlacements");

            migrationBuilder.DropTable(
                name: "ReturnReceipts");

            migrationBuilder.DropIndex(
                name: "IX_ReturnInspections_ReturnReceiptId_ShipmentItemId",
                table: "ReturnInspections");

            migrationBuilder.DropColumn(
                name: "InspectedGoodUnits",
                table: "ReturnInspections");

            migrationBuilder.DropColumn(
                name: "ReceivedAdditionalUnitCount",
                table: "ReturnInspections");

            migrationBuilder.DropColumn(
                name: "ReceivedBoxCount",
                table: "ReturnInspections");

            migrationBuilder.DropColumn(
                name: "ReceivedUnitsPerBox",
                table: "ReturnInspections");

            migrationBuilder.DropColumn(
                name: "ReturnReceiptId",
                table: "ReturnInspections");
        }
    }
}
