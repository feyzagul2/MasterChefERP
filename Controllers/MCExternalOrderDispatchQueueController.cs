using System;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Text;
using MasterChefTakip.Data;
using MasterChefTakip.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

// MC_EXTERNAL_DISPATCH_QUEUE_V1
[Authorize(Roles = AppRoles.Admin)]
[Route("MCExternalOrders/dispatchQueue")]
public sealed class MCExternalOrderDispatchQueueController : Controller
{
    private readonly ApplicationDbContext _db;

    public MCExternalOrderDispatchQueueController(ApplicationDbContext db)
    {
        _db = db;
    }

    private static string H(object? value)
    {
        return WebUtility.HtmlEncode(
            Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
    }

    private static bool Exists(DbConnection connection, string name)
    {
        using var cmd = connection.CreateCommand();

        cmd.CommandText =
            "SELECT COUNT(*) FROM sqlite_master " +
            "WHERE type='table' AND name=@name";

        var p = cmd.CreateParameter();
        p.ParameterName = "@name";
        p.Value = name;
        cmd.Parameters.Add(p);

        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    private static long Count(DbConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;

        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
    }

    private static string Layout(string content)
    {
        return "<!doctype html><html lang='tr'><head>" +
            "<meta charset='utf-8'>" +
            "<meta name='viewport' content='width=device-width,initial-scale=1'>" +
            "<title>MASTER CHEF - Sevkiyat Hazirlik Listesi</title>" +
            "<style>" +
            "*{box-sizing:border-box}" +
            "body{font-family:Arial,sans-serif;background:#f1f5f9;" +
            "color:#172033;margin:0;padding:28px}" +
            "main{max-width:1400px;margin:auto}" +
            "h1{margin-bottom:8px}" +
            ".muted{color:#64748b}" +
            ".cards{display:grid;grid-template-columns:" +
            "repeat(auto-fit,minmax(190px,1fr));gap:14px;margin:22px 0}" +
            ".card,section{background:white;border:1px solid #e2e8f0;" +
            "border-radius:12px;padding:22px}" +
            "section{margin:20px 0}" +
            ".card strong{display:block;font-size:30px;margin-top:12px}" +
            ".scroll{overflow-x:auto}" +
            "table{width:100%;border-collapse:collapse}" +
            "th,td{text-align:left;padding:12px;" +
            "border-bottom:1px solid #e2e8f0;white-space:nowrap}" +
            "th{background:#eff6ff}" +
            ".warn{background:#fff7ed;border:1px solid #fdba74;" +
            "padding:14px;border-radius:8px}" +
            ".button{display:inline-block;background:#183b5a;color:white;" +
            "padding:12px 17px;border-radius:8px;text-decoration:none;" +
            "margin-right:10px;margin-bottom:10px}" +
            "</style></head><body><main>" +
            content + "</main></body></html>";
    }

    private static string Table(
        DbConnection connection,
        string sql,
        string[] headings)
    {
        var html = new StringBuilder();

        html.Append("<div class='scroll'><table><thead><tr>");

        foreach (var heading in headings)
        {
            html.Append("<th>");
            html.Append(H(heading));
            html.Append("</th>");
        }

        html.Append("</tr></thead><tbody>");

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;

        using var reader = cmd.ExecuteReader();

        int rowCount = 0;

        while (reader.Read())
        {
            rowCount++;
            html.Append("<tr>");

            for (int i = 0; i < reader.FieldCount; i++)
            {
                html.Append("<td>");

                if (!reader.IsDBNull(i))
                    html.Append(H(reader.GetValue(i)));
                else
                    html.Append("—");

                html.Append("</td>");
            }

            html.Append("</tr>");
        }

        if (rowCount == 0)
        {
            html.Append("<tr><td colspan='");
            html.Append(headings.Length);
            html.Append("'>Kayit bulunamadi.</td></tr>");
        }

        html.Append("</tbody></table></div>");

        return html.ToString();
    }

    private static string Card(string label, long number)
    {
        return "<div class='card'>" + H(label) +
            "<strong>" +
            number.ToString(
                "N0", CultureInfo.GetCultureInfo("tr-TR")) +
            "</strong></div>";
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        if (_db.Database.ProviderName?.Contains(
            "Sqlite", StringComparison.OrdinalIgnoreCase) != true)
        {
            return StatusCode(500, "SQLite veritabani gerekli.");
        }

        var connection = _db.Database.GetDbConnection();

        bool openedHere = connection.State != ConnectionState.Open;

        if (openedHere)
            _db.Database.OpenConnection();

        try
        {
            var html = new StringBuilder();

            html.Append(
                "<h1>MASTER CHEF - Sevkiyat Hazirlik Listesi</h1>" +
                "<p class='muted'>Mevcut siparis sisteminden " +
                "aktarilan siparislerin depo gorunumu.</p>");

            if (!Exists(connection, "MCExternalSyncLines"))
            {
                html.Append(
                    "<section><p>Henuz aktarilmis siparis yok.</p>" +
                    "<a class='button' href='/MCExternalOrders/import'>" +
                    "Siparis Aktarimi</a></section>");

                return Content(
                    Layout(html.ToString()),
                    "text/html; charset=utf-8");
            }

            long orders = Count(connection, """
                SELECT COUNT(DISTINCT ExternalOrderId)
                FROM MCExternalSyncLines
                """);

            long lines = Count(connection, """
                SELECT COUNT(*)
                FROM MCExternalSyncLines
                """);

            long matched = Count(connection, """
                SELECT COUNT(*)
                FROM MCExternalSyncLines
                WHERE MatchStatus='ESLESTI'
                  AND ProductId IS NOT NULL
                """);

            long unmatched = Count(connection, """
                SELECT COUNT(*)
                FROM MCExternalSyncLines
                WHERE MatchStatus<>'ESLESTI'
                   OR ProductId IS NULL
                """);

            long readyForReview = Count(connection, """
                SELECT COUNT(*)
                FROM (
                    SELECT ExternalOrderId
                    FROM MCExternalSyncLines
                    GROUP BY ExternalOrderId COLLATE NOCASE
                    HAVING SUM(
                        CASE
                            WHEN MatchStatus='ESLESTI'
                             AND ProductId IS NOT NULL
                            THEN 0 ELSE 1
                        END
                    )=0
                )
                """);

            html.Append("<div class='cards'>");
            html.Append(Card("Farkli Siparis", orders));
            html.Append(Card("Siparis Satiri", lines));
            html.Append(Card("Urun Karti Eslesen", matched));
            html.Append(Card("Eslesmeyen Satir", unmatched));
            html.Append(Card("Urun Eslestirmesi Tamam Siparis",
                readyForReview));
            html.Append("</div>");

            html.Append(
                "<p class='warn'><strong>Onemli:</strong> " +
                "Urun eslestirmesi tamamlanan bir siparisin " +
                "stokta mevcut oldugu veya sevkiyata hazir " +
                "oldugu anlamina gelmez. Bu ekran stok " +
                "tahsisi, koli hesaplamasi veya sevkiyat " +
                "hareketi olusturmaz.</p>");

            html.Append(
                "<section><h2>1. Harici Siparisler</h2>" +
                "<p class='muted'>Son 200 farkli siparis. " +
                "Durum, urun karti eslestirmesine gore hesaplanir.</p>");

            html.Append(Table(connection, """
                SELECT
                    ExternalOrderId,
                    COUNT(*) AS LineCount,
                    SUM(
                        CASE
                            WHEN MatchStatus='ESLESTI'
                             AND ProductId IS NOT NULL
                            THEN 1 ELSE 0
                        END
                    ) AS MatchedLines,
                    SUM(
                        CASE
                            WHEN MatchStatus<>'ESLESTI'
                              OR ProductId IS NULL
                            THEN 1 ELSE 0
                        END
                    ) AS UnmatchedLines,
                    MAX(CustomerCode) AS CustomerCode,
                    MAX(OrderDate) AS OrderDate,
                    CASE
                        WHEN SUM(
                            CASE
                                WHEN MatchStatus<>'ESLESTI'
                                  OR ProductId IS NULL
                                THEN 1 ELSE 0
                            END
                        )=0
                        THEN 'URUN ESLESMESI TAMAM'
                        ELSE 'URUN ESLESMESI EKSIK'
                    END AS ReviewStatus
                FROM MCExternalSyncLines
                GROUP BY ExternalOrderId COLLATE NOCASE
                ORDER BY MAX(Id) DESC
                LIMIT 200
                """,
                new[]
                {
                    "Siparis No",
                    "Kalem",
                    "Eslesen",
                    "Eslesmeyen",
                    "Musteri Kodu",
                    "Siparis Tarihi",
                    "Kontrol Durumu"
                }));

            html.Append("</section>");

            html.Append(
                "<section><h2>2. Siparis Urun Detaylari</h2>" +
                "<p class='muted'>Aktarilan miktarlar " +
                "kaynak CSV'deki birimle gosterilir. " +
                "Koli veya kilogram donusumu yapilmaz.</p>");

            html.Append(Table(connection, """
                SELECT
                    ExternalOrderId,
                    CustomerCode,
                    ProductCode,
                    Quantity,
                    ProductId,
                    MatchStatus,
                    ImportId
                FROM MCExternalSyncLines
                ORDER BY Id DESC
                LIMIT 300
                """,
                new[]
                {
                    "Siparis No",
                    "Musteri",
                    "Urun Kodu",
                    "Kaynak Miktar",
                    "Urun Kart ID",
                    "Eslesme Durumu",
                    "Aktarim No"
                }));

            html.Append("</section>");

            html.Append(
                "<section><h2>3. Islem Baglantilari</h2>" +
                "<a class='button' href='/MCExternalOrders/reconcile'>" +
                "Siparis Mutabakati</a>" +
                "<a class='button' href='/MCExternalOrders/refreshMatches'>" +
                "Urun Eslestirme</a>" +
                "<a class='button' href='/MCProductionPlanning/management'>" +
                "Yonetim Merkezi</a>" +
                "<p class='muted'>Bu liste mevcut entegrasyon " +
                "kayitlarini okur. Siparis olusturmaz, " +
                "stok dusmez, sevkiyat veya fatura kaydetmez.</p>" +
                "</section>");

            return Content(
                Layout(html.ToString()),
                "text/html; charset=utf-8");
        }
        finally
        {
            if (openedHere)
                _db.Database.CloseConnection();
        }
    }
}