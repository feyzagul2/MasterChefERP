using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterChefTakip.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShipmentPackages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShipmentPackages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ShipmentId = table.Column<int>(type: "INTEGER", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentPackages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentPackages_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShipmentPackageItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ShipmentPackageId = table.Column<int>(type: "INTEGER", nullable: false),
                    ShipmentItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentPackageItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentPackageItems_ShipmentItems_ShipmentItemId",
                        column: x => x.ShipmentItemId,
                        principalTable: "ShipmentItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentPackageItems_ShipmentPackages_ShipmentPackageId",
                        column: x => x.ShipmentPackageId,
                        principalTable: "ShipmentPackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentPackageItems_ShipmentItemId",
                table: "ShipmentPackageItems",
                column: "ShipmentItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentPackageItems_ShipmentPackageId_ShipmentItemId",
                table: "ShipmentPackageItems",
                columns: new[] { "ShipmentPackageId", "ShipmentItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentPackages_ShipmentId_Number",
                table: "ShipmentPackages",
                columns: new[] { "ShipmentId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShipmentPackageItems");

            migrationBuilder.DropTable(
                name: "ShipmentPackages");
        }
    }
}
