using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterChefTakip.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShipmentDeliveryTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveredAtUtc",
                table: "Shipments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveredByUserId",
                table: "Shipments",
                type: "TEXT",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveredToName",
                table: "Shipments",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryNotes",
                table: "Shipments",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ShippedAtUtc",
                table: "Shipments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippedByUserId",
                table: "Shipments",
                type: "TEXT",
                maxLength: 450,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliveredAtUtc",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "DeliveredByUserId",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "DeliveredToName",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "DeliveryNotes",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "ShippedAtUtc",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "ShippedByUserId",
                table: "Shipments");
        }
    }
}
