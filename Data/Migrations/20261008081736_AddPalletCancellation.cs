using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterChefTakip.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPalletCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReversesMovementId",
                table: "StockMovements",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "Pallets",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                table: "Pallets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelledByUserId",
                table: "Pallets",
                type: "TEXT",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCancelled",
                table: "Pallets",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ReversesMovementId",
                table: "StockMovements",
                column: "ReversesMovementId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockMovements_ReversesMovementId",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "ReversesMovementId",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "Pallets");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                table: "Pallets");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "Pallets");

            migrationBuilder.DropColumn(
                name: "IsCancelled",
                table: "Pallets");
        }
    }
}
