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

// MC_EXTERNAL_ORDER_RECONCILIATION_V1
[Authorize(Roles = AppRoles.Admin)]
[Route("MCExternalOrders/reconcile")]
public sealed class MCExternalOrderReconciliationController : Controller
{
    private readonly ApplicationDbContext _db;

    public MCExternalOrderReconciliationController(ApplicationDbContext db)
    {
        _db = db;
    }

    private static string H(object? value)
    {
        return WebUtility.HtmlEncode(
            Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
    }

    private DbConnection Open()
    {
        var connection = _db.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
            _db.Database.OpenConnection();

        return connection;
    }

    private static bool Exists(DbConnection db, string table)
    {
        using var cmd = db.CreateCommand();

        cmd.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type='table' AND name=@name
            """;

        var p = cmd.CreateParameter();
        p.ParameterName = "@name";
        p.Value = table;
        cmd.Parameters.Add(p);

        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    private static long Count(DbConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;

        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
    }

    private static string Table(
        DbConnection db,
        string sql,
        string[] headers)
    {
        var html = new StringBuilder();

        html.Append("<div class='scroll'><table><thead><tr>");

        foreach (var header in headers)
        {
            html.Append("<th>");
            html.Append(H(header));
            html.Append("</th>");
        }

        html.Append("</tr></thead><tbody>");

        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;

        using var reader = cmd.ExecuteReader();

        var rows = 0;

        while (reader.Read())
        {
            rows++;
            html.Append("<tr>");

            for (var i = 0; i < reader.FieldCount; i++)
            {
                html.Append("<td>");
                html.Append(reader.IsDBNull(i)
                    ? "—"
                    : H(reader.GetValue(i)));
                html.Append("</td>");
            }

            html.Append("</tr>");
        }

        if (rows == 0)
        {
            html.Append("<tr><td colspan='");
            html.Append(headers.Length);
            html.Append("'>Kayıt bulunamadı.</td></tr>");
        }

        html.Append("</tbody></table></div>");

        return html.ToString();
    }

    private static string Card(
        string title, long value, string description)
    {
        return "<div class='card'><small>" +
            H(title) +
            "</small><strong>" +
            value.ToString("N0", CultureInfo.GetCultureInfo("tr-TR")) +
            "</strong><span>" +
            H(description) +
            "</span></div>";
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        var db = Open();

        if (!Exists(db, "MCExternalSyncImports") ||
            !Exists(db, "MCExternalSyncLines"))
        {
            return Content("""
                <!doctype html><html lang='tr'>
                <meta charset='utf-8'>
                <body style='font-family:Arial;padding:30px'>
                <h2>MASTER CHEF - Sipariş Entegrasyonu</h2>
                <p>Henüz gerçek sipariş aktarımı yapılmamış.
                Entegrasyon tabloları bulunamadı.</p>
                <a href='/MCExternalOrders/import'>
                Sipariş aktarım ekranına git</a>
                </body></html>
                """, "text/html; charset=utf-8");
        }

        var importCount = Count(db,
            "SELECT COUNT(*) FROM MCExternalSyncImports");

        var rowCount = Count(db,
            "SELECT COUNT(*) FROM MCExternalSyncLines");

        var orderCount = Count(db, """
            SELECT COUNT(DISTINCT ExternalOrderId)
            FROM MCExternalSyncLines
            """);

        var matchedCount = Count(db, """
            SELECT COUNT(*)
            FROM MCExternalSyncLines
            WHERE MatchStatus='ESLESTI'
              AND ProductId IS NOT NULL
            """);

        var unmatchedCount = Count(db, """
            SELECT COUNT(*)
            FROM MCExternalSyncLines
            WHERE MatchStatus<>'ESLESTI'
               OR ProductId IS NULL
            """);

        var duplicateCount = Count(db, """
            SELECT COUNT(*)
            FROM (
                SELECT ExternalOrderId, ProductCode
                FROM MCExternalSyncLines
                GROUP BY
                    ExternalOrderId COLLATE NOCASE,
                    ProductCode COLLATE NOCASE
                HAVING COUNT(DISTINCT ImportId)>1
            )
            """);

        var conflictCount = Count(db, """
            SELECT COUNT(*)
            FROM (
                SELECT ExternalOrderId, ProductCode
                FROM MCExternalSyncLines
                GROUP BY
                    ExternalOrderId COLLATE NOCASE,
                    ProductCode COLLATE NOCASE
                HAVING COUNT(DISTINCT Quantity)>1
            )
            """);

        var html = new StringBuilder();

        html.Append("""
            <!doctype html>
            <html lang='tr'>
            <head>
            <meta charset='utf-8'>
            <meta name='viewport'
                  content='width=device-width,initial-scale=1'>
            <title>MASTER CHEF - Sipariş Mutabakatı</title>
            <style>
            *{box-sizing:border-box}
            body{
                font-family:Arial,sans-serif;
                background:#f1f5f9;
                color:#172033;
                margin:0;
                padding:28px
            }
            main{max-width:1350px;margin:auto}
            h1{margin-bottom:8px}
            h2{margin-top:0}
            .intro{color:#64748b}
            .grid{
                display:grid;
                grid-template-columns:repeat(auto-fit,minmax(185px,1fr));
                gap:14px;
                margin:24px 0
            }
            .card{
                background:white;
                border:1px solid #e2e8f0;
                border-radius:12px;
                padding:20px;
                display:flex;
                flex-direction:column;
                gap:12px
            }
            .card small{color:#64748b}
            .card strong{font-size:30px}
            .card span{font-size:12px;color:#64748b}
            section{
                background:white;
                border:1px solid #e2e8f0;
                padding:23px;
                border-radius:12px;
                margin:20px 0
            }
            .scroll{overflow-x:auto}
            table{width:100%;border-collapse:collapse}
            th,td{
                padding:11px;
                border-bottom:1px solid #e2e8f0;
                text-align:left;
                white-space:nowrap
            }
            th{background:#eff6ff}
            .warn{
                background:#fff7ed;
                border:1px solid #fed7aa;
                border-radius:9px;
                padding:15px
            }
            .good{
                background:#ecfdf5;
                border:1px solid #a7f3d0;
                border-radius:9px;
                padding:15px
            }
            .actions{
                display:flex;
                flex-wrap:wrap;
                gap:12px
            }
            a.button{
                display:inline-block;
                padding:12px 18px;
                background:#183b5a;
                border-radius:7px;
                color:white;
                text-decoration:none
            }
            </style>
            </head>
            <body><main>
            <h1>MASTER CHEF - Sipariş Mutabakat Merkezi</h1>
            <p class='intro'>
            Mevcut sipariş sisteminden gelen kayıtların
            ürün eşleşmesi ve tekrar kontrolü.
            </p>
            <div class='grid'>
            """);

        html.Append(Card(
            "Aktarım Dosyası", importCount,
            "Entegrasyona alınan CSV dosyaları"));

        html.Append(Card(
            "Sipariş Satırı", rowCount,
            "Toplam aktarılmış satır"));

        html.Append(Card(
            "Farklı Sipariş", orderCount,
            "Harici sipariş numarası"));

        html.Append(Card(
            "Ürün Eşleşmesi", matchedCount,
            "Stok kartıyla eşleşen satır"));

        html.Append(Card(
            "Eşleşmeyen Satır", unmatchedCount,
            "İncelenmesi gereken ürün kodları"));

        html.Append(Card(
            "Tekrar Adayı", duplicateCount,
            "Farklı dosyalarda bulunan sipariş ve ürün çiftleri"));

        html.Append(Card(
            "Miktar Farkı", conflictCount,
            "Aynı sipariş ve ürün için farklı miktarlar"));

        html.Append("</div>");

        if (unmatchedCount > 0 || duplicateCount > 0 ||
            conflictCount > 0)
        {
            html.Append("""
                <p class='warn'>
                İncelenmesi gereken kayıtlar var.
                Bu rapor mükerrer veya hatalı kayıtları
                otomatik silmez. Tekrarlanan ürün satırları
                gerçek siparişin parçası da olabilir.
                </p>
                """);
        }
        else
        {
            html.Append("""
                <p class='good'>
                Kayıtlı verilerde bu raporun kontrol ettiği
                eşleşme veya tekrar sorunları bulunmadı.
                </p>
                """);
        }

        html.Append("""
            <section>
            <h2>1. Son Sipariş Aktarımları</h2>
            """);

        html.Append(Table(db, """
            SELECT
                Id,
                FileName,
                RowCount,
                ImportedAtUtc
            FROM MCExternalSyncImports
            ORDER BY Id DESC
            LIMIT 50
            """,
            new[]
            {
                "Aktarım No",
                "Dosya",
                "Satır Sayısı",
                "Aktarım Tarihi (UTC)"
            }));

        html.Append("</section>");

        html.Append("""
            <section>
            <h2>2. Eşleşmeyen Ürün Kodları</h2>
            <p>Mevcut ürün kartlarıyla eşleştirilemeyen
            sipariş satırları.</p>
            """);

        html.Append(Table(db, """
            SELECT
                l.ImportId,
                l.ExternalOrderId,
                l.ProductCode,
                l.Quantity,
                l.MatchStatus
            FROM MCExternalSyncLines l
            WHERE l.MatchStatus<>'ESLESTI'
               OR l.ProductId IS NULL
            ORDER BY l.Id DESC
            LIMIT 200
            """,
            new[]
            {
                "Aktarım",
                "Sipariş No",
                "Ürün Kodu",
                "Miktar",
                "Durum"
            }));

        html.Append("</section>");

        html.Append("""
            <section>
            <h2>3. Farklı Dosyalarda Tekrar Eden Sipariş Satırları</h2>
            <p>Aynı sipariş numarası ve ürün kodunun
            birden fazla aktarım dosyasında bulunması.</p>
            """);

        html.Append(Table(db, """
            SELECT
                ExternalOrderId,
                ProductCode,
                COUNT(*) AS TotalRows,
                COUNT(DISTINCT ImportId) AS ImportFiles,
                MIN(Quantity) AS MinQuantity,
                MAX(Quantity) AS MaxQuantity
            FROM MCExternalSyncLines
            GROUP BY
                ExternalOrderId COLLATE NOCASE,
                ProductCode COLLATE NOCASE
            HAVING COUNT(DISTINCT ImportId)>1
            ORDER BY ImportFiles DESC, TotalRows DESC
            LIMIT 200
            """,
            new[]
            {
                "Sipariş No",
                "Ürün Kodu",
                "Toplam Satır",
                "Dosya Sayısı",
                "En Küçük Miktar",
                "En Büyük Miktar"
            }));

        html.Append("</section>");

        html.Append("""
            <section>
            <h2>4. Miktarı Değişen Sipariş Satırları</h2>
            <p>Aynı sipariş ve ürün kodu için farklı
            miktarlar tespit edilen kayıtlar.
            Bunlar sipariş güncellemesi veya
            ayrı sipariş kalemleri olabilir.</p>
            """);

        html.Append(Table(db, """
            SELECT
                ExternalOrderId,
                ProductCode,
                COUNT(*) AS TotalRows,
                MIN(Quantity) AS MinQuantity,
                MAX(Quantity) AS MaxQuantity
            FROM MCExternalSyncLines
            GROUP BY
                ExternalOrderId COLLATE NOCASE,
                ProductCode COLLATE NOCASE
            HAVING COUNT(DISTINCT Quantity)>1
            ORDER BY TotalRows DESC
            LIMIT 200
            """,
            new[]
            {
                "Sipariş No",
                "Ürün Kodu",
                "Satır Sayısı",
                "En Küçük Miktar",
                "En Büyük Miktar"
            }));

        html.Append("</section>");

        html.Append("""
            <section>
            <h2>5. Entegrasyon İşlemleri</h2>
            <div class='actions'>
            <a class='button'
               href='/MCExternalOrders/import'>
               Yeni Sipariş Dosyası Aktar</a>
            <a class='button'
               href='/MCExternalOrders'>
               CSV Önizleme</a>
            <a class='button'
               href='/MCProductionPlanning/management'>
               Yönetim Merkezi</a>
            </div>
            </section>

            <section>
            <h2>Veri Güvenliği</h2>
            <p>
            Bu ekran yalnızca entegrasyon kayıtlarını okur.
            Mevcut sipariş sistemine, stok hareketlerine,
            sevkiyatlara veya muhasebe kayıtlarına
            müdahale etmez.
            </p>
            <p>
            Ürün eşleştirmeleri, aktarım anındaki
            durumları yansıtır. Sonradan değişen
            ürün kartları otomatik güncellenmez.
            </p>
            </section>
            </main></body></html>
            """);

        return Content(
            html.ToString(),
            "text/html; charset=utf-8");
    }
}