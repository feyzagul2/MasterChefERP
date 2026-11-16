using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterChefTakip.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseDispatchNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PurchaseDispatchNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DocumentNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    SupplierDocumentNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    PurchaseOrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    SupplierId = table.Column<int>(type: "INTEGER", nullable: false),
                    DocumentDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseDispatchNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseDispatchNotes_CariAccounts_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "CariAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseDispatchNotes_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseDispatchLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PurchaseDispatchNoteId = table.Column<int>(type: "INTEGER", nullable: false),
                    PurchaseOrderLineId = table.Column<int>(type: "INTEGER", nullable: false),
                    LineNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductDescription = table.Column<string>(type: "TEXT", maxLength: 250, nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseDispatchLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseDispatchLines_PurchaseDispatchNotes_PurchaseDispatchNoteId",
                        column: x => x.PurchaseDispatchNoteId,
                        principalTable: "PurchaseDispatchNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PurchaseDispatchLines_PurchaseOrderLines_PurchaseOrderLineId",
                        column: x => x.PurchaseOrderLineId,
                        principalTable: "PurchaseOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseDispatchLines_PurchaseDispatchNoteId_LineNumber",
                table: "PurchaseDispatchLines",
                columns: new[] { "PurchaseDispatchNoteId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseDispatchLines_PurchaseOrderLineId",
                table: "PurchaseDispatchLines",
                column: "PurchaseOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseDispatchNotes_DocumentNumber",
                table: "PurchaseDispatchNotes",
                column: "DocumentNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseDispatchNotes_PurchaseOrderId",
                table: "PurchaseDispatchNotes",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseDispatchNotes_SupplierId",
                table: "PurchaseDispatchNotes",
                column: "SupplierId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PurchaseDispatchLines");

            migrationBuilder.DropTable(
                name: "PurchaseDispatchNotes");
        }
    }
}
