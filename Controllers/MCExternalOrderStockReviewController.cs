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

// MC_EXTERNAL_ORDER_STOCK_REVIEW_V1
[Authorize(Roles = AppRoles.Admin)]
[Route("MCExternalOrders/stockReview")]
public sealed partial class MCExternalOrderStockReviewController : Controller
{
    private readonly ApplicationDbContext _db;

    public MCExternalOrderStockReviewController(
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

    private static bool Exists(
        SqliteConnection db,
        string table)
    {
        using var cmd = db.CreateCommand();

        cmd.CommandText =
            "SELECT COUNT(*) FROM sqlite_master " +
            "WHERE type='table' AND name=@name";

        cmd.Parameters.AddWithValue("@name", table);

        return Convert.ToInt64(
            cmd.ExecuteScalar() ?? 0L) > 0;
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
            "PRAGMA table_info(\"" + table + "\")";

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
            result.Add(reader.GetString(1));

        return result;
    }

    private static string? FindTable(
        SqliteConnection db,
        params string[] names)
    {
        foreach (var name in names)
        {
            if (Exists(db, name))
                return name;
        }

        return null;
    }

    private static string Page(string body)
    {
        return "<!doctype html><html lang='tr'><head>" +
            "<meta charset='utf-8'>" +
            "<meta name='viewport' content='width=device-width,initial-scale=1'>" +
            "<title>MASTER CHEF - Siparis Stok Kontrolu</title>" +
            "<style>" +
            "*{box-sizing:border-box}" +
            "body{font-family:Arial;background:#f1f5f9;" +
            "color:#172033;margin:0;padding:28px}" +
            "main{max-width:1450px;margin:auto}" +
            "h1{margin-bottom:8px}" +
            "section{background:white;border:1px solid #e2e8f0;" +
            "padding:22px;border-radius:12px;margin:20px 0}" +
            ".muted{color:#64748b}" +
            ".warn{background:#fff7ed;border:1px solid #fdba74;" +
            "padding:15px;border-radius:9px}" +
            ".good{background:#ecfdf5;border:1px solid #86efac;" +
            "padding:15px;border-radius:9px}" +
            ".scroll{overflow-x:auto}" +
            "table{width:100%;border-collapse:collapse}" +
            "th,td{text-align:left;padding:11px;" +
            "border-bottom:1px solid #e2e8f0;white-space:nowrap}" +
            "th{background:#eff6ff}" +
            ".button{display:inline-block;background:#183b5a;" +
            "color:white;padding:12px 17px;border-radius:8px;" +
            "text-decoration:none;margin:6px 8px 6px 0}" +
            "</style></head><body><main>" +
            body + "</main></body></html>";
    }

    private sealed record OrderLine(
        long Id,
        string OrderNumber,
        string? Customer,
        string ProductCode,
        string SourceQuantity,
        long? ProductId,
        string MatchStatus
    );

    private sealed record StockResult(
        string Status,
        decimal? Units,
        long MovementCount,
        long UnconvertibleCount
    );

    private static List<OrderLine> Orders(
        SqliteConnection db)
    {
        var result = new List<OrderLine>();

        using var cmd = db.CreateCommand();

        cmd.CommandText =
            "SELECT Id,ExternalOrderId,CustomerCode," +
            "ProductCode,Quantity,ProductId,MatchStatus " +
            "FROM MCExternalSyncLines " +
            "ORDER BY Id DESC LIMIT 300";

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            result.Add(new OrderLine(
                Convert.ToInt64(reader.GetValue(0)),
                reader.IsDBNull(1) ? "" : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? "" : reader.GetString(3),
                reader.IsDBNull(4)
                    ? ""
                    : Convert.ToString(
                        reader.GetValue(4),
                        CultureInfo.InvariantCulture) ?? "",
                reader.IsDBNull(5)
                    ? null
                    : Convert.ToInt64(reader.GetValue(5)),
                reader.IsDBNull(6) ? "" : reader.GetString(6)
            ));
        }

        return result;
    }

