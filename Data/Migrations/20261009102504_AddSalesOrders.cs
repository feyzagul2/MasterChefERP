using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterChefTakip.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeliveryAddress",
                table: "Shipments",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientPhone",
                table: "Shipments",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

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
                    Number = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CustomerName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    DeliveryAddress = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
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
                    SalesOrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ProductCode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false)
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Shipments_SalesOrders_SalesOrderId",
                table: "Shipments");

            migrationBuilder.DropTable(
                name: "SalesOrderItems");

            migrationBuilder.DropTable(
                name: "SalesOrders");

            migrationBuilder.DropIndex(
                name: "IX_Shipments_SalesOrderId",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "DeliveryAddress",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "RecipientPhone",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "SalesOrderId",
                table: "Shipments");
        }
    }
}
