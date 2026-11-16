using MasterChefTakip.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

[Authorize(Policy = AppPermissions.FinanceManage)]
[ApiController]
[Route("api/mc-executive-finance")]
public sealed class MCExecutiveFinanceApiController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public MCExecutiveFinanceApiController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var connectionString =
            _configuration.GetConnectionString("DefaultConnection")
            ?? "DataSource=app.db;Cache=Shared;Foreign Keys=True";

        var builder = new SqliteConnectionStringBuilder(connectionString)
        {
            Mode = SqliteOpenMode.ReadOnly
        };

        await using var connection =
            new SqliteConnection(builder.ConnectionString);

        await connection.OpenAsync(cancellationToken);

        var bankCash = new Dictionary<string, FinanceCurrency>(
            StringComparer.OrdinalIgnoreCase);

        var cari = new Dictionary<string, CariCurrency>(
            StringComparer.OrdinalIgnoreCase);

        var unknownAccountTypes = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        // ----------------------------------------------------
        // BANKA / KASA
        // Direction:
        // In  = +
        // Out = -
        //
        // Sadece gerçek hareketlerden hesaplanır.
        // Hareket yoksa 0 bakiye gerçek veri gibi sunulmaz.
        // ----------------------------------------------------

        const string bankSql = """
        SELECT
            COALESCE(a.AccountType, '') AS AccountType,
            COALESCE(a.Currency, '') AS Currency,
            COUNT(m.Id) AS MovementCount,
            COALESCE(SUM(
                CASE
                    WHEN LOWER(m.Direction) = 'in'
                        THEN m.Amount
                    WHEN LOWER(m.Direction) = 'out'
                        THEN -m.Amount
                    ELSE 0
                END
            ), 0) AS Balance
        FROM MCBankCashAccounts a
        LEFT JOIN MCBankCashMovements m
            ON m.AccountId = a.Id
        WHERE a.IsActive = 1
        GROUP BY
            a.AccountType,
            a.Currency
        ORDER BY
            a.Currency,
            a.AccountType;
        """;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = bankSql;

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var accountType = reader.GetString(0).Trim();
                var currency = NormalizeCurrency(reader.GetString(1));
                var movementCount = reader.GetInt64(2);
                var balance = Convert.ToDecimal(reader.GetValue(3));

                if (!bankCash.TryGetValue(currency, out var row))
                {
                    row = new FinanceCurrency(currency);
                    bankCash[currency] = row;
                }

                var normalizedType =
                    accountType.ToLowerInvariant();

                if (normalizedType.Contains("bank"))
                {
                    row.Bank += balance;
                    row.BankMovementCount += movementCount;
                }
                else if (
                    normalizedType.Contains("cash") ||
                    normalizedType.Contains("kasa"))
                {
                    row.Cash += balance;
                    row.CashMovementCount += movementCount;
                }
                else if (!string.IsNullOrWhiteSpace(accountType))
                {
                    unknownAccountTypes.Add(accountType);
                }
            }
        }

        // ----------------------------------------------------
        // CARI
        //
        // Mevcut sistem mantığı:
        // Bakiye = Debit - Credit
        //
        // Pozitif bakiye = Cari Alacak
        // Negatif bakiye = Cari Borç
        //
        // Önce her cari hesabın net bakiyesi hesaplanır,
        // sonra alacak/borç ayrılır.
        // ----------------------------------------------------

        const string cariSql = """
        WITH CariBalances AS
        (
            SELECT
                CariAccountId,
                COALESCE(Currency, '') AS Currency,
                COUNT(*) AS MovementCount,
                COALESCE(SUM(Debit - Credit), 0) AS Balance
            FROM MCCariMovements
            GROUP BY
                CariAccountId,
                Currency
        )
        SELECT
            Currency,
            SUM(MovementCount) AS MovementCount,
            COALESCE(SUM(
                CASE
                    WHEN Balance > 0 THEN Balance
                    ELSE 0
                END
            ), 0) AS Receivable,
            COALESCE(SUM(
                CASE
                    WHEN Balance < 0 THEN -Balance
                    ELSE 0
                END
            ), 0) AS Payable
        FROM CariBalances
        GROUP BY Currency
        ORDER BY Currency;
        """;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = cariSql;

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var currency = NormalizeCurrency(reader.GetString(0));

                cari[currency] = new CariCurrency(
                    currency,
                    reader.GetInt64(1),
                    Convert.ToDecimal(reader.GetValue(2)),
                    Convert.ToDecimal(reader.GetValue(3))
                );
            }
        }

        var currencies = bankCash.Keys
            .Concat(cari.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        var rows = new List<object>();

        foreach (var currency in currencies)
        {
            bankCash.TryGetValue(currency, out var bc);
            cari.TryGetValue(currency, out var ca);

            var bankHasData =
                bc is not null &&
                bc.BankMovementCount > 0;

            var cashHasData =
                bc is not null &&
                bc.CashMovementCount > 0;

            var cariHasData =
                ca is not null &&
                ca.MovementCount > 0;

            var bankCashHasAnyData =
                bankHasData || cashHasData;

            // Net pozisyon yalnızca banka/kasa ve cari tarafında
            // gerçek hareket verisi varsa yayınlanır.
            decimal? netPosition = null;

            if (bankCashHasAnyData && cariHasData)
            {
                netPosition =
                    (bc?.Bank ?? 0) +
                    (bc?.Cash ?? 0) +
                    (ca?.Receivable ?? 0) -
                    (ca?.Payable ?? 0);
            }

            rows.Add(new
            {
                currency,

                bank = bankHasData
                    ? bc!.Bank
                    : (decimal?)null,

                cash = cashHasData
                    ? bc!.Cash
                    : (decimal?)null,

                receivable = cariHasData
                    ? ca!.Receivable
                    : (decimal?)null,

                payable = cariHasData
                    ? ca!.Payable
                    : (decimal?)null,

                netPosition,

                bankMovementCount =
                    bc?.BankMovementCount ?? 0,

                cashMovementCount =
                    bc?.CashMovementCount ?? 0,

                cariMovementCount =
                    ca?.MovementCount ?? 0
            });
        }

        var totalBankCashMovements =
            bankCash.Values.Sum(x =>
                x.BankMovementCount +
                x.CashMovementCount);

        var totalCariMovements =
            cari.Values.Sum(x => x.MovementCount);

        return Ok(new
        {
            hasAnyFinanceMovement =
                totalBankCashMovements > 0 ||
                totalCariMovements > 0,

            hasBankCashMovements =
                totalBankCashMovements > 0,

            hasCariMovements =
                totalCariMovements > 0,

            bankCashMovementCount =
                totalBankCashMovements,

            cariMovementCount =
                totalCariMovements,

            currencies = rows,

            unknownAccountTypes =
                unknownAccountTypes
                    .OrderBy(x => x)
                    .ToArray(),

            generatedAtUtc =
                DateTime.UtcNow
        });
    }

    private static string NormalizeCurrency(string value)
    {
        var currency =
            (value ?? string.Empty)
            .Trim()
            .ToUpperInvariant();

        return string.IsNullOrWhiteSpace(currency)
            ? "TRY"
            : currency;
    }

    private sealed class FinanceCurrency
    {
        public FinanceCurrency(string currency)
        {
            Currency = currency;
        }

        public string Currency { get; }

        public decimal Bank { get; set; }

        public decimal Cash { get; set; }

        public long BankMovementCount { get; set; }

        public long CashMovementCount { get; set; }
    }

    private sealed record CariCurrency(
        string Currency,
        long MovementCount,
        decimal Receivable,
        decimal Payable
    );
}