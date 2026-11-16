using MasterChefTakip.Models;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

namespace MasterChefTakip.Controllers;

[Authorize(Policy = AppPermissions.FinanceManage)]
[ApiController]
[Route("api/mc-executive-cari-summary")]
public sealed class MCExecutiveCariSummaryApiController : ControllerBase
{
    private readonly IConfiguration _configuration;
    public MCExecutiveCariSummaryApiController(IConfiguration configuration) => _configuration = configuration;

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var cs = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs)) return StatusCode(500);

        var b = new SqliteConnectionStringBuilder(cs) { Mode = SqliteOpenMode.ReadOnly };
        await using var db = new SqliteConnection(b.ToString());
        await db.OpenAsync();

        var movementCount = await Count(db, "SELECT COUNT(*) FROM MCCariMovements");
        var invalidCount = await Count(db, "SELECT COUNT(*) FROM MCCariMovements WHERE Debit < 0 OR Credit < 0 OR ((Debit > 0) = (Credit > 0))");
        var today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var d7 = DateTime.Today.AddDays(7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var d30 = DateTime.Today.AddDays(30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var overdue = await Count(db, "SELECT COUNT(*) FROM MCCariMovements WHERE DueDate IS NOT NULL AND TRIM(DueDate) <> '' AND date(DueDate) < date(@d)", ("@d", today));
        var due7 = await Count(db, "SELECT COUNT(*) FROM MCCariMovements WHERE DueDate IS NOT NULL AND TRIM(DueDate) <> '' AND date(DueDate) BETWEEN date(@d) AND date(@x)", ("@d", today), ("@x", d7));
        var due30 = await Count(db, "SELECT COUNT(*) FROM MCCariMovements WHERE DueDate IS NOT NULL AND TRIM(DueDate) <> '' AND date(DueDate) BETWEEN date(@d) AND date(@x)", ("@d", today), ("@x", d30));
        var collections = await Count(db, "SELECT COUNT(*) FROM MCCariMovements WHERE MovementType='Collection'");
        var payments = await Count(db, "SELECT COUNT(*) FROM MCCariMovements WHERE MovementType='Payment'");

        var currencies = new List<object>();
        if (invalidCount == 0)
        {
            await using var q = db.CreateCommand();
            q.CommandText = """
                WITH B AS (
                    SELECT CariAccountId,
                           COALESCE(NULLIF(TRIM(Currency),''),'BELİRSİZ') Currency,
                           SUM(Debit-Credit) Balance
                    FROM MCCariMovements
                    GROUP BY CariAccountId, COALESCE(NULLIF(TRIM(Currency),''),'BELİRSİZ')
                )
                SELECT Currency,
                       SUM(CASE WHEN Balance>0 THEN Balance ELSE 0 END),
                       SUM(CASE WHEN Balance<0 THEN -Balance ELSE 0 END)
                FROM B GROUP BY Currency ORDER BY Currency
                """;
            await using var r = await q.ExecuteReaderAsync();
            while (await r.ReadAsync())
                currencies.Add(new {
                    currency = r.GetString(0),
                    receivable = Convert.ToDecimal(r.GetValue(1), CultureInfo.InvariantCulture),
                    payable = Convert.ToDecimal(r.GetValue(2), CultureInfo.InvariantCulture)
                });
        }

        var recent = new List<object>();
        await using (var q = db.CreateCommand())
        {
            q.CommandText = """
                SELECT m.MovementDate,m.MovementType,m.Debit,m.Credit,
                       COALESCE(NULLIF(TRIM(m.Currency),''),'BELİRSİZ'),
                       COALESCE(m.DueDate,''),COALESCE(c.Name,''),COALESCE(m.ReferenceNumber,'')
                FROM MCCariMovements m
                INNER JOIN CariAccounts c ON c.Id=m.CariAccountId
                ORDER BY m.MovementDate DESC,m.Id DESC LIMIT 8
                """;
            await using var r = await q.ExecuteReaderAsync();
            while (await r.ReadAsync())
                recent.Add(new {
                    movementDate = Convert.ToString(r.GetValue(0), CultureInfo.InvariantCulture) ?? "",
                    movementType = r.GetString(1),
                    debit = Convert.ToDecimal(r.GetValue(2), CultureInfo.InvariantCulture),
                    credit = Convert.ToDecimal(r.GetValue(3), CultureInfo.InvariantCulture),
                    currency = r.GetString(4),
                    dueDate = r.GetString(5),
                    cariName = r.GetString(6),
                    reference = r.GetString(7)
                });
        }

        return Ok(new {
            hasData = movementCount > 0,
            isValid = invalidCount == 0,
            warning = invalidCount == 0 ? null : $"{invalidCount} hatalı cari hareket bulundu. Toplamlar gizlendi.",
            movementCount, invalidCount,
            overdueDueMovementCount = overdue,
            dueWithin7DaysCount = due7,
            dueWithin30DaysCount = due30,
            collectionCount = collections,
            paymentCount = payments,
            currencies, recent
        });
    }

    private static async Task<int> Count(SqliteConnection db, string sql, params (string Name,string Value)[] p)
    {
        await using var q = db.CreateCommand();
        q.CommandText = sql;
        foreach (var x in p) q.Parameters.AddWithValue(x.Name, x.Value);
        return Convert.ToInt32(await q.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }
}
