using MasterChefTakip.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Security.Claims;
using System.Text;

namespace MasterChefTakip.Controllers;

[Authorize(Policy = AppPermissions.ProductionManage)]
[Route("MCProductionCostHistory")]
public class MCProductionCostHistoryController : Controller
{
    private readonly IConfiguration _config;

    public MCProductionCostHistoryController(IConfiguration config)
    {
        _config = config;
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        var cs = _config.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException("Veritabani baglantisi yok.");

        var db = new SqliteConnection(cs);
        await db.OpenAsync();
        return db;
    }

    private static async Task EnsureTableAsync(SqliteConnection db)
    {
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS MCProductionCostSnapshots (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProductionOrderId INTEGER NOT NULL,
                Currency TEXT NOT NULL,
                PlannedQuantityKg TEXT NOT NULL,
                MaterialCost TEXT NOT NULL,
                LaborRate TEXT NOT NULL,
                OverheadRate TEXT NOT NULL,
                PackagingCost TEXT NOT NULL,
                ProfitRate TEXT NOT NULL,
                TotalCost TEXT NOT NULL,
                CostPerKg TEXT NOT NULL,
                SellingPricePerKg TEXT NOT NULL,
                MaterialsJson TEXT NOT NULL,
                CreatedBy TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_MCProductionCostSnapshots_Order
              ON MCProductionCostSnapshots(ProductionOrderId, Id DESC);
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    public sealed class CostInput
    {
        public int ProductionOrderId { get; set; }
        public string Currency { get; set; } = "";
        public decimal PlannedQuantityKg { get; set; }
        public decimal MaterialCost { get; set; }
        public decimal LaborRate { get; set; }
        public decimal OverheadRate { get; set; }
        public decimal PackagingCost { get; set; }
        public decimal ProfitRate { get; set; }
        public string MaterialsJson { get; set; } = "";
    }

    [HttpPost("Save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromForm] CostInput input)
    {
        if (input.ProductionOrderId <= 0 ||
            input.Currency is not ("TRY" or "USD" or "EUR") ||
            input.PlannedQuantityKg <= 0 ||
            input.MaterialCost < 0 ||
            input.LaborRate < 0 || input.LaborRate > 1000 ||
            input.OverheadRate < 0 || input.OverheadRate > 1000 ||
            input.PackagingCost < 0 ||
            input.ProfitRate < 0 || input.ProfitRate >= 100 ||
            input.MaterialsJson.Length > 100000)
        {
            return BadRequest("Maliyet bilgileri gecersiz.");
        }

        decimal total;
        decimal perKg;
        decimal selling;

        try
        {
            total = input.MaterialCost *
                    (1 + input.LaborRate / 100 +
                     input.OverheadRate / 100) +
                    input.PackagingCost;

            perKg = total / input.PlannedQuantityKg;
            selling = perKg / (1 - input.ProfitRate / 100);
        }
        catch (OverflowException)
        {
            return BadRequest("Maliyet degerleri cok buyuk.");
        }

        await using var db = await OpenAsync();

        // Yalnizca mevcut uretim emirlerine kayit ekle.
        await using (var check = db.CreateCommand())
        {
            check.CommandText =
                "SELECT COUNT(*) FROM ProductionOrders WHERE Id=@id";
            check.Parameters.AddWithValue("@id", input.ProductionOrderId);

            if (Convert.ToInt32(await check.ExecuteScalarAsync()) == 0)
                return NotFound("Uretim emri bulunamadi.");
        }

        await EnsureTableAsync(db);

        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO MCProductionCostSnapshots
            (ProductionOrderId, Currency, PlannedQuantityKg,
             MaterialCost, LaborRate, OverheadRate, PackagingCost,
             ProfitRate, TotalCost, CostPerKg, SellingPricePerKg,
             MaterialsJson, CreatedBy, CreatedAtUtc)
            VALUES
            (@order, @currency, @quantity,
             @material, @labor, @overhead, @packaging,
             @profit, @total, @perkg, @selling,
             @materials, @user, @created)
            """;

        string D(decimal value) =>
            value.ToString(CultureInfo.InvariantCulture);

        cmd.Parameters.AddWithValue("@order", input.ProductionOrderId);
        cmd.Parameters.AddWithValue("@currency", input.Currency);
        cmd.Parameters.AddWithValue("@quantity", D(input.PlannedQuantityKg));
        cmd.Parameters.AddWithValue("@material", D(input.MaterialCost));
        cmd.Parameters.AddWithValue("@labor", D(input.LaborRate));
        cmd.Parameters.AddWithValue("@overhead", D(input.OverheadRate));
        cmd.Parameters.AddWithValue("@packaging", D(input.PackagingCost));
        cmd.Parameters.AddWithValue("@profit", D(input.ProfitRate));
        cmd.Parameters.AddWithValue("@total", D(total));
        cmd.Parameters.AddWithValue("@perkg", D(perKg));
        cmd.Parameters.AddWithValue("@selling", D(selling));
        cmd.Parameters.AddWithValue("@materials", input.MaterialsJson);
        cmd.Parameters.AddWithValue("@user",
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown");
        cmd.Parameters.AddWithValue("@created",
            DateTime.UtcNow.ToString("O"));

        await cmd.ExecuteNonQueryAsync();

        return Redirect("/MCProductionCostHistory/" +
            input.ProductionOrderId);
    }

    [HttpGet("{orderId:int}")]
    public async Task<IActionResult> Index(int orderId)
    {
        if (orderId <= 0) return BadRequest();

        await using var db = await OpenAsync();
        await EnsureTableAsync(db);

        var html = new StringBuilder();

        html.Append("""
            <!doctype html><html lang="tr"><head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <title>Üretim Maliyet Geçmişi</title>
            <style>
            body{font-family:Arial,sans-serif;background:#f2f5f9;color:#193d5b;margin:0;padding:25px}
            main{max-width:1250px;margin:auto}
            .box{background:white;padding:22px;border:1px solid #dce5ed;border-radius:12px}
            table{width:100%;border-collapse:collapse;font-size:14px}
            th,td{padding:12px;border-bottom:1px solid #e3eaf1;text-align:left}
            th{background:#edf3f8}
            a{color:#17618f;text-decoration:none;font-weight:bold}
            .scroll{overflow-x:auto}
            </style></head><body><main>
            <a href="/Production/Orders">← Üretim Emirleri</a>
            <h1>Üretim Maliyet Geçmişi</h1>
            <div class="box"><div class="scroll"><table>
            <thead><tr>
            <th>Tarih</th><th>Para Birimi</th>
            <th>Hammadde</th><th>Toplam Maliyet</th>
            <th>Kg Maliyeti</th><th>Önerilen Kg Satış</th>
            <th>İşçilik %</th><th>Genel Gider %</th>
            <th>Ambalaj</th><th>Kâr %</th>
            </tr></thead><tbody>
            """);

        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT CreatedAtUtc,Currency,MaterialCost,TotalCost,
                   CostPerKg,SellingPricePerKg,LaborRate,
                   OverheadRate,PackagingCost,ProfitRate
            FROM MCProductionCostSnapshots
            WHERE ProductionOrderId=@id
            ORDER BY Id DESC LIMIT 100
            """;
        cmd.Parameters.AddWithValue("@id", orderId);

        await using var reader = await cmd.ExecuteReaderAsync();
        int count = 0;

        while (await reader.ReadAsync())
        {
            count++;
            html.Append("<tr>");
            for (int i = 0; i < 10; i++)
            {
                var value = System.Net.WebUtility.HtmlEncode(
                    Convert.ToString(reader.GetValue(i)) ?? "");
                html.Append("<td>").Append(value).Append("</td>");
            }
            html.Append("</tr>");
        }

        if (count == 0)
            html.Append("<tr><td colspan='10'>Henüz maliyet kaydı yok.</td></tr>");

        html.Append("</tbody></table></div></div></main></body></html>");
        return Content(html.ToString(), "text/html; charset=utf-8");
    }
}