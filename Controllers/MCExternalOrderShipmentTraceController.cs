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

// MC_EXTERNAL_SHIPMENT_TRACE_V1
[Authorize(Roles = AppRoles.Admin)]
[Route("MCExternalOrders/shipmentTrace")]
public sealed class MCExternalOrderShipmentTraceController : Controller
{
    private readonly ApplicationDbContext _db;

    public MCExternalOrderShipmentTraceController(
        ApplicationDbContext db)
    {
        _db = db;
    }

    private static string H(object? value) =>
        WebUtility.HtmlEncode(
            Convert.ToString(
                value,
                CultureInfo.InvariantCulture) ?? "");

    private static string Q(string value) =>
        "\"" + value.Replace("\"", "\"\"") + "\"";

    private static bool Exists(SqliteConnection db, string table)
    {
        using var cmd = db.CreateCommand();

        cmd.CommandText =
            "SELECT COUNT(*) FROM sqlite_master " +
            "WHERE type='table' AND name=@name";

        cmd.Parameters.AddWithValue("@name", table);

        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    private static HashSet<string> Columns(
        SqliteConnection db, string table)
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

    private static string Page(string body) =>
        "<!doctype html><html lang='tr'><head>" +
        "<meta charset='utf-8'>" +
        "<meta name='viewport' content='width=device-width,initial-scale=1'>" +
        "<title>MASTER CHEF - Sevkiyat Hareket Takibi</title>" +
        "<style>" +
        "*{box-sizing:border-box}" +
        "body{font-family:Arial;background:#f1f5f9;" +
        "color:#172033;margin:0;padding:26px}" +
        "main{max-width:1450px;margin:auto}" +
        "section{background:#fff;border:1px solid #e2e8f0;" +
        "border-radius:12px;padding:23px;margin:20px 0}" +
        ".warn{background:#fff7ed;border:1px solid #fdba74;" +
        "border-radius:9px;padding:15px}" +
        ".muted{color:#64748b}" +
        ".scroll{overflow-x:auto}" +
        "table{border-collapse:collapse;width:100%}" +
        "th,td{padding:11px;text-align:left;" +
        "border-bottom:1px solid #e2e8f0;white-space:nowrap}" +
        "th{background:#eff6ff}" +
        ".button{display:inline-block;padding:12px 17px;" +
        "background:#183b5a;color:white;text-decoration:none;" +
        "border-radius:8px;margin:6px 10px 6px 0}" +
        "</style></head><body><main>" +
        body + "</main></body></html>";

    [HttpGet("")]
    public IActionResult Index()
    {
        if (_db.Database.ProviderName?.Contains(
            "Sqlite", StringComparison.OrdinalIgnoreCase) != true)
        {
            return StatusCode(500, "SQLite veritabani gerekli.");
        }

        var db = (SqliteConnection)_db.Database.GetDbConnection();
        bool openedHere = db.State != ConnectionState.Open;

        if (openedHere)
            _db.Database.OpenConnection();

        try
        {
            var html = new StringBuilder();

            html.Append(
                "<h1>MASTER CHEF - Sevkiyat Hareket Takibi</h1>" +
                "<p class='muted'>Harici siparis baglantilari " +
                "uzerinden uretim, paketleme ve sevkiyat " +
                "hareketlerinin incelenmesi.</p>");

            html.Append(
                "<p class='warn'><strong>Onemli:</strong> " +
                "Bu ekran paketleme partisiyle iliskili " +
                "sevkiyat hareketlerini gosterir. " +
                "Bir hareketin gorunmesi, sevkiyatin " +
                "bu harici siparise ait oldugunu veya " +
                "musteriye teslim edildigini kanitlamaz. " +
                "Siparis bakiyesi otomatik kapatilmaz.</p>");

            if (!Exists(db, "MCExternalProductionOrderLinks"))
            {
                html.Append(
                    "<section><p>Henuz siparis-uretim emri " +
                    "baglantisi bulunmuyor.</p></section>");

                return Content(
                    Page(html.ToString()),
                    "text/html; charset=utf-8");
            }

            var linkColumns = Columns(
                db, "MCExternalProductionOrderLinks");

            if (!new[]
            {
                "ExternalOrderId",
                "ProductionOrderId",
                "ProductId"
            }.All(linkColumns.Contains))
            {
                html.Append(
                    "<section><p class='warn'>Siparis-uretim " +
                    "baglanti semasi dogrulanamadi.</p></section>");

                return Content(
                    Page(html.ToString()),
                    "text/html; charset=utf-8");
            }

            var packagingTable = FindTable(
                db,
                new[]
                {
                    "ProductionPackagingLinks",
                    "ProductionPackagingLink"
                },
                "ProductionOrderId",
                "PackagingBatchId");

            var movementColumns = Columns(db, "StockMovements");

            bool directMovement =
                movementColumns.Contains("Id") &&
                movementColumns.Contains("Type") &&
                movementColumns.Contains("PackagingBatchId");

            var palletTable = FindTable(
                db,
                new[]
                {
                    "PalletItems",
                    "PalletItem"
                },
                "Id",
                "PackagingBatchId");

            bool palletMovement =
                movementColumns.Contains("Id") &&
                movementColumns.Contains("Type") &&
                movementColumns.Contains("PalletItemId") &&
                palletTable != null;

            if (packagingTable == null ||
                (!directMovement && !palletMovement))
            {
                html.Append(
                    "<section><p class='warn'>" +
                    "Paketleme ve stok hareketleri arasinda " +
                    "dogrulanabilir baglanti bulunamadi. " +
                    "Sevkiyat sonucu hesaplanmadi.</p>" +
                    "</section>");

                return Content(
                    Page(html.ToString()),
                    "text/html; charset=utf-8");
            }

            string shipmentColumn =
                movementColumns.Contains("RelatedShipmentId")
                    ? "m.RelatedShipmentId"
                    : "NULL";

            string boxColumn =
                movementColumns.Contains("BoxCount")
                    ? "m.BoxCount"
                    : "NULL";

            string unitColumn =
                movementColumns.Contains("LooseUnitCount")
                    ? "m.LooseUnitCount"
                    : "NULL";

            string sql =
                "SELECT " +
                "l.ExternalOrderId, " +
                "l.ProductionOrderId, " +
                "l.ProductId, " +
                "p.PackagingBatchId, " +
                "m.Id, " +
                "m.Type, " +
                boxColumn + ", " +
                unitColumn + ", " +
                shipmentColumn + " " +
                "FROM MCExternalProductionOrderLinks l " +
                "JOIN " + Q(packagingTable) +
                " p ON p.ProductionOrderId=l.ProductionOrderId " +
                "LEFT JOIN StockMovements m ON m.Id IN (" +
                "SELECT m2.Id FROM StockMovements m2 " +
                (palletMovement
                    ? "LEFT JOIN " + Q(palletTable!) +
                      " pi2 ON pi2.Id=m2.PalletItemId "
                    : "") +
                "WHERE " +
                (directMovement && palletMovement
                    ? "COALESCE(m2.PackagingBatchId,pi2.PackagingBatchId)"
                    : directMovement
                        ? "m2.PackagingBatchId"
                        : "pi2.PackagingBatchId") +
                "=p.PackagingBatchId " +
                "AND m2.Type IN (1,2,3,4,5,6)" +
                ") " +
                "ORDER BY l.ProductionOrderId DESC," +
                "p.PackagingBatchId DESC,m.Id DESC " +
                "LIMIT 500";

            // Sorguda sadece gercek stok hareketleri okunur.
            // Shipment Type=2, Receipt Type=1.
            // ShipmentCancellation Type=6 ayri gosterilir.

            html.Append(
                "<section><h2>Kayitli Stok ve Sevkiyat Hareketleri</h2>" +
                "<p class='muted'>Son 500 hareket/baglanti satiri. " +
                "Ayni paketleme partisi baska siparislerle " +
                "ortak kullanilmis olabilir.</p>" +
                "<div class='scroll'><table><thead><tr>" +
                "<th>Harici Siparis</th>" +
                "<th>Uretim Emri</th>" +
                "<th>Urun Kart ID</th>" +
                "<th>Paketleme Partisi</th>" +
                "<th>Hareket ID</th>" +
                "<th>Hareket Turu</th>" +
                "<th>Koli</th>" +
                "<th>Tekil Adet</th>" +
                "<th>Sevkiyat ID</th>" +
                "<th>Yorum</th>" +
                "</tr></thead><tbody>");

            int rows = 0;
            int receipts = 0;
            int shipments = 0;
            int cancellations = 0;

            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = sql;

                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    rows++;

                    int? type = reader.IsDBNull(5)
                        ? null
                        : Convert.ToInt32(reader.GetValue(5));

                    string movementName = type switch
                    {
                        1 => "MAMUL STOK GIRISI",
                        2 => "SEVKIYAT CIKISI",
                        3 => "IADE GIRISI",
                        4 => "HASAR CIKISI",
                        5 => "GIRIS IPTALI",
                        6 => "SEVKIYAT IPTALI",
                        _ => "HAREKET BULUNAMADI"
                    };

                    if (type == 1)
                        receipts++;

                    if (type == 2)
                        shipments++;

                    if (type == 6)
                        cancellations++;

                    string interpretation = type switch
                    {
                        1 => "Parti icin giris kaydi var",
                        2 => "Parti icin sevkiyat hareketi var",
                        6 => "Sevkiyat iptal hareketi var",
                        null => "Stok hareketi bulunamadi",
                        _ => "Stok hareketi - ayrica incele"
                    };

                    html.Append("<tr>");

                    for (int i = 0; i < 9; i++)
                    {
                        html.Append("<td>");

                        if (!reader.IsDBNull(i))
                            html.Append(H(reader.GetValue(i)));
                        else
                            html.Append("—");

                        if (i == 5)
                        {
                            html.Append(" / ");
                            html.Append(H(movementName));
                        }

                        html.Append("</td>");
                    }

                    html.Append("<td>");
                    html.Append(H(interpretation));
                    html.Append("</td></tr>");
                }
            }

            if (rows == 0)
            {
                html.Append(
                    "<tr><td colspan='10'>" +
                    "Kayitli hareket veya paketleme " +
                    "baglantisi bulunamadi.</td></tr>");
            }

            html.Append("</tbody></table></div></section>");

            html.Append(
                "<section><h2>Hareket Ozeti</h2>" +
                "<p>Listelenen satir: <strong>" +
                rows + "</strong></p>" +
                "<p>Mamul stok girisi: <strong>" +
                receipts + "</strong></p>" +
                "<p>Sevkiyat cikis hareketi: <strong>" +
                shipments + "</strong></p>" +
                "<p>Sevkiyat iptal hareketi: <strong>" +
                cancellations + "</strong></p>" +
                "<p class='warn'>Bu sayilar tekil teslimat " +
                "veya teslim edilen siparis sayisi degildir. " +
                "Ayni stok hareketi, birden fazla " +
                "siparis baglantisinda gorunebilir. " +
                "Sevkiyat iptalleri de ayri incelenmelidir.</p>" +
                "</section>");

            html.Append(
                "<section><h2>Ilgili Ekranlar</h2>" +
                "<a class='button' " +
                "href='/MCExternalOrders/productionTrace'>" +
                "Uretim Paketleme Takibi</a>" +
                "<a class='button' " +
                "href='/MCExternalOrders/dispatchQueue'>" +
                "Sevkiyat Hazirlik Listesi</a>" +
                "<a class='button' " +
                "href='/MCExternalOrders/productionLinks'>" +
                "Siparis Uretim Baglantilari</a>" +
                "<a class='button' " +
                "href='/MCProductionPlanning/management'>" +
                "Yonetim Merkezi</a>" +
                "<p class='muted'>Bu rapor salt okunurdur. " +
                "Siparis, uretim, stok, sevkiyat veya " +
                "finans hareketi olusturmaz.</p>" +
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