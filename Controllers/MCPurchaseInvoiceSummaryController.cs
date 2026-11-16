using MasterChefTakip.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace MasterChefTakip.Controllers;

// MC_INVOICE_LIVE_SUMMARY_V1
[Authorize(Policy = AppPermissions.PurchaseManage)]
[Route("MCPurchaseInvoiceSummary")]
public class MCPurchaseInvoiceSummaryController : Controller
{
    private readonly IConfiguration _config;

    public MCPurchaseInvoiceSummaryController(IConfiguration config)
    {
        _config = config;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (!User.IsInRole("Admin"))
            return Forbid();

        var cs = _config.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(cs))
            return StatusCode(503, new { error = "Veritabani baglantisi tanimli degil." });

        var builder = new SqliteConnectionStringBuilder(cs)
        {
            Mode = SqliteOpenMode.ReadOnly
        };

        await using var con = new SqliteConnection(builder.ToString());
        await con.OpenAsync();

        await using var cmd = con.CreateCommand();
        cmd.CommandText = """
            SELECT
                COALESCE(Status, ''),
                COALESCE(Currency, ''),
                COUNT(*),
                COALESCE(SUM(CAST(GrandTotal AS REAL)), 0)
            FROM MCPurchaseInvoices
            GROUP BY Status, Currency
            """;

        var total = 0L;
        var draft = 0L;
        var finalized = 0L;
        var other = 0L;

        var totals = new Dictionary<string, decimal>(
            StringComparer.OrdinalIgnoreCase);

        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var status = reader.GetString(0).Trim().ToLowerInvariant();
            var currency = reader.GetString(1).Trim().ToUpperInvariant();

            if (currency.Length == 0)
                currency = "BELIRTILMEMIS";

            var count = reader.GetInt64(2);
            var amount = Convert.ToDecimal(
                reader.GetValue(3), CultureInfo.InvariantCulture);

            total += count;

            if (status is "draft" or "taslak")
                draft += count;
            else if (status is "finalized" or "posted" or "kesinlesmis" or "kesinleşmiş")
                finalized += count;
            else
                other += count;

            if (!totals.ContainsKey(currency))
                totals[currency] = 0;

            totals[currency] += amount;
        }

        Response.Headers.CacheControl = "no-store";

        return Json(new
        {
            total,
            draft,
            finalized,
            other,
            totals
        });
    }
}