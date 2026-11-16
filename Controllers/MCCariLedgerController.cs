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
[Route("MCCariLedger")]
public sealed class MCCariLedgerController : Controller
{
    private readonly IConfiguration _configuration;

    public MCCariLedgerController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var model = new MCCariLedgerPage();

        var connectionString =
            _configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
            return StatusCode(500, "Veritabanı bağlantısı bulunamadı.");

        var builder = new SqliteConnectionStringBuilder(connectionString)
        {
            Mode = SqliteOpenMode.ReadOnly
        };

        await using var db = new SqliteConnection(builder.ToString());
        await db.OpenAsync();

        await using (var countCommand = db.CreateCommand())
        {
            countCommand.CommandText = @"
                SELECT COUNT(*)
                FROM CariAccounts";

            model.CariCount =
                Convert.ToInt32(await countCommand.ExecuteScalarAsync());
        }

        await using (var cariOptions = db.CreateCommand())
        {
            cariOptions.CommandText = @"
                SELECT Id, CompanyName, Currency, IsCustomer, IsSupplier
                FROM CariAccounts
                WHERE IsActive = 1
                ORDER BY CompanyName";

            await using var reader = await cariOptions.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                model.AccountOptions.Add(new MCCariLedgerAccountOption
                {
                    Id = reader.GetInt64(0),
                    Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Currency = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    IsCustomer = Convert.ToInt64(reader.GetValue(3)) != 0,
                    IsSupplier = Convert.ToInt64(reader.GetValue(4)) != 0
                });
            }
        }

        await using (var bankOptions = db.CreateCommand())
        {
            bankOptions.CommandText = @"
                SELECT Id, Name, Currency
                FROM MCBankCashAccounts
                WHERE IsActive = 1
                ORDER BY Name";

            await using var reader = await bankOptions.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                model.BankOptions.Add(new MCCariLedgerBankOption
                {
                    Id = reader.GetInt64(0),
                    Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Currency = reader.IsDBNull(2) ? "" : reader.GetString(2)
                });
            }
        }
        await using (var command = db.CreateCommand())
        {
            command.CommandText = @"
                SELECT
                    c.Id,
                    c.Code,
                    c.CompanyName,
                    c.IsCustomer,
                    c.IsSupplier,
                    c.Currency,
                    m.Id,
                    m.MovementDate,
                    m.MovementType,
                    m.Debit,
                    m.Credit,
                    m.Currency,
                    m.DueDate,
                    m.Description,
                    m.ReferenceNumber
                FROM MCCariMovements m
                INNER JOIN CariAccounts c
                    ON c.Id = m.CariAccountId
                ORDER BY m.MovementDate DESC, m.Id DESC";

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var debitText = Convert.ToString(
                    reader.GetValue(9),
                    CultureInfo.InvariantCulture) ?? "";

                var creditText = Convert.ToString(
                    reader.GetValue(10),
                    CultureInfo.InvariantCulture) ?? "";

                if (!decimal.TryParse(
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
                    model.Warning =
                        "Hatalı tutar bulunan cari hareketler var. " +
                        "Toplamlar gösterilmiyor.";
                    model.Rows.Clear();
                    break;
                }

                model.Rows.Add(new MCCariLedgerRow
                {
                    CariId = reader.GetInt64(0),
                    CariCode = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    CariName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    IsCustomer = !reader.IsDBNull(3) && reader.GetInt64(3) != 0,
                    IsSupplier = !reader.IsDBNull(4) && reader.GetInt64(4) != 0,
                    AccountCurrency = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    Id = reader.GetInt64(6),
                    Date = reader.IsDBNull(7) ? "" : reader.GetString(7),
                    Type = reader.IsDBNull(8) ? "" : reader.GetString(8),
                    Debit = debit,
                    Credit = credit,
                    Currency = reader.IsDBNull(11) ? "" : reader.GetString(11),
                    DueDate = reader.IsDBNull(12) ? "" : reader.GetString(12),
                    Description = reader.IsDBNull(13) ? "" : reader.GetString(13),
                    Reference = reader.IsDBNull(14) ? "" : reader.GetString(14)
                });
            }
        }

        return View(model);
    }
    // MC_CARI_ENTRY_V1
    [HttpPost("Record")]
    [ValidateAntiForgeryToken]
    // MC_CARI_WRITE_CURRENCY_GUARD_V2
    [TypeFilter(typeof(MCCariCurrencyValidationFilter))]
    public async Task<IActionResult> Record(
        long cariId,
        string movementType,
        string amount,
        string? dueDate,
        string? description,
        string? referenceNumber,
        long? bankAccountId,
        string requestToken)
    {
        if (cariId <= 0 ||
            string.IsNullOrWhiteSpace(requestToken) ||
            requestToken.Length > 100 ||
            !Guid.TryParse(requestToken, out _) ||
            !new[] { "Sale", "Purchase", "Collection", "Payment" }
                .Contains(movementType))
            return BadRequest("Geçersiz işlem bilgisi.");

                // MC_CARI_AMOUNT_SAFETY_V2
        // Accept 1250.50 or 1250,50. Never interpret comma as thousands.
        var amountText = (amount ?? "").Trim();

        if (!System.Text.RegularExpressions.Regex.IsMatch(
                amountText,
                @"^\d{1,13}([.,]\d{1,2})?$"))
            return BadRequest(
                "Tutar geçersiz. Örnek: 1250,50 veya 1250.50");

        amountText = amountText.Replace(',', '.');

        if (!decimal.TryParse(
                amountText,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var value) ||
            value <= 0 ||
            value > 1000000000000m)
            return BadRequest("Geçersiz işlem tutarı.");

        var hasBank = movementType == "Collection" ||
                      movementType == "Payment";

        if (hasBank != bankAccountId.HasValue ||
            (bankAccountId.HasValue && bankAccountId.Value <= 0))
            return BadRequest("Tahsilat ve ödemede kasa/banka hesabı zorunludur.");

        if (!string.IsNullOrWhiteSpace(dueDate) &&
            !DateTime.TryParseExact(
                dueDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _))
            return BadRequest("Vade tarihi geçersiz.");

        if ((description?.Length ?? 0) > 500 ||
            (referenceNumber?.Length ?? 0) > 100)
            return BadRequest("Açıklama veya referans çok uzun.");

        var connectionString =
            _configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
            return StatusCode(500, "Veritabanı bağlantısı bulunamadı.");

        await using var db = new SqliteConnection(connectionString);
        await db.OpenAsync();

        await using var tx = await db.BeginTransactionAsync();
        // MC_CARI_TRANSACTION_CURRENCY_GUARD_V4
        // Cari and bank currencies are verified within the write transaction.
        if (movementType is "Sale" or "Purchase" or "Collection" or "Payment")
        {
            string transactionCariCurrency;

            await using (var currencyCheck = db.CreateCommand())
            {
                currencyCheck.Transaction =
                    (Microsoft.Data.Sqlite.SqliteTransaction)tx;

                currencyCheck.CommandText = @"
                    SELECT Currency
                    FROM CariAccounts
                    WHERE Id = @id AND IsActive = 1";
                // MC_CARI_TRANSACTION_ROLE_GUARD_V6
                // Recheck account type inside the same write transaction.
                currencyCheck.CommandText +=
                    movementType is "Sale" or "Collection"
                        ? " AND IsCustomer = 1"
                        : " AND IsSupplier = 1";

                currencyCheck.Parameters.AddWithValue("@id", cariId);

                var result =
                    await currencyCheck.ExecuteScalarAsync();

                transactionCariCurrency =
                    Convert.ToString(result)?.Trim() ?? "";
            }

            if (string.IsNullOrWhiteSpace(transactionCariCurrency))
            {
                await tx.RollbackAsync();

                return BadRequest(
                    "Cari hesap para birimi bulunamadı. İşlem kaydedilmedi.");
            }

            if (movementType is "Collection" or "Payment")
            {
                if (!bankAccountId.HasValue)
                {
                    await tx.RollbackAsync();

                    return BadRequest(
                        "Tahsilat veya ödeme için banka/kasa hesabı zorunludur.");
                }

                string transactionBankCurrency;

                await using (var bankCurrencyCheck = db.CreateCommand())
                {
                    bankCurrencyCheck.Transaction =
                        (Microsoft.Data.Sqlite.SqliteTransaction)tx;

                    bankCurrencyCheck.CommandText = @"
                        SELECT Currency
                        FROM MCBankCashAccounts
                        WHERE Id = @id AND IsActive = 1";

                    bankCurrencyCheck.Parameters.AddWithValue(
                        "@id",
                        bankAccountId.Value);

                    var result =
                        await bankCurrencyCheck.ExecuteScalarAsync();

                    transactionBankCurrency =
                        Convert.ToString(result)?.Trim() ?? "";
                }

                if (string.IsNullOrWhiteSpace(transactionBankCurrency) ||
                    !string.Equals(
                        transactionCariCurrency,
                        transactionBankCurrency,
                        StringComparison.OrdinalIgnoreCase))
                {
                    await tx.RollbackAsync();

                    return BadRequest(
                        "Cari hesap ile banka/kasa para birimi uyuşmuyor. İşlem kaydedilmedi.");
                }
            }
        }


        try
        {
            string currency;
            bool isCustomer;
            bool isSupplier;

            await using (var cmd = db.CreateCommand())
            {
                cmd.Transaction = (SqliteTransaction)tx;
                cmd.CommandText = @"
                    SELECT Currency, IsCustomer, IsSupplier
                    FROM CariAccounts
                    WHERE Id = @id AND IsActive = 1";
                cmd.Parameters.AddWithValue("@id", cariId);

                await using var reader = await cmd.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                    return BadRequest("Aktif cari hesap bulunamadı.");

                currency = reader.IsDBNull(0) ? "" : reader.GetString(0);
                isCustomer = !reader.IsDBNull(1) &&
                             Convert.ToInt64(reader.GetValue(1)) != 0;
                isSupplier = !reader.IsDBNull(2) &&
                             Convert.ToInt64(reader.GetValue(2)) != 0;
            }

            if (string.IsNullOrWhiteSpace(currency))
                return BadRequest("Cari hesabın para birimi tanımlı değil.");

            if ((movementType == "Sale" || movementType == "Collection") &&
                !isCustomer)
                return BadRequest("Bu işlem için müşteri hesabı seçilmelidir.");

            if ((movementType == "Purchase" || movementType == "Payment") &&
                !isSupplier)
                return BadRequest("Bu işlem için tedarikçi hesabı seçilmelidir.");

            if (hasBank)
            {
                await using var checkBank = db.CreateCommand();
                checkBank.Transaction = (SqliteTransaction)tx;
                checkBank.CommandText = @"
                    SELECT Currency
                    FROM MCBankCashAccounts
                    WHERE Id = @id AND IsActive = 1";
                checkBank.Parameters.AddWithValue(
                    "@id", bankAccountId!.Value);

                var bankCurrency =
                    Convert.ToString(await checkBank.ExecuteScalarAsync());

                if (string.IsNullOrWhiteSpace(bankCurrency) ||
                    !string.Equals(
                        currency.Trim(),
                        bankCurrency.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                    return BadRequest(
                        "Kasa/banka hesabı bulunamadı veya para birimi uyuşmuyor.");
            }

            var utc = DateTime.UtcNow.ToString(
                "O", CultureInfo.InvariantCulture);

            var date = DateTime.Today.ToString(
                "yyyy-MM-dd", CultureInfo.InvariantCulture);

            var actor = User.Identity?.Name ?? "Admin";

            long? bankMovementId = null;

            if (hasBank)
            {
                await using var bank = db.CreateCommand();
                bank.Transaction = (SqliteTransaction)tx;
                bank.CommandText = @"
                    INSERT INTO MCBankCashMovements
                    (
                        AccountId, MovementDate, Direction,
                        Amount, Description, ReferenceNumber,
                        RequestToken, CreatedAtUtc, CreatedBy
                    )
                    VALUES
                    (
                        @account, @date, @direction,
                        @amount, @description, @reference,
                        @token, @utc, @actor
                    );
                    SELECT last_insert_rowid();";

                bank.Parameters.AddWithValue(
                    "@account", bankAccountId!.Value);
                bank.Parameters.AddWithValue("@date", date);
                bank.Parameters.AddWithValue(
                    "@direction",
                    movementType == "Collection" ? "In" : "Out");
                bank.Parameters.AddWithValue(
                    "@amount",
                    value.ToString(CultureInfo.InvariantCulture));
                bank.Parameters.AddWithValue(
                    "@description", description?.Trim() ?? "");
                bank.Parameters.AddWithValue(
                    "@reference", referenceNumber?.Trim() ?? "");
                bank.Parameters.AddWithValue("@token", requestToken);
                bank.Parameters.AddWithValue("@utc", utc);
                bank.Parameters.AddWithValue("@actor", actor);

                bankMovementId = Convert.ToInt64(
                    await bank.ExecuteScalarAsync());
            }

            var debit = movementType == "Sale" ||
                        movementType == "Payment";

            await using (var cari = db.CreateCommand())
            {
                cari.Transaction = (SqliteTransaction)tx;
                cari.CommandText = @"
                    INSERT INTO MCCariMovements
                    (
                        CariAccountId, MovementDate, MovementType,
                        Debit, Credit, Currency, DueDate,
                        Description, ReferenceNumber, RequestToken,
                        RelatedBankMovementId, CreatedAtUtc, CreatedBy
                    )
                    VALUES
                    (
                        @cari, @date, @type,
                        @debit, @credit, @currency, @due,
                        @description, @reference, @token,
                        @bank, @utc, @actor
                    )";

                cari.Parameters.AddWithValue("@cari", cariId);
                cari.Parameters.AddWithValue("@date", date);
                cari.Parameters.AddWithValue("@type", movementType);
                cari.Parameters.AddWithValue(
                    "@debit",
                    debit ? value.ToString(CultureInfo.InvariantCulture) : "0");
                cari.Parameters.AddWithValue(
                    "@credit",
                    debit ? "0" : value.ToString(CultureInfo.InvariantCulture));
                // MC_CARI_MOVEMENT_TRANSACTION_GUARD_V5
                // Check the actual movement currency before its INSERT.
                string mcV5AccountCurrency;

                await using (var mcV5Check = db.CreateCommand())
                {
                    mcV5Check.Transaction =
                        (Microsoft.Data.Sqlite.SqliteTransaction)tx;

                    mcV5Check.CommandText = @"
                        SELECT Currency
                        FROM CariAccounts
                        WHERE Id = @id AND IsActive = 1";

                    mcV5Check.Parameters.AddWithValue("@id", cariId);

                    var mcV5Result =
                        await mcV5Check.ExecuteScalarAsync();

                    mcV5AccountCurrency =
                        Convert.ToString(mcV5Result)?.Trim() ?? "";
                }

                if (string.IsNullOrWhiteSpace(mcV5AccountCurrency) ||
                    string.IsNullOrWhiteSpace(currency) ||
                    !string.Equals(
                        mcV5AccountCurrency,
                        currency.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    await tx.RollbackAsync();

                    return BadRequest(
                        "Hareketin para birimi cari hesapla uyusmuyor. Kayit iptal edildi.");
                }

                cari.Parameters.AddWithValue("@currency", currency.Trim());
                cari.Parameters.AddWithValue(
                    "@due",
                    string.IsNullOrWhiteSpace(dueDate)
                        ? DBNull.Value
                        : (object)dueDate);
                cari.Parameters.AddWithValue(
                    "@description", description?.Trim() ?? "");
                cari.Parameters.AddWithValue(
                    "@reference", referenceNumber?.Trim() ?? "");
                cari.Parameters.AddWithValue("@token", requestToken);
                cari.Parameters.AddWithValue(
                    "@bank",
                    bankMovementId.HasValue
                        ? (object)bankMovementId.Value
                        : DBNull.Value);
                cari.Parameters.AddWithValue("@utc", utc);
                cari.Parameters.AddWithValue("@actor", actor);

                await cari.ExecuteNonQueryAsync();
            }

            // MC_CARI_BANK_RECONCILIATION_V7
            // Validate the exact newly inserted cari movement.
            if (movementType is "Collection" or "Payment")
            {
                if (!bankAccountId.HasValue)
                {
                    await tx.RollbackAsync();
                    return BadRequest("Banka/kasa hesabı zorunludur.");
                }

                await using var mcV7Check = db.CreateCommand();

                mcV7Check.Transaction =
                    (Microsoft.Data.Sqlite.SqliteTransaction)tx;

                mcV7Check.CommandText = @"
                    SELECT
                        cm.CariAccountId,
                        cm.MovementType,
                        cm.Debit,
                        cm.Credit,
                        cm.Currency,
                        bm.AccountId,
                        bm.Direction,
                        bm.Amount,
                        b.Currency
                    FROM MCCariMovements cm
                    INNER JOIN MCBankCashMovements bm
                        ON bm.Id = cm.RelatedBankMovementId
                    INNER JOIN MCBankCashAccounts b
                        ON b.Id = bm.AccountId
                    WHERE cm.RequestToken = @token
                    -- MC_CARI_UNIQUE_BANK_LINK_V8
                    AND cm.RelatedBankMovementId IS NOT NULL
                    AND (
                        SELECT COUNT(*)
                        FROM MCCariMovements other
                        WHERE other.RelatedBankMovementId =
                              cm.RelatedBankMovementId
                    ) = 1
                    LIMIT 1";

                mcV7Check.Parameters.AddWithValue(
                    "@token",
                    requestToken);

                await using var mcV7Reader =
                    await mcV7Check.ExecuteReaderAsync();

                if (!await mcV7Reader.ReadAsync())
                {
                    await mcV7Reader.CloseAsync();
                    await tx.RollbackAsync();

                    return BadRequest(
                        "Cari ve banka/kasa bağlantısı doğrulanamadı.");
                }

                string mcV7Text(int index) =>
                    mcV7Reader.IsDBNull(index)
                        ? ""
                        : Convert.ToString(
                            mcV7Reader.GetValue(index),
                            CultureInfo.InvariantCulture) ?? "";

                // Initialize separately to avoid C# definite-assignment errors.
                decimal mcV7Debit = 0m;
                decimal mcV7Credit = 0m;
                decimal mcV7BankAmount = 0m;

                bool mcV7NumbersOk =
                    decimal.TryParse(
                        mcV7Text(2),
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out mcV7Debit) &
                    decimal.TryParse(
                        mcV7Text(3),
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out mcV7Credit) &
                    decimal.TryParse(
                        mcV7Text(7),
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out mcV7BankAmount);

                bool mcV7Collection = movementType == "Collection";

                bool mcV7AmountsCorrect =
                    mcV7Collection
                        ? mcV7Debit == 0m &&
                          mcV7Credit == value &&
                          mcV7BankAmount == value
                        : mcV7Credit == 0m &&
                          mcV7Debit == value &&
                          mcV7BankAmount == value;

                bool mcV7DirectionCorrect =
                    string.Equals(
                        mcV7Text(6),
                        mcV7Collection ? "In" : "Out",
                        StringComparison.OrdinalIgnoreCase);

                bool mcV7AccountCorrect =
                    long.TryParse(
                        mcV7Text(0),
                        out var mcV7CariId) &&
                    long.TryParse(
                        mcV7Text(5),
                        out var mcV7BankId) &&
                    mcV7CariId == cariId &&
                    mcV7BankId == bankAccountId.Value;

                bool mcV7TypeCorrect =
                    string.Equals(
                        mcV7Text(1),
                        movementType,
                        StringComparison.Ordinal);

                bool mcV7CurrencyCorrect =
                    !string.IsNullOrWhiteSpace(mcV7Text(4)) &&
                    string.Equals(
                        mcV7Text(4).Trim(),
                        mcV7Text(8).Trim(),
                        StringComparison.OrdinalIgnoreCase);

                await mcV7Reader.CloseAsync();

                if (!mcV7NumbersOk ||
                    !mcV7AmountsCorrect ||
                    !mcV7DirectionCorrect ||
                    !mcV7AccountCorrect ||
                    !mcV7TypeCorrect ||
                    !mcV7CurrencyCorrect)
                {
                    await tx.RollbackAsync();

                    return BadRequest(
                        "Cari ve banka/kasa hareketleri mutabık değil. İşlem geri alındı.");
                }
            }
            // MC_CARI_BANK_UNIQUE_INDEX_V9
            // Enforce one-to-one bank movement links at SQLite level.
            // Index creation participates in the current transaction.
            if (movementType is "Collection" or "Payment")
            {
                await using var mcV9Index = db.CreateCommand();

                mcV9Index.Transaction =
                    (Microsoft.Data.Sqlite.SqliteTransaction)tx;

                mcV9Index.CommandText = @"
                    CREATE UNIQUE INDEX IF NOT EXISTS
                        UX_MCCariMovements_RelatedBankMovementId
                    ON MCCariMovements(RelatedBankMovementId)
                    WHERE RelatedBankMovementId IS NOT NULL";

                await mcV9Index.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();

            TempData["MCCariSuccess"] =
                "Cari işlem başarıyla kaydedildi.";

            return RedirectToAction(nameof(Index));
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            await tx.RollbackAsync();
            return Conflict(
                "Bu işlem daha önce kaydedilmiş olabilir veya kayıt kuralı ihlal edildi.");
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }
}

public sealed class MCCariLedgerPage
{
    public List<MCCariLedgerAccountOption> AccountOptions { get; set; } = new();
    public List<MCCariLedgerBankOption> BankOptions { get; set; } = new();
    public int CariCount { get; set; }
    public string Warning { get; set; } = "";
    public List<MCCariLedgerRow> Rows { get; set; } = new();
}

public sealed class MCCariLedgerRow
{
    public long Id { get; set; }
    public long CariId { get; set; }
    public string CariCode { get; set; } = "";
    public string CariName { get; set; } = "";
    public bool IsCustomer { get; set; }
    public bool IsSupplier { get; set; }
    public string AccountCurrency { get; set; } = "";
    public string Date { get; set; } = "";
    public string Type { get; set; } = "";
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string Currency { get; set; } = "";
    public string DueDate { get; set; } = "";
    public string Description { get; set; } = "";
    public string Reference { get; set; } = "";
}
public sealed class MCCariLedgerAccountOption
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Currency { get; set; } = "";
    public bool IsCustomer { get; set; }
    public bool IsSupplier { get; set; }
}

public sealed class MCCariLedgerBankOption
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Currency { get; set; } = "";
}