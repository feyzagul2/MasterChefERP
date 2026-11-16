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
[Route("MCBankCash")]
public sealed class MCBankCashController : Controller
{
    private readonly IConfiguration _configuration;

    public MCBankCashController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private SqliteConnection Open()
    {
        var cs = _configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException("Veritabanı bağlantısı bulunamadı.");

        return new SqliteConnection(cs);
    }

    private static async Task EnsureTables(SqliteConnection db, SqliteTransaction tx)
    {
        await using var cmd = db.CreateCommand();
        cmd.Transaction = tx;

        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS MCBankCashAccounts
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Code TEXT NOT NULL UNIQUE,
                Name TEXT NOT NULL,
                AccountType TEXT NOT NULL
                    CHECK(AccountType IN ('Cash','Bank')),
                Currency TEXT NOT NULL DEFAULT 'TRY',
                IsActive INTEGER NOT NULL DEFAULT 1,
                CreatedAtUtc TEXT NOT NULL,
                CreatedBy TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS MCBankCashMovements
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                AccountId INTEGER NOT NULL,
                MovementDate TEXT NOT NULL,
                Direction TEXT NOT NULL
                    CHECK(Direction IN ('In','Out')),
                Amount TEXT NOT NULL,
                Description TEXT NOT NULL,
                ReferenceNumber TEXT NOT NULL,
                RequestToken TEXT NOT NULL UNIQUE,
                CreatedAtUtc TEXT NOT NULL,
                CreatedBy TEXT NOT NULL,
                FOREIGN KEY(AccountId) REFERENCES MCBankCashAccounts(Id)
            );";

        await cmd.ExecuteNonQueryAsync();
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var model = new MCBankCashPage();

        await using var db = Open();
        await db.OpenAsync();

