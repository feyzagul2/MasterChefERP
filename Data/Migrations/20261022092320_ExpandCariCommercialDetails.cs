using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterChefTakip.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExpandCariCommercialDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccountGroup",
                table: "CariAccounts",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CreditLimit",
                table: "CariAccounts",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultDiscountPercent",
                table: "CariAccounts",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "DefaultDispatchNote",
                table: "CariAccounts",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultInvoiceNote",
                table: "CariAccounts",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultWarehouse",
                table: "CariAccounts",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryInstructions",
                table: "CariAccounts",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryMethod",
                table: "CariAccounts",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EDocumentType",
                table: "CariAccounts",
                type: "TEXT",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "InvoiceAddress",
                table: "CariAccounts",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceEmail",
                table: "CariAccounts",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsVatExempt",
                table: "CariAccounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "CariAccounts",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentTerms",
                table: "CariAccounts",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreferredCarrier",
                table: "CariAccounts",
                type: "TEXT",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PriceListName",
                table: "CariAccounts",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RiskStatus",
                table: "CariAccounts",
                type: "TEXT",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SalesRepresentative",
                table: "CariAccounts",
                type: "TEXT",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VatExemptionReason",
                table: "CariAccounts",
                type: "TEXT",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccountGroup",
                table: "CariAccounts");

            migrationBuilder.DropColumn(
                name: "CreditLimit",
                table: "CariAccounts");

            migrationBuilder.DropColumn(
                name: "DefaultDiscountPercent",
                table: "CariAccounts");

            migrationBuilder.DropColumn(
                name: "DefaultDispatchNote",
                table: "CariAccounts");

            migrationBuilder.DropColumn(
                name: "DefaultInvoiceNote",
                table: "CariAccounts");

            migrationBuilder.DropColumn(
                name: "DefaultWarehouse",
                table: "CariAccounts");

            migrationBuilder.DropColumn(
                name: "DeliveryInstructions",
                table: "CariAccounts");

            migrationBuilder.DropColumn(
                name: "DeliveryMethod",
                table: "CariAccounts");

            migrationBuilder.DropColumn(
                name: "EDocumentType",
                table: "CariAccounts");

            migrationBuilder.DropColumn(
                name: "InvoiceAddress",
                table: "CariAccounts");

            migrationBuilder.DropColumn(
                name: "InvoiceEmail",
                table: "CariAccounts");

            migrationBuilder.DropColumn(
                name: "IsVatExempt",
                table: "CariAccounts");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "CariAccounts");

            migrationBuilder.DropColumn(
                name: "PaymentTerms",
                table: "CariAccounts");

            migrationBuilder.DropColumn(
                name: "PreferredCarrier",
                table: "CariAccounts");

            migrationBuilder.DropColumn(
                name: "PriceListName",
                table: "CariAccounts");

            migrationBuilder.DropColumn(
                name: "RiskStatus",
                table: "CariAccounts");

            migrationBuilder.DropColumn(
                name: "SalesRepresentative",
                table: "CariAccounts");

            migrationBuilder.DropColumn(
                name: "VatExemptionReason",
                table: "CariAccounts");
        }
    }
}
