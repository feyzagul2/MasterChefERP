using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using MasterChefTakip.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace MasterChefTakip.Controllers;

[Authorize(Roles = AppRoles.Admin)]
[Route("MCBankCashTransfer")]
public sealed class MCBankCashTransferController : Controller
{
    private readonly IConfiguration _configuration;

    public MCBankCashTransferController(IConfiguration configuration)
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

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var model = new MCBankCashTransferPage();

        await using var db = Open();
        await db.OpenAsync();

        await using var check = db.CreateCommand();

        check.CommandText = @"
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name IN (
                'MCBankCashAccounts',
                'MCBankCashMovements'
              )";

        if (Convert.ToInt64(await check.ExecuteScalarAsync()) != 2)
            return View(model);

        await using var cmd = db.CreateCommand();

        cmd.CommandText = @"
            SELECT Id, Code, Name
            FROM MCBankCashAccounts
            WHERE IsActive = 1
              AND Currency = 'TRY'
            ORDER BY Name";

        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            model.Accounts.Add(new MCBankCashTransferAccount
            {
                Id = reader.GetInt64(0),
                Code = reader.GetString(1),
                Name = reader.GetString(2)
            });
        }

        return View(model);
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        long fromAccountId,
        long toAccountId,
        string? amount,
        string? transferDate,
        string? description,
        string? requestToken)
    {
        if (fromAccountId <= 0 ||
            toAccountId <= 0 ||
            fromAccountId == toAccountId)
            return BadRequest("Kaynak ve hedef hesap farklı olmalıdır.");

        if (!DateTime.TryParseExact(
            transferDate,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date))
            return BadRequest("Geçersiz tarih.");

        if (date.Year < 2000 || date.Year > 2100)
            return BadRequest("Tarih aralık dışında.");

        if (!decimal.TryParse(
            amount,
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var value))
            return BadRequest("Geçersiz tutar.");

        if (value <= 0 ||
            value > 100000000000m ||
            decimal.Round(value, 2) != value)
            return BadRequest("Tutar pozitif ve iki ondalıklı olmalıdır.");

        if (!Guid.TryParse(requestToken, out var token))
            return BadRequest("Geçersiz işlem anahtarı.");

        description = (description ?? "").Trim();

        if (description.Length > 400)
            return BadRequest("Açıklama çok uzun.");

        var dateText = date.ToString(
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture);

        var amountText = value.ToString(
            CultureInfo.InvariantCulture);

        var tokenText = token.ToString("N");

        await using var db = Open();
        await db.OpenAsync();

        using var tx = db.BeginTransaction();

        try
        {
            await using (var check = db.CreateCommand())
            {
                check.Transaction = tx;

                check.CommandText = @"
                    SELECT COUNT(*)
                    FROM MCBankCashAccounts
                    WHERE Id IN (@source, @target)
                      AND IsActive = 1
                      AND Currency = 'TRY'";

                check.Parameters.AddWithValue("@source", fromAccountId);
                check.Parameters.AddWithValue("@target", toAccountId);

                if (Convert.ToInt64(await check.ExecuteScalarAsync()) != 2)
                    return BadRequest("Aktif TL hesapları bulunamadı.");
            }

            // Tekrarlanan istekte iki hareketin tekrar oluşmasını önler.
            await using (var check = db.CreateCommand())
            {
                check.Transaction = tx;

                check.CommandText = @"
                    SELECT COUNT(*)
                    FROM MCBankCashMovements
                    WHERE RequestToken IN (@outToken, @inToken)";

                check.Parameters.AddWithValue(
                    "@outToken", tokenText + ":OUT");

                check.Parameters.AddWithValue(
                    "@inToken", tokenText + ":IN");

                if (Convert.ToInt64(await check.ExecuteScalarAsync()) != 0)
                    return Conflict("Bu virman daha önce işlenmiş.");
            }

            // Tüm hareketler üzerinden kaynak bakiyeyi hesapla.
            decimal balance = 0m;

            await using (var check = db.CreateCommand())
            {
                check.Transaction = tx;

                check.CommandText = @"
                    SELECT Direction, Amount
                    FROM MCBankCashMovements
                    WHERE AccountId = @id";

                check.Parameters.AddWithValue("@id", fromAccountId);

                await using var reader = await check.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var direction = reader.GetString(0);

                    if (!decimal.TryParse(
                        reader.GetString(1),
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out var movementAmount))
                        throw new InvalidOperationException(
                            "Hesap hareketinde geçersiz tutar bulundu.");

                    balance += direction == "In"
                        ? movementAmount
                        : -movementAmount;
                }
            }

            if (balance < value)
                return BadRequest(
                    "Kaynak hesabın bakiyesi virman için yetersiz.");

            // İki hareket aynı transaction içinde kaydedilir.
            // Biri başarısızsa ikisi de geri alınır.
            foreach (var entry in new[]
            {
                new {
                    AccountId = fromAccountId,
                    Direction = "Out",
                    Token = tokenText + ":OUT"
                },
                new {
                    AccountId = toAccountId,
                    Direction = "In",
                    Token = tokenText + ":IN"
                }
            })
            {
                await using var insert = db.CreateCommand();
                insert.Transaction = tx;

                insert.CommandText = @"
                    INSERT INTO MCBankCashMovements
                    (
                        AccountId,
                        MovementDate,
                        Direction,
                        Amount,
                        Description,
                        ReferenceNumber,
                        RequestToken,
                        CreatedAtUtc,
                        CreatedBy
                    )
                    VALUES
                    (
                        @account,
                        @date,
                        @direction,
                        @amount,
                        @description,
                        @reference,
                        @token,
                        @created,
                        @user
                    )";

                insert.Parameters.AddWithValue(
                    "@account", entry.AccountId);

                insert.Parameters.AddWithValue("@date", dateText);
                insert.Parameters.AddWithValue(
                    "@direction", entry.Direction);
                insert.Parameters.AddWithValue("@amount", amountText);

                insert.Parameters.AddWithValue(
                    "@description",
                    "Hesaplar arası virman - " + description);

                insert.Parameters.AddWithValue(
                    "@reference",
                    "VIR-" + tokenText[..12].ToUpperInvariant());

                insert.Parameters.AddWithValue("@token", entry.Token);

                insert.Parameters.AddWithValue(
                    "@created",
                    DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));

                insert.Parameters.AddWithValue(
                    "@user",
                    User.Identity?.Name ?? "Unknown");

                await insert.ExecuteNonQueryAsync();
            }

            tx.Commit();

            return Redirect("/MCBankCash");
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return Conflict("Virman daha önce kaydedilmiş veya kayıt çakışması oluşmuş.");
        }
    }
}

public sealed class MCBankCashTransferPage
{
    public List<MCBankCashTransferAccount> Accounts { get; set; } = new();
}

public sealed class MCBankCashTransferAccount
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
}