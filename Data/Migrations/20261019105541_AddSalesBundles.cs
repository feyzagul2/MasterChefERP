using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterChefTakip.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesBundles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SalesBundles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SalesCode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    SalesName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesBundles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SalesBundleComponents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SalesBundleId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    UnitsPerBundle = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesBundleComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesBundleComponents_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SalesBundleComponents_SalesBundles_SalesBundleId",
                        column: x => x.SalesBundleId,
                        principalTable: "SalesBundles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesBundleComponents_ProductId",
                table: "SalesBundleComponents",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesBundleComponents_SalesBundleId",
                table: "SalesBundleComponents",
                column: "SalesBundleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalesBundleComponents");

            migrationBuilder.DropTable(
                name: "SalesBundles");
        }
    }
}
