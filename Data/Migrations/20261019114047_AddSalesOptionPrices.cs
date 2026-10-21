using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterChefTakip.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesOptionPrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "SalesBundles",
                type: "TEXT",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SalesPriceVatExcluded",
                table: "SalesBundles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SalesPriceVatIncluded",
                table: "SalesBundles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VatRatePercent",
                table: "SalesBundles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "ProductSalesOptions",
                type: "TEXT",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SalesPriceVatExcluded",
                table: "ProductSalesOptions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SalesPriceVatIncluded",
                table: "ProductSalesOptions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VatRatePercent",
                table: "ProductSalesOptions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Currency",
                table: "SalesBundles");

            migrationBuilder.DropColumn(
                name: "SalesPriceVatExcluded",
                table: "SalesBundles");

            migrationBuilder.DropColumn(
                name: "SalesPriceVatIncluded",
                table: "SalesBundles");

            migrationBuilder.DropColumn(
                name: "VatRatePercent",
                table: "SalesBundles");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "ProductSalesOptions");

            migrationBuilder.DropColumn(
                name: "SalesPriceVatExcluded",
                table: "ProductSalesOptions");

            migrationBuilder.DropColumn(
                name: "SalesPriceVatIncluded",
                table: "ProductSalesOptions");

            migrationBuilder.DropColumn(
                name: "VatRatePercent",
                table: "ProductSalesOptions");
        }
    }
}