        await using (var check = db.CreateCommand())
        {
            check.CommandText = @"
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type='table'
                  AND name='MCBankCashAccounts'";

            if (Convert.ToInt64(await check.ExecuteScalarAsync()) == 0)
                return View(model);
        }

        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT Id, Code, Name, AccountType, IsActive
                FROM MCBankCashAccounts
                ORDER BY Name";

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                model.Accounts.Add(new MCBankCashAccount
                {
                    Id = reader.GetInt64(0),
                    Code = reader.GetString(1),
                    Name = reader.GetString(2),
                    Type = reader.GetString(3),
                    Active = reader.GetInt64(4) == 1
                });
            }
        }

        await using (var check = db.CreateCommand())
        {
            check.CommandText = @"
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type='table'
                  AND name='MCBankCashMovements'";

            if (Convert.ToInt64(await check.ExecuteScalarAsync()) == 0)
                return View(model);
        }

        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT m.Id, m.AccountId, a.Name,
                       m.MovementDate, m.Direction,
                       m.Amount, m.Description, m.ReferenceNumber
                FROM MCBankCashMovements m
                JOIN MCBankCashAccounts a ON a.Id=m.AccountId
                ORDER BY m.MovementDate DESC, m.Id DESC";

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                model.Movements.Add(new MCBankCashMovement
                {
                    Id = reader.GetInt64(0),
                    AccountId = reader.GetInt64(1),
                    AccountName = reader.GetString(2),
                    Date = reader.GetString(3),
                    Direction = reader.GetString(4),
                    Amount = decimal.Parse(
                        reader.GetString(5),
                        CultureInfo.InvariantCulture),
                    Description = reader.GetString(6),
                    Reference = reader.GetString(7)
                });
            }
        }

        foreach (var account in model.Accounts)
        {
            account.Balance = model.Movements
                .Where(x => x.AccountId == account.Id)
                .Sum(x => x.Direction == "In" ? x.Amount : -x.Amount);
        }

        return View(model);
    }

    [HttpPost("CreateAccount")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAccount(
        string? code,
        string? name,
        string? accountType)
    {
        code = (code ?? "").Trim().ToUpperInvariant();
        name = (name ?? "").Trim();

        if (!System.Text.RegularExpressions.Regex.IsMatch(
            code, @"^[A-Z0-9_-]{2,30}$"))
            return BadRequest("Hesap kodu geçersiz.");

        if (name.Length < 2 || name.Length > 120)
            return BadRequest("Hesap adı 2-120 karakter olmalıdır.");

        if (accountType != "Bank" && accountType != "Cash")
            return BadRequest("Hesap türü geçersiz.");

        await using var db = Open();
        await db.OpenAsync();
        using var tx = db.BeginTransaction();

        await EnsureTables(db, tx);

        await using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;

            cmd.CommandText = @"
                INSERT INTO MCBankCashAccounts
                (Code, Name, AccountType, Currency,
                 IsActive, CreatedAtUtc, CreatedBy)
                VALUES (@code,@name,@type,'TRY',1,@date,@user)";

            cmd.Parameters.AddWithValue("@code", code);
            cmd.Parameters.AddWithValue("@name", name);
            cmd.Parameters.AddWithValue("@type", accountType);
            cmd.Parameters.AddWithValue("@date", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue(
                "@user", User.Identity?.Name ?? "Unknown");

            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                return Conflict("Bu hesap kodu zaten kayıtlı.");
            }
        }

        tx.Commit();

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("CreateMovement")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateMovement(
        long accountId,
        string? direction,
        string? movementDate,
        string? amount,
        string? description,
        string? referenceNumber,
        string? requestToken)
    {
        if (accountId <= 0 || (direction != "In" && direction != "Out"))
            return BadRequest("Hesap veya işlem türü geçersiz.");

        if (!DateTime.TryParseExact(
            movementDate, "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date))
            return BadRequest("Tarih geçersiz.");

        if (date.Year < 2000 || date.Year > 2100)
            return BadRequest("Tarih aralık dışında.");

        if (!decimal.TryParse(
            amount, NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var value))
            return BadRequest("Tutar geçersiz.");

        if (value <= 0 || value > 100000000000m ||
            decimal.Round(value, 2) != value)
            return BadRequest("Tutar pozitif ve iki ondalıklı olmalıdır.");

        description = (description ?? "").Trim();
        referenceNumber = (referenceNumber ?? "").Trim();

        if (description.Length > 500 || referenceNumber.Length > 100)
            return BadRequest("Açıklama veya referans çok uzun.");

        if (!Guid.TryParse(requestToken, out var token))
            return BadRequest("İşlem anahtarı geçersiz.");

        await using var db = Open();
        await db.OpenAsync();
        using var tx = db.BeginTransaction();

        await EnsureTables(db, tx);

        await using (var check = db.CreateCommand())
        {
            check.Transaction = tx;
            check.CommandText = @"
                SELECT COUNT(*) FROM MCBankCashAccounts
                WHERE Id=@id AND IsActive=1";

            check.Parameters.AddWithValue("@id", accountId);

            if (Convert.ToInt64(await check.ExecuteScalarAsync()) != 1)
                return BadRequest("Aktif hesap bulunamadı.");
        }

        await using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;

            cmd.CommandText = @"
                INSERT INTO MCBankCashMovements
                (AccountId,MovementDate,Direction,Amount,
                 Description,ReferenceNumber,RequestToken,
                 CreatedAtUtc,CreatedBy)
                VALUES
                (@account,@date,@direction,@amount,
                 @description,@reference,@token,@created,@user)";

            cmd.Parameters.AddWithValue("@account", accountId);
            cmd.Parameters.AddWithValue(
                "@date", date.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@direction", direction);
            cmd.Parameters.AddWithValue(
                "@amount", value.ToString(CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@description", description);
            cmd.Parameters.AddWithValue("@reference", referenceNumber);
            cmd.Parameters.AddWithValue("@token", token.ToString("N"));
            cmd.Parameters.AddWithValue(
                "@created", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue(
                "@user", User.Identity?.Name ?? "Unknown");

            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                return Conflict("Bu işlem daha önce kaydedilmiş.");
            }
        }

        tx.Commit();

        return RedirectToAction(nameof(Index));
    }
}

public sealed class MCBankCashPage
{
    public List<MCBankCashAccount> Accounts { get; set; } = new();
    public List<MCBankCashMovement> Movements { get; set; } = new();

    public decimal TotalBalance => Accounts.Sum(x => x.Balance);
    public decimal TotalIn => Movements
        .Where(x => x.Direction == "In").Sum(x => x.Amount);
    public decimal TotalOut => Movements
        .Where(x => x.Direction == "Out").Sum(x => x.Amount);
}

public sealed class MCBankCashAccount
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool Active { get; set; }
    public decimal Balance { get; set; }
}

public sealed class MCBankCashMovement
{
    public long Id { get; set; }
    public long AccountId { get; set; }
    public string AccountName { get; set; } = "";
    public string Date { get; set; } = "";
    public string Direction { get; set; } = "";
    public decimal Amount { get; set; }
    public string Description { get; set; } = "";
    public string Reference { get; set; } = "";
}