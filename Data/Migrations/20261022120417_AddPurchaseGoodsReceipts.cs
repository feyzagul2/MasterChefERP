using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterChefTakip.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseGoodsReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PurchaseGoodsReceipts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReceiptNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    PurchaseDispatchNoteId = table.Column<int>(type: "INTEGER", nullable: false),
                    ReceiptDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseGoodsReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseGoodsReceipts_PurchaseDispatchNotes_PurchaseDispatchNoteId",
                        column: x => x.PurchaseDispatchNoteId,
                        principalTable: "PurchaseDispatchNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseGoodsReceiptLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PurchaseGoodsReceiptId = table.Column<int>(type: "INTEGER", nullable: false),
                    PurchaseDispatchLineId = table.Column<int>(type: "INTEGER", nullable: false),
                    LineNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductDescription = table.Column<string>(type: "TEXT", maxLength: 250, nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ReceivedQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    AcceptedQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    RejectedQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseGoodsReceiptLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseGoodsReceiptLines_PurchaseDispatchLines_PurchaseDispatchLineId",
                        column: x => x.PurchaseDispatchLineId,
                        principalTable: "PurchaseDispatchLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseGoodsReceiptLines_PurchaseGoodsReceipts_PurchaseGoodsReceiptId",
                        column: x => x.PurchaseGoodsReceiptId,
                        principalTable: "PurchaseGoodsReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseGoodsReceiptLines_PurchaseDispatchLineId",
                table: "PurchaseGoodsReceiptLines",
                column: "PurchaseDispatchLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseGoodsReceiptLines_PurchaseGoodsReceiptId_PurchaseDispatchLineId",
                table: "PurchaseGoodsReceiptLines",
                columns: new[] { "PurchaseGoodsReceiptId", "PurchaseDispatchLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseGoodsReceipts_PurchaseDispatchNoteId",
                table: "PurchaseGoodsReceipts",
                column: "PurchaseDispatchNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseGoodsReceipts_ReceiptNumber",
                table: "PurchaseGoodsReceipts",
                column: "ReceiptNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PurchaseGoodsReceiptLines");

            migrationBuilder.DropTable(
                name: "PurchaseGoodsReceipts");
        }
    }
}
