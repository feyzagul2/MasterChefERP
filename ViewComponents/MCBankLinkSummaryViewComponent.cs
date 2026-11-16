using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace MasterChefTakip.ViewComponents;

public sealed class MCBankLinkSummaryViewComponent : ViewComponent
{
    private readonly IConfiguration _configuration;

    public MCBankLinkSummaryViewComponent(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var model = new MCBankLinkSummaryModel();
        var cs = _configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(cs))
        {
            model.Warning = "Veritabanı bağlantısı bulunamadı.";
            return View(model);
        }

        try
        {
            var builder = new SqliteConnectionStringBuilder(cs)
            {
                Mode = SqliteOpenMode.ReadOnly
            };

            await using var db = new SqliteConnection(builder.ToString());
            await db.OpenAsync();

            await using (var check = db.CreateCommand())
            {
                check.CommandText = @"
                    SELECT COUNT(*)
                    FROM sqlite_master
                    WHERE type='table'
                      AND name IN (
                        'MCBankCashAccounts',
                        'MCBankCashMovements',
                        'MCCariMovements'
                      )";

                if (Convert.ToInt64(await check.ExecuteScalarAsync()) != 3)
                {
                    model.Warning = "Banka/kasa hareket tabloları eksik.";
                    return View(model);
                }
            }

            await using var command = db.CreateCommand();

            command.CommandText = @"
                SELECT
                    a.Name,
                    a.AccountType,
                    a.Currency,
                    b.Id,
                    b.MovementDate,
                    b.Direction,
                    b.Amount,
                    b.Description,
                    b.ReferenceNumber,
                    (
                        SELECT COUNT(*)
                        FROM MCCariMovements c
                        WHERE c.RelatedBankMovementId = b.Id
                    ) AS CariLinkCount
                FROM MCBankCashMovements b
                INNER JOIN MCBankCashAccounts a
                    ON a.Id = b.AccountId
                ORDER BY b.Id DESC";

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                model.TotalMovements++;

                var amountText = Convert.ToString(
                    reader.GetValue(6),
                    CultureInfo.InvariantCulture);

                if (!decimal.TryParse(
                        amountText,
                        NumberStyles.AllowDecimalPoint |
                        NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture,
                        out var amount) ||
                    amount <= 0)
                {
                    model.InvalidMovements++;
                    continue;
                }

                var links = Convert.ToInt64(reader.GetValue(9));

                if (links == 1)
                {
                    model.LinkedMovements++;
                }
                else if (links == 0)
                {
                    model.UnlinkedMovements++;
                }
                else
                {
                    model.MultipleLinkedMovements++;
                }

                if (links == 1)
                    continue;

                if (model.Rows.Count >= 50)
                    continue;

                model.Rows.Add(new MCBankLinkSummaryRow
                {
                    Account = reader.IsDBNull(0)
                        ? ""
                        : reader.GetString(0),

                    AccountType = reader.IsDBNull(1)
                        ? ""
                        : reader.GetString(1),

                    Currency = reader.IsDBNull(2)
                        ? ""
                        : reader.GetString(2),

                    Id = reader.GetInt64(3),

                    Date = reader.IsDBNull(4)
                        ? ""
                        : reader.GetString(4),

                    Direction = reader.IsDBNull(5)
                        ? ""
                        : reader.GetString(5),

                    Amount = amount,

                    Description = reader.IsDBNull(7)
                        ? ""
                        : reader.GetString(7),

                    Reference = reader.IsDBNull(8)
                        ? ""
                        : reader.GetString(8),

                    LinkCount = links
                });
            }
        }
        catch (Exception ex) when (
            ex is SqliteException ||
            ex is InvalidOperationException ||
            ex is OverflowException ||
            ex is FormatException)
        {
            model = new MCBankLinkSummaryModel
            {
                Warning = "Banka/kasa bağlantı kayıtları okunamadı."
            };
        }

        return View(model);
    }
}

public sealed class MCBankLinkSummaryModel
{
    public string Warning { get; set; } = "";
    public long TotalMovements { get; set; }
    public long LinkedMovements { get; set; }
    public long UnlinkedMovements { get; set; }
    public long MultipleLinkedMovements { get; set; }
    public long InvalidMovements { get; set; }

    public List<MCBankLinkSummaryRow> Rows { get; set; } = new();
}

public sealed class MCBankLinkSummaryRow
{
    public long Id { get; set; }
    public string Account { get; set; } = "";
    public string AccountType { get; set; } = "";
    public string Currency { get; set; } = "";
    public string Date { get; set; } = "";
    public string Direction { get; set; } = "";
    public decimal Amount { get; set; }
    public string Description { get; set; } = "";
    public string Reference { get; set; } = "";
    public long LinkCount { get; set; }
}