using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterChefTakip.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLocalOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"Shipments\" SET \"SalesOrderId\" = NULL WHERE \"SalesOrderId\" IS NOT NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_Shipments_SalesOrders_SalesOrderId",
                table: "Shipments");

            migrationBuilder.DropTable(
                name: "SalesOrderQuantityEntry");

            migrationBuilder.DropTable(
                name: "SalesOrderItems");

            migrationBuilder.DropTable(
                name: "SalesOrders");

            migrationBuilder.DropIndex(
                name: "IX_Shipments_SalesOrderId",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "SalesOrderId",
                table: "Shipments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SalesOrderId",
                table: "Shipments",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SalesOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CustomerName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DeliveryAddress = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Number = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrderItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    SalesOrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductCode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ProductName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesOrderItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderItems_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrderQuantityEntry",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SalesOrderItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    EnteredQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                    QuantityType = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalUnits = table.Column<int>(type: "INTEGER", nullable: false),
                    UnitsPerBox = table.Column<int>(type: "INTEGER", nullable: true)
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
                name: "IX_Shipments_SalesOrderId",
                table: "Shipments",
                column: "SalesOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderItems_ProductId",
                table: "SalesOrderItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderItems_SalesOrderId_ProductId",
                table: "SalesOrderItems",
                columns: new[] { "SalesOrderId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderQuantityEntry_SalesOrderItemId",
                table: "SalesOrderQuantityEntry",
                column: "SalesOrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_Number",
                table: "SalesOrders",
                column: "Number",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Shipments_SalesOrders_SalesOrderId",
                table: "Shipments",
                column: "SalesOrderId",
                principalTable: "SalesOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
