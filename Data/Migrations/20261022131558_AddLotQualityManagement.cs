using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterChefTakip.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLotQualityManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductLots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    LotNumber = table.Column<string>(type: "TEXT", maxLength: 70, nullable: false),
                    ProductionDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiryDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductLots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductLots_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductQualityInspections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductLotId = table.Column<int>(type: "INTEGER", nullable: false),
                    Result = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    InspectionName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    InspectedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    InspectedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductQualityInspections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductQualityInspections_ProductLots_ProductLotId",
                        column: x => x.ProductLotId,
                        principalTable: "ProductLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductLots_ExpiryDate",
                table: "ProductLots",
                column: "ExpiryDate");

            migrationBuilder.CreateIndex(
                name: "IX_ProductLots_ProductId_LotNumber",
                table: "ProductLots",
                columns: new[] { "ProductId", "LotNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductLots_Status",
                table: "ProductLots",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ProductQualityInspections_ProductLotId_InspectedAtUtc",
                table: "ProductQualityInspections",
                columns: new[] { "ProductLotId", "InspectedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductQualityInspections");

            migrationBuilder.DropTable(
                name: "ProductLots");
        }
    }
}