    private sealed record StockMapping(
        string? BatchTable,
        string? PalletTable,
        bool CanCalculate,
        string Explanation
    );

    private static StockMapping DetectStockMapping(
        SqliteConnection db)
    {
        var movementColumns = Columns(
            db, "StockMovements");

        if (!movementColumns.Contains("Type") ||
            !movementColumns.Contains("BoxCount") ||
            !movementColumns.Contains("LooseUnitCount") ||
            !movementColumns.Contains("UnitsPerBox"))
        {
            return new StockMapping(
                null, null, false,
                "Stok hareketlerinin gerekli miktar " +
                "alanlari bulunamadi.");
        }

        var batchTable = FindTable(
            db,
            "PackagingBatches",
            "PackagingBatch");

        if (batchTable == null)
        {
            return new StockMapping(
                null, null, false,
                "Paketleme partisi tablosu bulunamadi.");
        }

        var batchColumns = Columns(db, batchTable);

        if (!batchColumns.Contains("Id") ||
            !batchColumns.Contains("ProductId"))
        {
            return new StockMapping(
                batchTable, null, false,
                "Paketleme partisi ile urun karti " +
                "arasinda dogrudan ProductId eslesmesi yok.");
        }

        var palletTable = FindTable(
            db,
            "PalletItems",
            "PalletItem");

        bool direct =
            movementColumns.Contains("PackagingBatchId");

        bool viaPallet =
            movementColumns.Contains("PalletItemId") &&
            palletTable != null &&
            Columns(db, palletTable).Contains("Id") &&
            Columns(db, palletTable).Contains(
                "PackagingBatchId");

        if (!direct && !viaPallet)
        {
            return new StockMapping(
                batchTable, palletTable, false,
                "Stok hareketinden paketleme partisine " +
                "dogrulanabilir baglanti bulunamadi.");
        }

        return new StockMapping(
            batchTable,
            viaPallet ? palletTable : null,
            true,
            "Paketleme partisi ve stok hareketleri " +
            "uzerinden hesaplama yapilabilir.");
    }

