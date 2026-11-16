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
[Route("MCBankCashReport")]
public sealed class MCBankCashReportController : Controller
{
    private readonly IConfiguration _configuration;

    public MCBankCashReportController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        long? accountId,
        string? start,
        string? end)
    {
        var model = new MCBankCashReportPage();

        var today = DateTime.Today;

        if (string.IsNullOrWhiteSpace(start))
            start = new DateTime(today.Year, today.Month, 1)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (string.IsNullOrWhiteSpace(end))
            end = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (!DateTime.TryParseExact(
            start, "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var startDate))
            return BadRequest("Başlangıç tarihi geçersiz.");

        if (!DateTime.TryParseExact(
            end, "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var endDate))
            return BadRequest("Bitiş tarihi geçersiz.");

        if (startDate > endDate)
            return BadRequest("Başlangıç tarihi bitişten sonra olamaz.");

        if (accountId.HasValue && accountId.Value <= 0)
            return BadRequest("Hesap seçimi geçersiz.");

        model.Start = start!;
        model.End = end!;
        model.AccountId = accountId;

        var cs = _configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(cs))
            return StatusCode(500, "Veritabanı bağlantısı bulunamadı.");

        await using var db = new SqliteConnection(cs);
        await db.OpenAsync();

        await using (var check = db.CreateCommand())
        {
            check.CommandText = @"
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type='table'
                  AND name IN (
                      'MCBankCashAccounts',
                      'MCBankCashMovements'
                  )";

            if (Convert.ToInt64(await check.ExecuteScalarAsync()) != 2)
                return View(model);
        }

        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT Id, Code, Name
                FROM MCBankCashAccounts
                ORDER BY Name";

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                model.Accounts.Add(new MCBankCashReportAccount
                {
                    Id = reader.GetInt64(0),
                    Code = reader.GetString(1),
                    Name = reader.GetString(2)
                });
            }
        }

        if (accountId.HasValue &&
            !model.Accounts.Any(x => x.Id == accountId.Value))
            return BadRequest("Seçilen hesap bulunamadı.");

        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT
                    m.Id,
                    m.MovementDate,
                    a.Name,
                    a.Code,
                    m.Direction,
                    m.Amount,
                    m.Description,
                    m.ReferenceNumber, m.RequestToken
                FROM MCBankCashMovements m
                INNER JOIN MCBankCashAccounts a
                    ON a.Id = m.AccountId
                WHERE m.MovementDate >= @start
                  AND m.MovementDate <= @end
                  AND (@accountId IS NULL OR m.AccountId = @accountId)
                ORDER BY m.MovementDate DESC, m.Id DESC";

            cmd.Parameters.AddWithValue("@start", start);
            cmd.Parameters.AddWithValue("@end", end);
            cmd.Parameters.AddWithValue(
                "@accountId",
                accountId.HasValue
                    ? (object)accountId.Value
                    : DBNull.Value);

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                if (!decimal.TryParse(
                    reader.GetString(5),
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var amount))
                    throw new InvalidOperationException(
                        "Geçersiz finans hareketi tutarı.");

                model.Rows.Add(new MCBankCashReportRow
                {
                    Id = reader.GetInt64(0),
                    Date = reader.GetString(1),
                    AccountName = reader.GetString(2),
                    AccountCode = reader.GetString(3),
                    Direction = reader.GetString(4),
                    Amount = amount,
                    Description = reader.GetString(6),
                    Reference = reader.GetString(7), RequestToken = reader.GetString(8)
                });
            }
        }

        // Secilen hesap veya tum hesaplar icin donem basi bakiye.
        // Mevcut hareketler okunur; veritabanina yazilmaz.
        await using (var balanceCommand = db.CreateCommand())
        {
            balanceCommand.CommandText = @"
                SELECT m.Direction, m.Amount
                FROM MCBankCashMovements m
                WHERE m.MovementDate < @start
                  AND (@accountId IS NULL OR m.AccountId = @accountId)";

            balanceCommand.Parameters.AddWithValue("@start", start);

            balanceCommand.Parameters.AddWithValue(
                "@accountId",
                accountId.HasValue
                    ? (object)accountId.Value
                    : DBNull.Value);

            await using var balanceReader =
                await balanceCommand.ExecuteReaderAsync();

            while (await balanceReader.ReadAsync())
            {
                if (!decimal.TryParse(
                    balanceReader.GetString(1),
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var openingAmount))
                {
                    throw new InvalidOperationException(
                        "Gecmis hesap hareketinde gecersiz tutar bulundu.");
                }

                var direction = balanceReader.GetString(0);

                if (direction == "In")
                    model.OpeningBalance += openingAmount;
                else if (direction == "Out")
                    model.OpeningBalance -= openingAmount;
                else
                    throw new InvalidOperationException(
                        "Gecersiz hesap hareketi turu.");
            }
        }
        return View(model);
    }
}

public sealed class MCBankCashReportPage
{
    public string Start { get; set; } = "";
    public string End { get; set; } = "";
    public long? AccountId { get; set; }

    public List<MCBankCashReportAccount> Accounts { get; set; } = new();
    public List<MCBankCashReportRow> Rows { get; set; } = new();

    public decimal TotalIn =>
        Rows.Where(x => x.Direction == "In").Sum(x => x.Amount);

    public decimal TotalOut =>
        Rows.Where(x => x.Direction == "Out").Sum(x => x.Amount);

    // MC_OPENING_BALANCE_V1
    public decimal OpeningBalance { get; set; }
    public decimal ClosingBalance => OpeningBalance + Net;
        // MC_TRANSFER_SEPARATION_V2
    public decimal ExternalIn =>
        Rows.Where(x => !x.IsTransfer && x.Direction == "In")
            .Sum(x => x.Amount);

    public decimal ExternalOut =>
        Rows.Where(x => !x.IsTransfer && x.Direction == "Out")
            .Sum(x => x.Amount);

    public decimal TransferIn =>
        Rows.Where(x => x.IsTransfer && x.Direction == "In")
            .Sum(x => x.Amount);

    public decimal TransferOut =>
        Rows.Where(x => x.IsTransfer && x.Direction == "Out")
            .Sum(x => x.Amount);

    public decimal Net => TotalIn - TotalOut;
}

public sealed class MCBankCashReportAccount
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class MCBankCashReportRow
{
    public long Id { get; set; }
    public string Date { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string AccountCode { get; set; } = "";
    public string Direction { get; set; } = "";
    public decimal Amount { get; set; }
    public string Description { get; set; } = "";
    public string Reference { get; set; } = "";
    public string RequestToken { get; set; } = "";

    public bool IsTransfer =>
        Reference.StartsWith("VIR-", StringComparison.OrdinalIgnoreCase)
        && (
            RequestToken.EndsWith(":IN", StringComparison.OrdinalIgnoreCase)
            || RequestToken.EndsWith(":OUT", StringComparison.OrdinalIgnoreCase)
        );
}