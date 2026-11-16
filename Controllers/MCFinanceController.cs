using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using MasterChefTakip.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace MasterChefTakip.Controllers;

[Authorize(Roles = AppRoles.Admin)]
[Route("MCFinance")]
[Microsoft.AspNetCore.Mvc.ServiceFilter(typeof(MasterChefTakip.Controllers.MCFinanceErrorFilter))]
public sealed class MCFinanceController : Controller
{
    private readonly IConfiguration _config;

    private static readonly string[] IncomeCategories =
    {
        "Müşteri Tahsilatı",
        "Diğer Gelir"
    };

    private static readonly string[] ExpenseCategories =
    {
        "Hammadde",
        "Ambalaj",
        "Personel",
        "Enerji",
        "Lojistik",
        "Kira",
        "Bakım",
        "Finansman",
        "Vergi",
        "Genel Gider",
        "Diğer Gider"
    };

    public MCFinanceController(IConfiguration config)
    {
        _config = config;
    }

    private SqliteConnection OpenConnection()
    {
        var cs = _config.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException(
                "DefaultConnection bulunamadı.");

        return new SqliteConnection(cs);
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? month)
    {
        var currentMonth = DateTime.Today.ToString(
            "yyyy-MM",
            CultureInfo.InvariantCulture);

        month = string.IsNullOrWhiteSpace(month)
            ? currentMonth
            : month.Trim();

        if (!DateTime.TryParseExact(
            month + "-01",
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var selectedDate))
        {
            return BadRequest("Ge?ersiz ay.");
        }

        var model = new MCFinancePage
        {
            SelectedMonth = month,
            IncomeCategories = IncomeCategories,
            ExpenseCategories = ExpenseCategories
        };

        await using var db = OpenConnection();
        await db.OpenAsync();

        await using (var check = db.CreateCommand())
        {
            check.CommandText = @"
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type = 'table'
                  AND name = 'MCFinanceTransactions'";

            var exists = Convert.ToInt64(
                await check.ExecuteScalarAsync()) > 0;

            if (!exists)
                return View(model);
        }

        var start = selectedDate.AddMonths(-11)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var end = selectedDate.AddMonths(1)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT
                    Id,
                    OccurredOn,
                    Kind,
                    Category,
                    Amount,
                    Description
                FROM MCFinanceTransactions
                WHERE OccurredOn >= @start
                  AND OccurredOn < @end
                ORDER BY OccurredOn DESC, Id DESC";

            cmd.Parameters.AddWithValue("@start", start);
            cmd.Parameters.AddWithValue("@end", end);

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var amountText = reader.GetString(4);

                if (!decimal.TryParse(
                    amountText,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var amount))
                {
                    continue;
                }

                model.Transactions.Add(new MCFinanceTransaction
                {
                    Id = reader.GetInt64(0),
                    Date = reader.GetString(1),
                    Kind = reader.GetString(2),
                    Category = reader.GetString(3),
                    Amount = amount,
                    Description = reader.GetString(5)
                });
            }
        }

        var selected = model.Transactions
            .Where(x => x.Date.StartsWith(
                month,
                StringComparison.Ordinal))
            .ToList();

        model.MonthTransactions = selected;

        model.Income = selected
            .Where(x => x.Kind == "Income")
            .Sum(x => x.Amount);

        model.Expense = selected
            .Where(x => x.Kind == "Expense")
            .Sum(x => x.Amount);

        for (var i = 11; i >= 0; i--)
        {
            var date = selectedDate.AddMonths(-i);

            var key = date.ToString(
                "yyyy-MM",
                CultureInfo.InvariantCulture);

            var rows = model.Transactions
                .Where(x => x.Date.StartsWith(
                    key,
                    StringComparison.Ordinal))
                .ToList();

            model.Monthly.Add(new MCFinanceMonth
            {
                Month = key,
                Income = rows
                    .Where(x => x.Kind == "Income")
                    .Sum(x => x.Amount),
                Expense = rows
                    .Where(x => x.Kind == "Expense")
                    .Sum(x => x.Amount)
            });
        }

        return View(model);
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        string? kind,
        string? category,
        string? amount,
        string? occurredOn,
        string? description)
    {
        kind = (kind ?? "").Trim();
        category = (category ?? "").Trim();
        amount = (amount ?? "").Trim();
        description = (description ?? "").Trim();

        if (kind != "Income" && kind != "Expense")
            return BadRequest("Geçersiz işlem türü.");

        var allowed = kind == "Income"
            ? IncomeCategories
            : ExpenseCategories;

        if (!allowed.Contains(category))
            return BadRequest("Geçersiz kategori.");

        if (!DateTime.TryParseExact(
            occurredOn,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date))
        {
            return BadRequest("Geçersiz tarih.");
        }

        if (date.Year < 2000 || date.Year > 2100)
            return BadRequest("Tarih aralık dışında.");

        if (!decimal.TryParse(
            amount,
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var value))
        {
            return BadRequest("Geçersiz tutar.");
        }

        if (value <= 0 ||
            value > 100000000000m ||
            decimal.Round(value, 2) != value)
        {
            return BadRequest(
                "Tutar pozitif ve en fazla iki ondalıklı olmalıdır.");
        }

        if (description.Length > 500)
            return BadRequest("Açıklama çok uzun.");

        await using var db = OpenConnection();
        await db.OpenAsync();

        await using var transaction =
            await db.BeginTransactionAsync();

        await using (var create = db.CreateCommand())
        {
            create.Transaction = (SqliteTransaction)transaction;

            create.CommandText = @"
                CREATE TABLE IF NOT EXISTS MCFinanceTransactions
                (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    OccurredOn TEXT NOT NULL,
                    Kind TEXT NOT NULL
                        CHECK (Kind IN ('Income','Expense')),
                    Category TEXT NOT NULL,
                    Amount TEXT NOT NULL,
                    Description TEXT NOT NULL,
                    Currency TEXT NOT NULL DEFAULT 'TRY',
                    CreatedAtUtc TEXT NOT NULL,
                    CreatedBy TEXT NOT NULL
                )";

            await create.ExecuteNonQueryAsync();
        }

        await using (var insert = db.CreateCommand())
        {
            insert.Transaction = (SqliteTransaction)transaction;

            insert.CommandText = @"
                INSERT INTO MCFinanceTransactions
                (
                    OccurredOn,
                    Kind,
                    Category,
                    Amount,
                    Description,
                    Currency,
                    CreatedAtUtc,
                    CreatedBy
                )
                VALUES
                (
                    @date,
                    @kind,
                    @category,
                    @amount,
                    @description,
                    'TRY',
                    @created,
                    @user
                )";

            insert.Parameters.AddWithValue(
                "@date",
                date.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture));

            insert.Parameters.AddWithValue("@kind", kind);
            insert.Parameters.AddWithValue("@category", category);

            insert.Parameters.AddWithValue(
                "@amount",
                value.ToString(CultureInfo.InvariantCulture));

            insert.Parameters.AddWithValue(
                "@description",
                description);

            insert.Parameters.AddWithValue(
                "@created",
                DateTime.UtcNow.ToString(
                    "O",
                    CultureInfo.InvariantCulture));

            insert.Parameters.AddWithValue(
                "@user",
                User.Identity?.Name ?? "Unknown");

            await insert.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        return RedirectToAction(
            nameof(Index),
            new
            {
                month = date.ToString(
                    "yyyy-MM",
                    CultureInfo.InvariantCulture)
            });
    }
}

public sealed class MCFinancePage
{
    public string SelectedMonth { get; set; } = "";

    public decimal Income { get; set; }
    public decimal Expense { get; set; }

    public decimal Net => Income - Expense;

    public string[] IncomeCategories { get; set; } =
        Array.Empty<string>();

    public string[] ExpenseCategories { get; set; } =
        Array.Empty<string>();

    public List<MCFinanceTransaction> Transactions
    {
        get;
        set;
    } = new();

    public List<MCFinanceTransaction> MonthTransactions
    {
        get;
        set;
    } = new();

    public List<MCFinanceMonth> Monthly
    {
        get;
        set;
    } = new();

    public decimal ChartMaximum =>
        Monthly.Count == 0
            ? 1m
            : Math.Max(
                1m,
                Monthly.Max(x => Math.Max(x.Income, x.Expense)));
}

public sealed class MCFinanceTransaction
{
    public long Id { get; set; }
    public string Date { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Category { get; set; } = "";
    public decimal Amount { get; set; }
    public string Description { get; set; } = "";
}

public sealed class MCFinanceMonth
{
    public string Month { get; set; } = "";
    public decimal Income { get; set; }
    public decimal Expense { get; set; }
}
