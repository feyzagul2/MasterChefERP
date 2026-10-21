using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterChefTakip.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStockMovementCorrection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CorrectsMovementId",
                table: "StockMovements",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_CorrectsMovementId",
                table: "StockMovements",
                column: "CorrectsMovementId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockMovements_CorrectsMovementId",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "CorrectsMovementId",
                table: "StockMovements");
        }
    }
}
