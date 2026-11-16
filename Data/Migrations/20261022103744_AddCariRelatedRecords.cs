using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterChefTakip.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCariRelatedRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CariAddresses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CariAccountId = table.Column<int>(type: "INTEGER", nullable: false),
                    AddressType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    AddressLine = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    City = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    District = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    PostalCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Country = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ContactPerson = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CariAddresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CariAddresses_CariAccounts_CariAccountId",
                        column: x => x.CariAccountId,
                        principalTable: "CariAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CariAuditEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CariAccountId = table.Column<int>(type: "INTEGER", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ActorName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CariAuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CariAuditEvents_CariAccounts_CariAccountId",
                        column: x => x.CariAccountId,
                        principalTable: "CariAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CariBankAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CariAccountId = table.Column<int>(type: "INTEGER", nullable: false),
                    BankName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    BranchName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    AccountHolder = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Iban = table.Column<string>(type: "TEXT", maxLength: 34, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CariBankAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CariBankAccounts_CariAccounts_CariAccountId",
                        column: x => x.CariAccountId,
                        principalTable: "CariAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CariContacts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CariAccountId = table.Column<int>(type: "INTEGER", nullable: false),
                    FullName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    JobTitle = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CariContacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CariContacts_CariAccounts_CariAccountId",
                        column: x => x.CariAccountId,
                        principalTable: "CariAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CariAddresses_CariAccountId",
                table: "CariAddresses",
                column: "CariAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CariAuditEvents_CariAccountId_CreatedAtUtc",
                table: "CariAuditEvents",
                columns: new[] { "CariAccountId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CariBankAccounts_CariAccountId",
                table: "CariBankAccounts",
                column: "CariAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CariContacts_CariAccountId",
                table: "CariContacts",
                column: "CariAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CariAddresses");

            migrationBuilder.DropTable(
                name: "CariAuditEvents");

            migrationBuilder.DropTable(
                name: "CariBankAccounts");

            migrationBuilder.DropTable(
                name: "CariContacts");
        }
    }
}