    private static StockResult CalculateStock(
        SqliteConnection db,
        StockMapping mapping,
        long productId)
    {
        if (!mapping.CanCalculate ||
            mapping.BatchTable == null)
        {
            return new StockResult(
                "STOK HESAPLANAMIYOR",
                null, 0, 0);
        }

        var movementColumns = Columns(
            db, "StockMovements");

        bool direct = movementColumns.Contains(
            "PackagingBatchId");

        bool viaPallet = mapping.PalletTable != null;

        string joinPallet = "";

        if (viaPallet)
        {
            joinPallet =
                " LEFT JOIN \"" + mapping.PalletTable +
                "\" pi ON pi.Id=m.PalletItemId ";
        }

        string batchReference;

        if (direct && viaPallet)
        {
            batchReference =
                "COALESCE(m.PackagingBatchId," +
                "pi.PackagingBatchId)";
        }
        else if (direct)
        {
            batchReference = "m.PackagingBatchId";
        }
        else
        {
            batchReference = "pi.PackagingBatchId";
        }

        string sql =
            "SELECT " +
            "COUNT(*), " +
            "COALESCE(SUM(CASE " +
            "WHEN COALESCE(m.BoxCount,0)<>0 " +
            "AND (m.UnitsPerBox IS NULL " +
            "OR m.UnitsPerBox<=0) " +
            "THEN 1 ELSE 0 END),0), " +
            "COALESCE(SUM(CASE " +
            "WHEN COALESCE(m.BoxCount,0)<>0 " +
            "AND (m.UnitsPerBox IS NULL " +
            "OR m.UnitsPerBox<=0) THEN 0 " +
            "WHEN m.Type IN (1,3,6) THEN " +
            "(COALESCE(m.BoxCount,0)*" +
            "COALESCE(m.UnitsPerBox,0))+" +
            "COALESCE(m.LooseUnitCount,0) " +
            "WHEN m.Type IN (2,4,5) THEN -" +
            "((COALESCE(m.BoxCount,0)*" +
            "COALESCE(m.UnitsPerBox,0))+" +
            "COALESCE(m.LooseUnitCount,0)) " +
            "ELSE 0 END),0), " +
            "COALESCE(SUM(CASE " +
            "WHEN m.Type NOT IN (1,2,3,4,5,6) " +
            "OR m.Type IS NULL THEN 1 ELSE 0 END),0) " +
            "FROM StockMovements m " +
            joinPallet +
            "JOIN \"" + mapping.BatchTable +
            "\" b ON b.Id=" + batchReference + " " +
            "WHERE b.ProductId=@product";

        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;

        cmd.Parameters.AddWithValue(
            "@product", productId);

        using var reader = cmd.ExecuteReader();

        if (!reader.Read())
        {
            return new StockResult(
                "STOK VERISI YOK",
                null, 0, 0);
        }

        long movements =
            Convert.ToInt64(reader.GetValue(0));

        long unconvertible =
            Convert.ToInt64(reader.GetValue(1));

        decimal units = Convert.ToDecimal(
            reader.GetValue(2),
            CultureInfo.InvariantCulture);

        long unknownTypes =
            Convert.ToInt64(reader.GetValue(3));

        if (movements == 0)
        {
            return new StockResult(
                "STOK HAREKETI BULUNAMADI",
                null, 0, 0);
        }

        if (unconvertible > 0 || unknownTypes > 0)
        {
            return new StockResult(
                "EKSIK HAREKET VERISI",
                null,
                movements,
                unconvertible + unknownTypes);
        }

        return new StockResult(
            units < 0
                ? "NEGATIF BAKIYE - KONTROL"
                : "HESAPLANAN BAKIYE",
            units,
            movements,
            0);
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        if (_db.Database.ProviderName?.Contains(
            "Sqlite",
            StringComparison.OrdinalIgnoreCase) != true)
        {
            return StatusCode(
                500, "SQLite veritabani gerekli.");
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
                "<h1>MASTER CHEF - Siparis Stok Karsilastirma</h1>" +
                "<p class='muted'>Harici siparisler ile " +
                "kayitli mamul stok hareketleri arasindaki " +
                "urun bazli kontrol.</p>");

            if (!Exists(db, "MCExternalSyncLines"))
            {
                html.Append(
                    "<section><p>Henuz aktarilmis " +
                    "siparis bulunmuyor.</p>" +
                    "<a class='button' " +
                    "href='/MCExternalOrders/import'>" +
                    "Siparis Aktarimi</a></section>");

                return Content(
                    Page(html.ToString()),
                    "text/html; charset=utf-8");
            }

            var mapping = DetectStockMapping(db);
            var orders = Orders(db);

            html.Append(
                "<section><h2>Stok Hesaplama Durumu</h2>" +
                "<p>" + H(mapping.Explanation) + "</p>");

            if (!mapping.CanCalculate)
            {
                html.Append(
                    "<p class='warn'>Stok eslestirme semasi " +
                    "dogrulanamadigi icin stok miktari " +
                    "hesaplanmayacak. Yanlis stok bilgisi " +
                    "gosterilmeyecek.</p>");
            }

            html.Append("</section>");

            html.Append(
                "<p class='warn'><strong>Onemli:</strong> " +
                "CSV siparis miktarlarinin birimi henuz " +
                "dogrulanmadi. Hesaplanan stok adetleri " +
                "siparis miktarlariyla otomatik " +
                "karsilastirilmaz. Bu ekran sevkiyat " +
                "onayi veya stok tahsisi vermez.</p>");

            html.Append(
                "<section><h2>Siparis ve Mamul Stok Kontrolu</h2>" +
                "<p class='muted'>Son 300 aktarim satiri. " +
                "Stok bakiyesi paketleme partisine bagli " +
                "stok hareketlerinden hesaplanir. " +
                "Fiziksel sayim yerine gecmez.</p>" +
                "<div class='scroll'><table><thead><tr>" +
                "<th>Siparis No</th>" +
                "<th>Musteri</th>" +
                "<th>Urun Kodu</th>" +
                "<th>Siparis Miktari</th>" +
                "<th>Urun Kart ID</th>" +
                "<th>Urun Eslesmesi</th>" +
                "<th>Hesaplanan Stok (Adet)</th>" +
                "<th>Stok Kontrol Durumu</th>" +
                "</tr></thead><tbody>");

            var cache =
                new Dictionary<long, StockResult>();

            int matched = 0;
            int unmatched = 0;
            int stockCalculated = 0;
            int stockUnknown = 0;

            foreach (var line in orders)
            {
                bool productMatched =
                    line.ProductId.HasValue &&
                    line.MatchStatus == "ESLESTI";

                StockResult stock;

                if (!productMatched)
                {
                    unmatched++;

                    stock = new StockResult(
                        "URUN KARTI ESLESMEDI",
                        null, 0, 0);
                }
                else
                {
                    matched++;

                    long productId = line.ProductId!.Value;

                    if (!cache.TryGetValue(
                        productId, out stock!))
                    {
                        stock = CalculateStock(
                            db, mapping, productId);

                        cache[productId] = stock;
                    }

                    if (stock.Units.HasValue)
                        stockCalculated++;
                    else
                        stockUnknown++;
                }

                html.Append("<tr>");

                void Cell(object? value)
                {
                    html.Append("<td>");
                    html.Append(H(value));
                    html.Append("</td>");
                }

                Cell(line.OrderNumber);
                Cell(line.Customer);
                Cell(line.ProductCode);
                Cell(line.SourceQuantity);
                Cell(line.ProductId);
                Cell(line.MatchStatus);

                Cell(stock.Units.HasValue
                    ? stock.Units.Value.ToString(
                        "N0",
                        CultureInfo.GetCultureInfo("tr-TR"))
                    : "HESAPLANAMADI");

                Cell(stock.Status);

                html.Append("</tr>");
            }

            if (orders.Count == 0)
            {
                html.Append(
                    "<tr><td colspan='8'>" +
                    "Aktarilmis siparis satiri bulunmuyor." +
                    "</td></tr>");
            }

            html.Append("</tbody></table></div></section>");

            html.Append(
                "<section><h2>Kontrol Ozeti</h2>" +
                "<p>Gosterilen siparis satiri: <strong>" +
                orders.Count + "</strong></p>" +
                "<p>Urun karti eslesen satir: <strong>" +
                matched + "</strong></p>" +
                "<p>Urun karti eslesmeyen satir: <strong>" +
                unmatched + "</strong></p>" +
                "<p>Stok bakiyesi hesaplanabilen satir: <strong>" +
                stockCalculated + "</strong></p>" +
                "<p>Stok bakiyesi belirlenemeyen satir: <strong>" +
                stockUnknown + "</strong></p>" +
                "</section>");

            html.Append(
                "<section><h2>Islem Baglantilari</h2>" +
                "<a class='button' " +
                "href='/MCExternalOrders/dispatchQueue'>" +
                "Sevkiyat Hazirlik Listesi</a>" +
                "<a class='button' " +
                "href='/MCExternalOrders/reconcile'>" +
                "Siparis Mutabakati</a>" +
                "<a class='button' " +
                "href='/MCProductionPlanning/management'>" +
                "Yonetim Merkezi</a>" +
                "<p class='muted'>Bu rapor salt okunurdur. " +
                "Siparis, stok, sevkiyat veya finans " +
                "kaydi olusturmaz.</p></section>");

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