using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace MasterChefTakip.ViewComponents;

public sealed class MCFinanceCariSummaryViewComponent : ViewComponent
{
    private readonly IConfiguration _configuration;

    public MCFinanceCariSummaryViewComponent(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var model = new MCFinanceCariSummaryModel();

        var connectionString =
            _configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            model.Warning = "Veritabanı bağlantısı bulunamadı.";
            return View(model);
        }

        try
        {
            var builder = new SqliteConnectionStringBuilder(connectionString)
            {
                Mode = SqliteOpenMode.ReadOnly
            };

            await using var db = new SqliteConnection(builder.ToString());
            await db.OpenAsync();

            await using var check = db.CreateCommand();

            check.CommandText = @"
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type = 'table'
                  AND name IN ('CariAccounts','MCCariMovements')";

            if (Convert.ToInt64(await check.ExecuteScalarAsync()) != 2)
            {
                model.Warning = "Cari hareket tabloları bulunamadı.";
                return View(model);
            }

            var lookup = new Dictionary<long, MCFinanceCariSummaryPosition>();

            await using var command = db.CreateCommand();

            command.CommandText = @"
                SELECT
                    c.Id,
                    c.CompanyName,
                    c.IsCustomer,
                    c.IsSupplier,
                    c.Currency,
                    m.MovementType,
                    m.Currency,
                    m.Debit,
                    m.Credit
                FROM MCCariMovements m
                INNER JOIN CariAccounts c
                    ON c.Id = m.CariAccountId
                ORDER BY c.Id, m.Id";

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var id = reader.GetInt64(0);

                if (!lookup.TryGetValue(id, out var item))
                {
                    item = new MCFinanceCariSummaryPosition
                    {
                        Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
                        IsCustomer = !reader.IsDBNull(2) &&
                            Convert.ToInt64(reader.GetValue(2)) != 0,
                        IsSupplier = !reader.IsDBNull(3) &&
                            Convert.ToInt64(reader.GetValue(3)) != 0,
                        Currency = reader.IsDBNull(4)
                            ? ""
                            : reader.GetString(4).Trim().ToUpperInvariant()
                    };

                    lookup.Add(id, item);
                }

                var type = reader.IsDBNull(5) ? "" : reader.GetString(5);

                var movementCurrency = reader.IsDBNull(6)
                    ? ""
                    : reader.GetString(6).Trim().ToUpperInvariant();

                var debitText = Convert.ToString(
                    reader.GetValue(7),
                    CultureInfo.InvariantCulture) ?? "";

                var creditText = Convert.ToString(
                    reader.GetValue(8),
                    CultureInfo.InvariantCulture) ?? "";

                if (string.IsNullOrWhiteSpace(item.Currency) ||
                    item.Currency != movementCurrency ||
                    !decimal.TryParse(
                        debitText,
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out var debit) ||
                    !decimal.TryParse(
                        creditText,
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out var credit) ||
                    debit < 0 ||
                    credit < 0 ||
                    (debit > 0) == (credit > 0))
                {
                    item.Invalid = true;
                    continue;
                }

                // A customer and supplier can be the same company.
                // Until their roles have separate ledgers, do not net them.
                if (item.IsCustomer == item.IsSupplier)
                {
                    item.Invalid = true;
                    continue;
                }

                bool allowed =
                    item.IsCustomer
                        ? type == "Sale" ||
                          type == "Collection" ||
                          type == "OpeningDebit" ||
                          type == "OpeningCredit"
                        : type == "Purchase" ||
                          type == "Payment" ||
                          type == "OpeningDebit" ||
                          type == "OpeningCredit";

                if (!allowed)
                {
                    item.Invalid = true;
                    continue;
                }

                // Customer: debit creates receivable.
                // Supplier: credit creates payable.
                item.Net += debit - credit;
                item.MovementCount++;
            }

            model.Positions = lookup.Values
                .OrderBy(x => x.Name)
                .ToList();
        }
        catch (Exception ex) when (
            ex is SqliteException ||
            ex is InvalidOperationException ||
            ex is OverflowException ||
            ex is FormatException ||
            ex is ArgumentException)
        {
            model.Positions.Clear();
            model.Warning = "Cari finans kayıtları okunamadı.";
        }

        return View(model);
    }
}

public sealed class MCFinanceCariSummaryModel
{
    public string Warning { get; set; } = "";
    public List<MCFinanceCariSummaryPosition> Positions { get; set; } = new();
}

public sealed class MCFinanceCariSummaryPosition
{
    public string Name { get; set; } = "";
    public string Currency { get; set; } = "";
    public bool IsCustomer { get; set; }
    public bool IsSupplier { get; set; }
    public bool Invalid { get; set; }
    public int MovementCount { get; set; }
    public decimal Net { get; set; }

    public bool Valid =>
        !Invalid &&
        MovementCount > 0 &&
        IsCustomer != IsSupplier &&
        !string.IsNullOrWhiteSpace(Currency);

    public decimal Outstanding =>
        IsCustomer ? Math.Max(Net, 0m) : Math.Max(-Net, 0m);

    public decimal Advance =>
        IsCustomer ? Math.Max(-Net, 0m) : Math.Max(Net, 0m);
}