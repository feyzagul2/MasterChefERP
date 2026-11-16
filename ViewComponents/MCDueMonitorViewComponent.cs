using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace MasterChefTakip.ViewComponents;

public sealed class MCDueMonitorViewComponent : ViewComponent
{
    private readonly IConfiguration _configuration;

    public MCDueMonitorViewComponent(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var model = new MCDueMonitorModel();

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

                        // Count sale/purchase movements without a usable due date.
            await using (var missing = db.CreateCommand())
            {
                missing.CommandText = @"
                    SELECT COUNT(*)
                    FROM MCCariMovements
                    WHERE MovementType IN ('Sale','Purchase')
                      AND (
                          DueDate IS NULL
                          OR TRIM(DueDate) = ''
                      )";

                model.MissingDueDateCount = Convert.ToInt32(
                    await missing.ExecuteScalarAsync());
            }
                        // Read-only details for movements with missing due dates.
            await using (var details = db.CreateCommand())
            {
                details.CommandText = @"
                    SELECT
                        m.Id,
                        c.CompanyName,
                        m.MovementType,
                        m.MovementDate,
                        m.Debit,
                        m.Credit,
                        m.Currency,
                        m.ReferenceNumber
                    FROM MCCariMovements m
                    INNER JOIN CariAccounts c
                        ON c.Id = m.CariAccountId
                    WHERE m.MovementType IN ('Sale','Purchase')
                      AND (
                          m.DueDate IS NULL
                          OR TRIM(m.DueDate) = ''
                      )
                    ORDER BY m.MovementDate DESC, m.Id DESC
                    LIMIT 30";

                await using var detailsReader =
                    await details.ExecuteReaderAsync();

                while (await detailsReader.ReadAsync())
                {
                    var movementType = detailsReader.GetString(2);

                    var amountIndex =
                        movementType == "Sale" ? 4 : 5;

                    var amountText = Convert.ToString(
                        detailsReader.GetValue(amountIndex),
                        CultureInfo.InvariantCulture) ?? "";

                    var amountValid = decimal.TryParse(
                        amountText,
                        NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture,
                        out var amount);

                    model.MissingDueRows.Add(new MCMissingDueRow
                    {
                        Id = detailsReader.GetInt64(0),

                        Company = detailsReader.IsDBNull(1)
                            ? ""
                            : detailsReader.GetString(1),

                        Type = movementType,

                        Date = detailsReader.IsDBNull(3)
                            ? ""
                            : detailsReader.GetString(3),

                        Amount = amountValid && amount > 0
                            ? amount
                            : null,

                        Currency = detailsReader.IsDBNull(6)
                            ? ""
                            : detailsReader.GetString(6),

                        Reference = detailsReader.IsDBNull(7)
                            ? ""
                            : detailsReader.GetString(7)
                    });
                }
            }
            await using var command = db.CreateCommand();

            command.CommandText = @"
                SELECT
                    c.CompanyName,
                    m.MovementType,
                    m.DueDate,
                    m.Debit,
                    m.Credit,
                    m.Currency,
                    m.ReferenceNumber
                FROM MCCariMovements m
                INNER JOIN CariAccounts c
                    ON c.Id = m.CariAccountId
                WHERE m.MovementType IN ('Sale','Purchase')
                  AND m.DueDate IS NOT NULL
                  AND TRIM(m.DueDate) <> ''
                ORDER BY m.DueDate, m.Id
                LIMIT 1000";

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var dateText = reader.GetString(2);

                if (!DateTime.TryParseExact(
                        dateText,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var dueDate))
                {
                    model.InvalidCount++;
                    continue;
                }

                var type = reader.GetString(1);

                var amountIndex = type == "Sale" ? 3 : 4;

                var amountText = Convert.ToString(
                    reader.GetValue(amountIndex),
                    CultureInfo.InvariantCulture) ?? "";

                if (!decimal.TryParse(
                        amountText,
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out var amount) ||
                    amount <= 0)
                {
                    model.InvalidCount++;
                    continue;
                }

                var days = (dueDate.Date - DateTime.Today).Days;

                model.Rows.Add(new MCDueMonitorRow
                {
                    Company = reader.IsDBNull(0) ? "" : reader.GetString(0),
                    Type = type,
                    DueDate = dueDate,
                    Amount = amount,
                    Currency = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    Reference = reader.IsDBNull(6) ? "" : reader.GetString(6),
                    DaysRemaining = days
                });
            }
        }
        catch (Exception ex) when (
            ex is SqliteException ||
            ex is InvalidOperationException ||
            ex is FormatException ||
            ex is OverflowException)
        {
            model.Rows.Clear();
            model.Warning = "Vade kayıtları okunamadı.";
        }

        return View(model);
    }
}

public sealed class MCDueMonitorModel
{
    public string Warning { get; set; } = "";
    public int InvalidCount { get; set; }
    // MC_DUE_MISSING_DATES_V2
    public int MissingDueDateCount { get; set; }
    // MC_DUE_MISSING_DETAILS_V3
    public List<MCMissingDueRow> MissingDueRows { get; set; } = new();
    public List<MCDueMonitorRow> Rows { get; set; } = new();
}

public sealed class MCDueMonitorRow
{
    public string Company { get; set; } = "";
    public string Type { get; set; } = "";
    public DateTime DueDate { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public string Reference { get; set; } = "";
    public int DaysRemaining { get; set; }
}
// Read-only missing due date detail row.
public sealed class MCMissingDueRow
{
    public long Id { get; set; }
    public string Company { get; set; } = "";
    public string Type { get; set; } = "";
    public string Date { get; set; } = "";
    public decimal? Amount { get; set; }
    public string Currency { get; set; } = "";
    public string Reference { get; set; } = "";
}