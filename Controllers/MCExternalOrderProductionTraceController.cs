using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using MasterChefTakip.Data;
using MasterChefTakip.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

// MC_EXTERNAL_PRODUCTION_TRACE_V1
[Authorize(Roles = AppRoles.Admin)]
[Route("MCExternalOrders/productionTrace")]
public sealed class MCExternalOrderProductionTraceController : Controller
{
    private readonly ApplicationDbContext _db;

    public MCExternalOrderProductionTraceController(
        ApplicationDbContext db)
    {
        _db = db;
    }

    private static string H(object? value)
    {
        return WebUtility.HtmlEncode(
            Convert.ToString(
                value,
                CultureInfo.InvariantCulture) ?? "");
    }

    private static string Q(string value)
    {
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static bool Exists(
        SqliteConnection db,
        string table)
    {
        using var cmd = db.CreateCommand();

        cmd.CommandText =
            "SELECT COUNT(*) FROM sqlite_master " +
            "WHERE type='table' AND name=@name";

        cmd.Parameters.AddWithValue("@name", table);

        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    private static HashSet<string> Columns(
        SqliteConnection db,
        string table)
    {
        var result = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        if (!Exists(db, table))
            return result;

        using var cmd = db.CreateCommand();

        cmd.CommandText =
            "PRAGMA table_info(" + Q(table) + ")";

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
            result.Add(reader.GetString(1));

        return result;
    }

    private static string? FindTable(
        SqliteConnection db,
        string[] candidates,
        params string[] required)
    {
        foreach (var name in candidates)
        {
            var columns = Columns(db, name);

            if (required.All(columns.Contains))
                return name;
        }

        return null;
    }

    private static long Count(
        SqliteConnection db,
        string sql,
        string parameter,
        long value)
    {
        using var cmd = db.CreateCommand();

        cmd.CommandText = sql;

        cmd.Parameters.AddWithValue(parameter, value);

        return Convert.ToInt64(
            cmd.ExecuteScalar() ?? 0L);
    }

    private static string Page(string body)
    {
        return "<!doctype html><html lang='tr'><head>" +
            "<meta charset='utf-8'>" +
            "<meta name='viewport' " +
            "content='width=device-width,initial-scale=1'>" +
            "<title>MASTER CHEF - Uretim Paketleme Izleme</title>" +
            "<style>" +
            "*{box-sizing:border-box}" +
            "body{font-family:Arial;background:#f1f5f9;" +
            "color:#172033;margin:0;padding:26px}" +
            "main{max-width:1450px;margin:auto}" +
            "section{background:white;border:1px solid #e2e8f0;" +
            "border-radius:12px;padding:22px;margin:20px 0}" +
            ".muted{color:#64748b}" +
            ".warn{background:#fff7ed;border:1px solid #fdba74;" +
            "padding:14px;border-radius:8px}" +
            ".good{color:#166534;font-weight:bold}" +
            ".scroll{overflow-x:auto}" +
            "table{width:100%;border-collapse:collapse}" +
            "th,td{padding:12px;text-align:left;" +
            "border-bottom:1px solid #e2e8f0;white-space:nowrap}" +
            "th{background:#eff6ff}" +
            ".button{display:inline-block;background:#183b5a;" +
            "color:white;text-decoration:none;border-radius:8px;" +
            "padding:12px 17px;margin:5px 10px 5px 0}" +
            "</style></head><body><main>" +
            body + "</main></body></html>";
    }

    private sealed record ProductionLink(
        string ExternalOrderId,
        long ProductionOrderId,
        long ProductId);

    private static List<ProductionLink> Links(
        SqliteConnection db)
    {
        var result = new List<ProductionLink>();

        using var cmd = db.CreateCommand();

        cmd.CommandText = """
            SELECT ExternalOrderId,
                   ProductionOrderId,
                   ProductId
            FROM MCExternalProductionOrderLinks
            ORDER BY ProductionOrderId DESC
            LIMIT 300
            """;

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            result.Add(new ProductionLink(
                reader.GetString(0),
                Convert.ToInt64(reader.GetValue(1)),
                Convert.ToInt64(reader.GetValue(2))
            ));
        }

        return result;
    }

    private sealed record ProductionState(
        bool Found,
        string Status,
        decimal? PlannedKg,
        long? ActualProductId);

    private static ProductionState ReadProduction(
        SqliteConnection db,
        string orders,
        string recipes,
        long orderId)
    {
        using var cmd = db.CreateCommand();

        cmd.CommandText =
            "SELECT o.Status,o.PlannedQuantityKg,r.ProductId " +
            "FROM " + Q(orders) + " o " +
            "LEFT JOIN " + Q(recipes) +
            " r ON r.Id=o.RecipeId " +
            "WHERE o.Id=@id LIMIT 1";

        cmd.Parameters.AddWithValue("@id", orderId);

        using var reader = cmd.ExecuteReader();

        if (!reader.Read())
        {
            return new ProductionState(
                false, "URETIM EMRI BULUNAMADI", null, null);
        }

        string status = reader.IsDBNull(0)
            ? "DURUM BELIRSIZ"
            : Convert.ToString(
                reader.GetValue(0),
                CultureInfo.InvariantCulture) ?? "";

        decimal? kg = reader.IsDBNull(1)
            ? null
            : Convert.ToDecimal(
                reader.GetValue(1),
                CultureInfo.InvariantCulture);

        long? product = reader.IsDBNull(2)
            ? null
            : Convert.ToInt64(reader.GetValue(2));

        return new ProductionState(
            true, status, kg, product);
    }

    private static long LinkedBatches(
        SqliteConnection db,
        string linkTable,
        long productionOrderId)
    {
        return Count(db,
            "SELECT COUNT(DISTINCT PackagingBatchId) " +
            "FROM " + Q(linkTable) +
            " WHERE ProductionOrderId=@id " +
            "AND PackagingBatchId IS NOT NULL",
            "@id",
            productionOrderId);
    }

    private static long ReceiptMovements(
        SqliteConnection db,
        string linkTable,
        long productionOrderId)
    {
        return Count(db,
            "SELECT COUNT(*) " +
            "FROM StockMovements m " +
            "WHERE m.Type=1 " +
            "AND m.PackagingBatchId IN (" +
            "SELECT DISTINCT PackagingBatchId " +
            "FROM " + Q(linkTable) + " " +
            "WHERE ProductionOrderId=@id " +
            "AND PackagingBatchId IS NOT NULL)",
            "@id",
            productionOrderId);
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        if (_db.Database.ProviderName?.Contains(
            "Sqlite",
            StringComparison.OrdinalIgnoreCase) != true)
        {
            return StatusCode(
                500,
                "SQLite veritabani gerekli.");
        }

        var db = (SqliteConnection)
            _db.Database.GetDbConnection();

        bool openedHere =
            db.State != ConnectionState.Open;

        if (openedHere)
            _db.Database.OpenConnection();

        try
        {
            var html = new StringBuilder();

            html.Append(
                "<h1>MASTER CHEF - Siparis / Uretim / Paketleme</h1>" +
                "<p class='muted'>Harici siparisten uretime, " +
                "paketlemeye ve mamul stok girisine kadar " +
                "kayitli baglantilarin izlenmesi.</p>");

            if (!Exists(db, "MCExternalProductionOrderLinks"))
            {
                html.Append(
                    "<section><p>Henuz kayitli siparis-" +
                    "uretim emri baglantisi bulunmuyor.</p>" +
                    "<a class='button' " +
                    "href='/MCExternalOrders/productionLinks'>" +
                    "Siparis Uretim Baglantisi</a></section>");

                return Content(
                    Page(html.ToString()),
                    "text/html; charset=utf-8");
            }

            var productionTable = FindTable(
                db,
                new[]
                {
                    "ProductionOrders",
                    "ProductionOrder"
                },
                "Id",
                "RecipeId",
                "Status",
                "PlannedQuantityKg");

            var recipeTable = FindTable(
                db,
                new[]
                {
                    "ProductionRecipes",
                    "ProductionRecipe",
                    "Recipes"
                },
                "Id",
                "ProductId");

            var packagingLinkTable = FindTable(
                db,
                new[]
                {
                    "ProductionPackagingLinks",
                    "ProductionPackagingLink"
                },
                "ProductionOrderId",
                "PackagingBatchId");

            var movements = Columns(db, "StockMovements");

            bool receiptAvailable =
                packagingLinkTable != null &&
                movements.Contains("Type") &&
                movements.Contains("PackagingBatchId");

            bool productionAvailable =
                productionTable != null &&
                recipeTable != null;

            html.Append(
                "<p class='warn'><strong>Kontrol mantigi:</strong> " +
                "Uretim emrinin tamamlanmasi, paketleme " +
                "partisi baglantisi ve mamul stok girisi " +
                "ayri ayri incelenir. " +
                "Stok girisi bulunmasi urunun halen " +
                "depoda mevcut oldugunu kanitlamaz. " +
                "Bu ekran sevkiyat emri olusturmaz.</p>");

            if (!productionAvailable)
            {
                html.Append(
                    "<p class='warn'>Uretim emri veya recete " +
                    "tablosu dogrulanamadi. Uretim durumu " +
                    "bilinmiyor olarak gosterilecek.</p>");
            }

            if (packagingLinkTable == null)
            {
                html.Append(
                    "<p class='warn'>Uretim-paketleme " +
                    "baglanti tablosu bulunamadi. " +
                    "Paketleme durumu hesaplanmayacak.</p>");
            }

            if (!receiptAvailable)
            {
                html.Append(
                    "<p class='warn'>Paketleme partisine bagli " +
                    "mamul stok girisi kayitlari " +
                    "dogrulanamadi.</p>");
            }

            var links = Links(db);

            int completed = 0;
            int packagingFound = 0;
            int receiptFound = 0;
            int requiresReview = 0;

            html.Append(
                "<section><h2>Uretimden Paketlemeye Takip</h2>" +
                "<p class='muted'>Son 300 kayitli " +
                "siparis-uretim baglantisi gosterilir.</p>" +
                "<div class='scroll'><table><thead><tr>" +
                "<th>Harici Siparis No</th>" +
                "<th>Urun Kart ID</th>" +
                "<th>Uretim Emri ID</th>" +
                "<th>Uretim Durumu</th>" +
                "<th>Planlanan Kg</th>" +
                "<th>Bagli Paketleme Partisi</th>" +
                "<th>Mamul Stok Giris Hareketi</th>" +
                "<th>Izleme Durumu</th>" +
                "</tr></thead><tbody>");

            foreach (var link in links)
            {
                ProductionState production = productionAvailable
                    ? ReadProduction(
                        db,
                        productionTable!,
                        recipeTable!,
                        link.ProductionOrderId)
                    : new ProductionState(
                        false,
                        "URETIM TABLOSU DOGRULANAMADI",
                        null,
                        null);

                long? batchCount = packagingLinkTable != null
                    ? LinkedBatches(
                        db,
                        packagingLinkTable,
                        link.ProductionOrderId)
                    : null;

                long? receiptCount = receiptAvailable
                    ? ReceiptMovements(
                        db,
                        packagingLinkTable!,
                        link.ProductionOrderId)
                    : null;

                bool validProduct =
                    production.Found &&
                    production.ActualProductId == link.ProductId;

                bool isCompleted =
                    production.Status.Equals(
                        "Completed",
                        StringComparison.OrdinalIgnoreCase);

                if (isCompleted && validProduct)
                    completed++;

                if (batchCount.GetValueOrDefault() > 0)
                    packagingFound++;

                if (receiptCount.GetValueOrDefault() > 0)
                    receiptFound++;

                string status;

                if (!productionAvailable)
                {
                    status = "URETIM BILGISI DOGRULANAMADI";
                }
                else if (!production.Found)
                {
                    status = "URETIM EMRI BULUNAMADI";
                }
                else if (!validProduct)
                {
                    status = "URUN KARTI UYUSMAZLIGI";
                }
                else if (!isCompleted)
                {
                    status = "URETIM TAMAMLANMASI BEKLENIYOR";
                }
                else if (!batchCount.HasValue)
                {
                    status = "PAKETLEME BAGLANTISI BILINMIYOR";
                }
                else if (batchCount.Value == 0)
                {
                    status = "PAKETLEME BAGLANTISI YOK";
                }
                else if (!receiptCount.HasValue)
                {
                    status = "MAMUL GIRISI DOGRULANAMIYOR";
                }
                else if (receiptCount.Value == 0)
                {
                    status = "MAMUL STOK GIRISI BULUNAMADI";
                }
                else
                {
                    status = "URETIM-PAKETLEME-GIRIS IZLENEBILIYOR";
                }

                if (status !=
                    "URETIM-PAKETLEME-GIRIS IZLENEBILIYOR")
                {
                    requiresReview++;
                }

                html.Append("<tr>");

                void Cell(object? value)
                {
                    html.Append("<td>");
                    html.Append(H(value));
                    html.Append("</td>");
                }

                Cell(link.ExternalOrderId);
                Cell(link.ProductId);
                Cell(link.ProductionOrderId);
                Cell(production.Status);

                Cell(production.PlannedKg.HasValue
                    ? production.PlannedKg.Value.ToString(
                        "N2",
                        CultureInfo.GetCultureInfo("tr-TR"))
                    : "BILINMIYOR");

                Cell(batchCount.HasValue
                    ? batchCount.Value.ToString()
                    : "BILINMIYOR");

                Cell(receiptCount.HasValue
                    ? receiptCount.Value.ToString()
                    : "BILINMIYOR");

                Cell(status);

                html.Append("</tr>");
            }

            if (links.Count == 0)
            {
                html.Append(
                    "<tr><td colspan='8'>" +
                    "Kayitli siparis-uretim baglantisi yok." +
                    "</td></tr>");
            }

            html.Append("</tbody></table></div></section>");

            html.Append(
                "<section><h2>Izleme Ozeti</h2>" +
                "<p>Listelenen siparis-uretim baglantisi: " +
                "<strong>" + links.Count + "</strong></p>" +
                "<p>Uretimi tamamlanmis ve urunu eslesen: " +
                "<strong>" + completed + "</strong></p>" +
                "<p>Paketleme partisi baglantisi bulunan: " +
                "<strong>" + packagingFound + "</strong></p>" +
                "<p>Mamul stok giris hareketi bulunan: " +
                "<strong>" + receiptFound + "</strong></p>" +
                "<p>Kontrol gerektiren baglanti: " +
                "<strong>" + requiresReview + "</strong></p>" +
                "</section>");

            html.Append(
                "<section><h2>Ilgili Islemler</h2>" +
                "<a class='button' " +
                "href='/MCExternalOrders/productionLinks'>" +
                "Siparis Uretim Baglantilari</a>" +
                "<a class='button' " +
                "href='/MCExternalOrders/stockReview/productionCoverage'>" +
                "Uretim Planlama</a>" +
                "<a class='button' " +
                "href='/MCExternalOrders/dispatchQueue'>" +
                "Sevkiyat Hazirlik Listesi</a>" +
                "<a class='button' " +
                "href='/MCProductionPlanning/management'>" +
                "Yonetim Merkezi</a>" +
                "<p class='muted'>Bu ekran yalnizca mevcut " +
                "kayitlari okur. Siparis, uretim, stok, " +
                "sevkiyat veya finans hareketi olusturmaz.</p>" +
                "</section>");

            return Content(
                Page(html.ToString()),
                "text/html; charset=utf-8");
        }
        finally
        {
            if (openedHere)
                _db.Database.CloseConnection();
        }
    }
}