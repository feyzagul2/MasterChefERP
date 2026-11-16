using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace MasterChefTakip.ViewComponents;

// MC_FINANCE_INTEGRITY_V2
public sealed class MCFinanceDataStatusViewComponent : ViewComponent
{
    private readonly IConfiguration _configuration;

    public MCFinanceDataStatusViewComponent(
        IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var model = new MCFinanceDataStatusModel();

        var cs = _configuration.GetConnectionString(
            "DefaultConnection");

        if (string.IsNullOrWhiteSpace(cs))
        {
            model.Warning = "Veritabanı bağlantısı bulunamadı.";
            return View(model);
        }

        try
        {
            var builder = new SqliteConnectionStringBuilder(cs)
            {
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private
            };

            await using var db =
                new SqliteConnection(builder.ToString());

            await db.OpenAsync();

            async Task<bool> Exists(string table)
            {
                await using var cmd = db.CreateCommand();

                cmd.CommandText = @"
                    SELECT COUNT(*)
                    FROM sqlite_master
                    WHERE type='table' AND name=@name";

                cmd.Parameters.AddWithValue("@name", table);

                return Convert.ToInt64(
                    await cmd.ExecuteScalarAsync()) == 1;
            }

            async Task<long> Count(string sql)
            {
                await using var cmd = db.CreateCommand();

                cmd.CommandText = sql;

                return Convert.ToInt64(
                    await cmd.ExecuteScalarAsync());
            }

            var hasCari = await Exists("CariAccounts");
            var hasMovements = await Exists("MCCariMovements");
            var hasBank = await Exists("MCBankCashAccounts");
            var hasBankMovements =
                await Exists("MCBankCashMovements");
            var hasAudit = await Exists("MCCariDueDateAudit");

            model.CariAccounts = hasCari
                ? await Count("SELECT COUNT(*) FROM CariAccounts")
                : null;

            model.CariMovements = hasMovements
                ? await Count("SELECT COUNT(*) FROM MCCariMovements")
                : null;

            model.BankAccounts = hasBank
                ? await Count("SELECT COUNT(*) FROM MCBankCashAccounts")
                : null;

            model.BankMovements = hasBankMovements
                ? await Count("SELECT COUNT(*) FROM MCBankCashMovements")
                : null;

            model.DueAudit = hasAudit
                ? await Count("SELECT COUNT(*) FROM MCCariDueDateAudit")
                : null;

            if (hasCari && hasMovements)
            {
                model.OrphanCariMovements = await Count(@"
                    SELECT COUNT(*)
                    FROM MCCariMovements m
                    LEFT JOIN CariAccounts c
                        ON c.Id=m.CariAccountId
                    WHERE c.Id IS NULL");
                model.CariCurrencyMismatch = await Count(@"
                    SELECT COUNT(*)
                    FROM MCCariMovements m
                    JOIN CariAccounts c
                        ON c.Id = m.CariAccountId
                    WHERE m.MovementType IN
                          ('Sale','Purchase','Collection','Payment')
                      AND (
                          TRIM(COALESCE(m.Currency,'')) = ''
                          OR TRIM(COALESCE(c.Currency,'')) = ''
                          OR UPPER(TRIM(m.Currency))
                             <> UPPER(TRIM(c.Currency))
                      )");

                model.MissingDueDates = await Count(@"
                    SELECT COUNT(*)
                    FROM MCCariMovements
                    WHERE MovementType IN ('Sale','Purchase')
                      AND (
                          DueDate IS NULL
                          OR TRIM(DueDate)=''
                      )");

                model.DuplicateTokens = await Count(@"
                    SELECT COUNT(*)
                    FROM (
                        SELECT RequestToken
                        FROM MCCariMovements
                        WHERE RequestToken IS NOT NULL
                          AND TRIM(RequestToken)<>''
                        GROUP BY RequestToken
                        HAVING COUNT(*)>1
                    )");
            }

            if (hasMovements && hasBankMovements)
            {
                model.MissingBankLinks = await Count(@"
                    SELECT COUNT(*)
                    FROM MCCariMovements
                    WHERE MovementType IN ('Collection','Payment')
                      AND RelatedBankMovementId IS NULL");

                model.BrokenBankLinks = await Count(@"
                    SELECT COUNT(*)
                    FROM MCCariMovements m
                    LEFT JOIN MCBankCashMovements b
                        ON b.Id=m.RelatedBankMovementId
                    WHERE m.RelatedBankMovementId IS NOT NULL
                      AND b.Id IS NULL");

                model.MultipleBankLinks = await Count(@"
                    SELECT COUNT(*)
                    FROM (
                        SELECT RelatedBankMovementId
                        FROM MCCariMovements
                        WHERE RelatedBankMovementId IS NOT NULL
                        GROUP BY RelatedBankMovementId
                        HAVING COUNT(*)>1
                    )");
                if (hasBank)
                {
                    model.CurrencyMismatch = await Count(@"
                        SELECT COUNT(*)
                        FROM MCCariMovements m
                        JOIN MCBankCashMovements b
                            ON b.Id = m.RelatedBankMovementId
                        JOIN MCBankCashAccounts a
                            ON a.Id = b.AccountId
                        WHERE m.MovementType IN ('Collection','Payment')
                          AND (
                              TRIM(COALESCE(m.Currency,'')) = ''
                              OR TRIM(COALESCE(a.Currency,'')) = ''
                              OR UPPER(TRIM(m.Currency))
                                 <> UPPER(TRIM(a.Currency))
                          )");
                }
            }

            if (hasAudit && hasMovements)
            {
                model.OrphanAudit = await Count(@"
                    SELECT COUNT(*)
                    FROM MCCariDueDateAudit a
                    LEFT JOIN MCCariMovements m
                        ON m.Id=a.MovementId
                    WHERE m.Id IS NULL");

                model.AuditDateMismatch = await Count(@"
                    SELECT COUNT(*)
                    FROM MCCariDueDateAudit a
                    JOIN MCCariMovements m
                        ON m.Id=a.MovementId
                    WHERE a.Id=(
                        SELECT MAX(a2.Id)
                        FROM MCCariDueDateAudit a2
                        WHERE a2.MovementId=a.MovementId
                    )
                    AND COALESCE(m.DueDate,'')
                        <> COALESCE(a.NewDueDate,'')");
            }

            model.DatabaseAvailable = true;
        }
        catch (Exception ex) when (
            ex is SqliteException ||
            ex is ArgumentException ||
            ex is InvalidOperationException ||
            ex is FormatException)
        {
            model.DatabaseAvailable = false;
            model.Warning =
                "Finans tutarlılık kontrolü tamamlanamadı. " +
                "Veritabanı bağlantısı veya tablo yapısı kontrol edilmeli.";
        }

        return View(model);
    }
}

public sealed class MCFinanceDataStatusModel
{
    public bool DatabaseAvailable { get; set; }

    public long? CariAccounts { get; set; }
    public long? CariMovements { get; set; }
    public long? BankAccounts { get; set; }
    public long? BankMovements { get; set; }
    public long? DueAudit { get; set; }

    public long? OrphanCariMovements { get; set; }
    public long? MissingDueDates { get; set; }
    public long? DuplicateTokens { get; set; }

    public long? MissingBankLinks { get; set; }
    public long? BrokenBankLinks { get; set; }
    public long? MultipleBankLinks { get; set; }
    public long? CurrencyMismatch { get; set; }
    // MC_FINANCE_CARI_CURRENCY_V4
    public long? CariCurrencyMismatch { get; set; } // MC_FINANCE_CURRENCY_CHECK_V3

    public long? OrphanAudit { get; set; }
    public long? AuditDateMismatch { get; set; }

    public string Warning { get; set; } = "";
}