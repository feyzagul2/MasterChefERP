using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterChefTakip.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderQuantityEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SalesOrderQuantityEntry",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SalesOrderItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    QuantityType = table.Column<int>(type: "INTEGER", nullable: false),
                    EnteredQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                    UnitsPerBox = table.Column<int>(type: "INTEGER", nullable: true),
                    TotalUnits = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderQuantityEntry", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesOrderQuantityEntry_SalesOrderItems_SalesOrderItemId",
                        column: x => x.SalesOrderItemId,
                        principalTable: "SalesOrderItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderQuantityEntry_SalesOrderItemId",
                table: "SalesOrderQuantityEntry",
                column: "SalesOrderItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalesOrderQuantityEntry");
        }
    }
}
