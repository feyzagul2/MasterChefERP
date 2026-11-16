using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace MasterChefTakip.Controllers;

// MC_FINANCE_LINK_AUDIT_V10
[Authorize(Roles = "Admin")]
[Route("MCFinanceLinkAudit")]
public sealed class MCFinanceLinkAuditController : Controller
{
    private readonly IConfiguration _configuration;

    public MCFinanceLinkAuditController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var connectionString =
            _configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return StatusCode(
                503,
                "Finans veritabani baglantisi bulunamadi.");
        }

        try
        {
            var builder =
                new SqliteConnectionStringBuilder(connectionString)
                {
                    Mode = SqliteOpenMode.ReadOnly,
                    Cache = SqliteCacheMode.Private
                };

            await using var db =
                new SqliteConnection(builder.ToString());

            await db.OpenAsync();

            async Task<long> CountAsync(string sql)
            {
                await using var cmd = db.CreateCommand();
                cmd.CommandText = sql;

                var result = await cmd.ExecuteScalarAsync();

                return result == null || result == DBNull.Value
                    ? 0
                    : Convert.ToInt64(result);
            }

            var totalCariMovements = await CountAsync(
                "SELECT COUNT(*) FROM MCCariMovements");

            var totalBankMovements = await CountAsync(
                "SELECT COUNT(*) FROM MCBankCashMovements");

            var linkedCariMovements = await CountAsync(@"
                SELECT COUNT(*)
                FROM MCCariMovements
                WHERE RelatedBankMovementId IS NOT NULL");

            var duplicateBankLinks = await CountAsync(@"
                SELECT COUNT(*)
                FROM (
                    SELECT RelatedBankMovementId
                    FROM MCCariMovements
                    WHERE RelatedBankMovementId IS NOT NULL
                    GROUP BY RelatedBankMovementId
                    HAVING COUNT(*) > 1
                )");

            var missingBankMovements = await CountAsync(@"
                SELECT COUNT(*)
                FROM MCCariMovements cm
                LEFT JOIN MCBankCashMovements bm
                    ON bm.Id = cm.RelatedBankMovementId
                WHERE cm.RelatedBankMovementId IS NOT NULL
                  AND bm.Id IS NULL");

            var missingPaymentLinks = await CountAsync(@"
                SELECT COUNT(*)
                FROM MCCariMovements
                WHERE MovementType IN ('Collection', 'Payment')
                  AND RelatedBankMovementId IS NULL");

            var accountMismatches = await CountAsync(@"
                SELECT COUNT(*)
                FROM MCCariMovements cm
                JOIN MCBankCashMovements bm
                    ON bm.Id = cm.RelatedBankMovementId
                JOIN MCBankCashAccounts ba
                    ON ba.Id = bm.AccountId
                WHERE UPPER(TRIM(COALESCE(cm.Currency, ''))) <>
                      UPPER(TRIM(COALESCE(ba.Currency, '')))");

            var directionMismatches = await CountAsync(@"
                SELECT COUNT(*)
                FROM MCCariMovements cm
                JOIN MCBankCashMovements bm
                    ON bm.Id = cm.RelatedBankMovementId
                WHERE
                    (cm.MovementType = 'Collection'
                     AND UPPER(TRIM(COALESCE(bm.Direction, ''))) <> 'IN')
                    OR
                    (cm.MovementType = 'Payment'
                     AND UPPER(TRIM(COALESCE(bm.Direction, ''))) <> 'OUT')");

            var uniqueIndexExists = await CountAsync(@"
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type = 'index'
                  AND name = 'UX_MCCariMovements_RelatedBankMovementId'
                  AND tbl_name = 'MCCariMovements'
                  AND sql LIKE '%CREATE UNIQUE INDEX%'");

            var problems = new List<string>();

            if (duplicateBankLinks > 0)
                problems.Add("Mükerrer banka hareketi bağlantısı bulundu.");

            if (missingBankMovements > 0)
                problems.Add("Banka hareketi bulunamayan cari kayıtları var.");

            if (missingPaymentLinks > 0)
                problems.Add("Banka bağlantısı olmayan tahsilat/ödeme var.");

            if (accountMismatches > 0)
                problems.Add("Cari-banka para birimi uyuşmazlığı bulundu.");

            if (directionMismatches > 0)
                problems.Add("Tahsilat/ödeme yön uyuşmazlığı bulundu.");

            if (uniqueIndexExists == 0)
                problems.Add("V9 UNIQUE indeksi henüz oluşturulmamış.");

            return Json(new
            {
                module = "MASTER CHEF TAKIP",
                version = "MC_FINANCE_LINK_AUDIT_V10",
                mode = "ReadOnly",
                checkedAtUtc = DateTime.UtcNow,

                overview = new
                {
                    totalCariMovements,
                    totalBankMovements,
                    linkedCariMovements
                },

                integrity = new
                {
                    duplicateBankLinks,
                    missingBankMovements,
                    missingPaymentLinks,
                    accountMismatches,
                    directionMismatches
                },

                uniqueIndex = new
                {
                    name = "UX_MCCariMovements_RelatedBankMovementId",
                    exists = uniqueIndexExists > 0
                },

                status = problems.Count == 0
                    ? "OK"
                    : "ReviewRequired",

                warnings = problems
            });
        }
        catch (SqliteException)
        {
            return StatusCode(
                503,
                "Finansal bağlantı denetimi veritabanından okunamadı.");
        }
    }
}