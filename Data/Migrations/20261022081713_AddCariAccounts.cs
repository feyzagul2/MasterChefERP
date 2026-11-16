using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterChefTakip.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCariAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CariAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    CompanyName = table.Column<string>(type: "TEXT", maxLength: 250, nullable: false),
                    IsCustomer = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSupplier = table.Column<bool>(type: "INTEGER", nullable: false),
                    ContactPerson = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    TaxOffice = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    TaxNumber = table.Column<string>(type: "TEXT", maxLength: 11, nullable: true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Address = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    City = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    District = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    PaymentTermDays = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CariAccounts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CariAccounts_Code",
                table: "CariAccounts",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CariAccounts_CompanyName",
                table: "CariAccounts",
                column: "CompanyName");

            migrationBuilder.CreateIndex(
                name: "IX_CariAccounts_TaxNumber",
                table: "CariAccounts",
                column: "TaxNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CariAccounts");
        }
    }
}
