using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterChefTakip.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShipmentCancellationAndLowStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RelatedShipmentId",
                table: "StockMovements",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "Shipments",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                table: "Shipments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelledByUserId",
                table: "Shipments",
                type: "TEXT",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AfterStockStamp",
                table: "ShipmentItems",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BeforeBoxCount",
                table: "ShipmentItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BeforeLooseUnitCount",
                table: "ShipmentItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BeforeOpenBoxCapacity",
                table: "ShipmentItems",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BeforeOpenBoxUnitCount",
                table: "ShipmentItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "HasStockSnapshot",
                table: "ShipmentItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LowStockThresholdUnits",
                table: "Products",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RelatedShipmentId",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "AfterStockStamp",
                table: "ShipmentItems");

            migrationBuilder.DropColumn(
                name: "BeforeBoxCount",
                table: "ShipmentItems");

            migrationBuilder.DropColumn(
                name: "BeforeLooseUnitCount",
                table: "ShipmentItems");

            migrationBuilder.DropColumn(
                name: "BeforeOpenBoxCapacity",
                table: "ShipmentItems");

            migrationBuilder.DropColumn(
                name: "BeforeOpenBoxUnitCount",
                table: "ShipmentItems");

            migrationBuilder.DropColumn(
                name: "HasStockSnapshot",
                table: "ShipmentItems");

            migrationBuilder.DropColumn(
                name: "LowStockThresholdUnits",
                table: "Products");
        }
    }
}
