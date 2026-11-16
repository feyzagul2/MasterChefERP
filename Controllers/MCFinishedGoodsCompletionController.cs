using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Net;
using System.Text;
using MasterChefTakip.Data;
using MasterChefTakip.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

// MC_FINISHED_GOODS_COMPLETION_V1
[Authorize(Roles = AppRoles.Admin)]
[Route("MCFinishedGoodsCompletion")]
public sealed class MCFinishedGoodsCompletionController : Controller
{
    private readonly ApplicationDbContext _db;

    public MCFinishedGoodsCompletionController(
        ApplicationDbContext db)
    {
        _db = db;
    }

    private static string H(object? value)
    {
        return WebUtility.HtmlEncode(
            Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
    }

    private static string Identifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Gecersiz SQL alani.");

        foreach (var ch in name)
        {
            if (!(char.IsLetterOrDigit(ch) || ch == '_'))
                throw new InvalidOperationException("Gecersiz SQL alani.");
        }

        return "\"" + name + "\"";
    }

    private static bool TableExists(
        System.Data.Common.DbConnection connection,
        string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM sqlite_master " +
            "WHERE type='table' AND name=@name";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@name";
        parameter.Value = table;
        command.Parameters.Add(parameter);

        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    private static HashSet<string> Columns(
        System.Data.Common.DbConnection connection,
        string table)
    {
        var result = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        if (!TableExists(connection, table))
            return result;

        using var command = connection.CreateCommand();
        command.CommandText =
            "PRAGMA table_info(" + Identifier(table) + ")";

        using var reader = command.ExecuteReader();

        while (reader.Read())
            result.Add(reader.GetString(1));

        return result;
    }

    private static long Count(
        System.Data.Common.DbConnection connection,
        string table)
    {
        if (!TableExists(connection, table))
            return 0;

        using var command = connection.CreateCommand();

        command.CommandText =
            "SELECT COUNT(*) FROM " + Identifier(table);

        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static long Scalar(
        System.Data.Common.DbConnection connection,
        string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToInt64(command.ExecuteScalar() ?? 0);
    }

    private static string Card(
        string title,
        long value,
        string description)
    {
        return "<article class='card'><small>" +
               H(title) +
               "</small><strong>" +
               value.ToString("N0", CultureInfo.GetCultureInfo("tr-TR")) +
               "</strong><span>" +
               H(description) +
               "</span></article>";
    }

    private static string Link(
        string href,
        string title,
        string description)
    {
        return "<a class='action' href='" +
               H(href) +
               "'><strong>" +
               H(title) +
               "</strong><span>" +
               H(description) +
               "</span></a>";
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        var connection = _db.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
            _db.Database.OpenConnection();

        var orders = Count(connection, "ProductionOrders");
        var lots = Count(connection, "ProductLots");
        var batches = Count(connection, "PackagingBatches");
        var links = Count(connection, "ProductionPackagingLinks");
        var movements = Count(connection, "StockMovements");

        var orderColumns = Columns(connection, "ProductionOrders");
        var lotColumns = Columns(connection, "ProductLots");
        var linkColumns = Columns(connection, "ProductionPackagingLinks");
        var movementColumns = Columns(connection, "StockMovements");

        long completedOrders = 0;
        long unmatchedOrders = 0;
        long linkedOrders = 0;
        long receiptMovements = 0;
        long unlinkedReceipts = 0;

        if (orderColumns.Contains("Status"))
        {
            completedOrders = Scalar(
                connection,
                "SELECT COUNT(*) FROM ProductionOrders " +
                "WHERE Status='Completed'");
        }

        if (linkColumns.Contains("ProductionOrderId"))
        {
            linkedOrders = Scalar(
                connection,
                "SELECT COUNT(DISTINCT ProductionOrderId) " +
                "FROM ProductionPackagingLinks");
        }

        if (movementColumns.Contains("Type"))
        {
            receiptMovements = Scalar(
                connection,
                "SELECT COUNT(*) FROM StockMovements WHERE Type=1");
        }

        bool canCheckOrders =
            orderColumns.Contains("Id") &&
            orderColumns.Contains("Status") &&
            linkColumns.Contains("ProductionOrderId");

        if (canCheckOrders)
        {
            unmatchedOrders = Scalar(
                connection,
                """
                SELECT COUNT(*)
                FROM ProductionOrders o
                WHERE o.Status='Completed'
                AND NOT EXISTS (
                    SELECT 1
                    FROM ProductionPackagingLinks l
                    WHERE l.ProductionOrderId=o.Id
                )
                """);
        }

        bool canCheckReceipts =
            linkColumns.Contains("PackagingBatchId") &&
            movementColumns.Contains("PackagingBatchId") &&
            movementColumns.Contains("Type");

        if (canCheckReceipts)
        {
            unlinkedReceipts = Scalar(
                connection,
                """
                SELECT COUNT(*)
                FROM StockMovements m
                WHERE m.Type=1
                  AND m.PackagingBatchId IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1
                      FROM ProductionPackagingLinks l
                      WHERE l.PackagingBatchId=m.PackagingBatchId
                  )
                """);
        }

        var html = new StringBuilder();

        html.Append("""
        <!doctype html>
        <html lang="tr">
        <head>
        <meta charset="utf-8">
        <meta name="viewport"
              content="width=device-width,initial-scale=1">
        <title>MASTER CHEF - Mamul Stok Tamamlama</title>
        <style>
        *{box-sizing:border-box}
        body{
            margin:0;
            padding:28px;
            font-family:Arial,sans-serif;
            background:#f1f5f9;
            color:#172033
        }
        main{max-width:1250px;margin:auto}
        h1{margin-bottom:8px}
        .intro{color:#64748b;margin-bottom:25px}
        .grid{
            display:grid;
            grid-template-columns:repeat(auto-fit,minmax(210px,1fr));
            gap:15px
        }
        .card{
            background:white;
            border:1px solid #e2e8f0;
            padding:20px;
            border-radius:12px;
            display:flex;
            flex-direction:column;
            gap:12px
        }
        .card small{font-size:14px;color:#64748b}
        .card strong{font-size:31px;color:#172033}
        .card span{font-size:13px;color:#64748b}
        section{
            background:white;
            padding:23px;
            margin-top:22px;
            border-radius:12px;
            border:1px solid #e2e8f0
        }
        .actions{
            display:grid;
            grid-template-columns:repeat(auto-fit,minmax(260px,1fr));
            gap:12px
        }
        .action{
            text-decoration:none;
            padding:18px;
            border:1px solid #cbd5e1;
            border-radius:9px;
            display:flex;
            flex-direction:column;
            gap:8px;
            color:#183b5a
        }
        .action:hover{background:#eff6ff}
        .action span{font-size:13px;color:#64748b}
        .warn{
            background:#fff7ed;
            padding:14px;
            border:1px solid #fed7aa;
            border-radius:8px
        }
        .good{
            background:#ecfdf5;
            padding:14px;
            border:1px solid #a7f3d0;
            border-radius:8px
        }
        table{width:100%;border-collapse:collapse}
        td,th{
            padding:11px;
            text-align:left;
            border-bottom:1px solid #e2e8f0
        }
        th{background:#f8fafc}
        </style>
        </head>
        <body><main>
        <h1>MASTER CHEF - Mamul Stok Tamamlama</h1>
        <p class="intro">
        Mevcut üretim emri, ürün lotu, paketleme partisi ve
        gerçek stok girişleri arasındaki bağlantılar.
        </p>
        <div class="grid">
        """);

        html.Append(Card(
            "Üretim Emirleri",orders,
            "Mevcut üretim emirleri"));

        html.Append(Card(
            "Tamamlanan Üretimler",completedOrders,
            "Completed durumundaki emirler"));

        html.Append(Card(
            "Ürün Lotları",lots,
            "Mevcut mamul lotları"));

        html.Append(Card(
            "Paketleme Partileri",batches,
            "Mevcut paketleme kayıtları"));

        html.Append(Card(
            "Üretim-Paketleme Bağlantıları",links,
            "Mevcut bağlantı kayıtları"));

        html.Append(Card(
            "Bağlanan Üretim Emirleri",linkedOrders,
            "En az bir paketleme bağlantısı bulunan emirler"));

        html.Append(Card(
            "Mamul Stok Giriş Hareketleri",receiptMovements,
            "Mevcut stok hareketlerinde giriş türü"));

        html.Append(Card(
            "Toplam Stok Hareketleri",movements,
            "Giriş, çıkış ve diğer hareketler"));

        html.Append("</div>");

        html.Append("""
        <section>
        <h2>Bağlantı Kontrolü</h2>
        <table>
        <tr><th>Kontrol</th><th>Sonuç</th></tr>
        """);

        if (canCheckOrders)
        {
            html.Append("<tr><td>Tamamlanan fakat paketleme bağlantısı " +
                        "bulunmayan üretim emri</td><td><strong>" +
                        unmatchedOrders + "</strong></td></tr>");
        }
        else
        {
            html.Append("<tr><td>Üretim-paketleme eşleştirmesi</td>" +
                        "<td>Mevcut şema ile doğrulanamadı</td></tr>");
        }

        if (canCheckReceipts)
        {
            html.Append("<tr><td>Paketleme partisi belirtilmiş, " +
                        "üretimle bağlantısı olmayan stok giriş hareketi" +
                        "</td><td><strong>" +
                        unlinkedReceipts + "</strong></td></tr>");
        }
        else
        {
            html.Append("<tr><td>Stok giriş-paketleme eşleştirmesi</td>" +
                        "<td>Mevcut şema ile doğrulanamadı</td></tr>");
        }

        html.Append("""
        </table>
        <p class="warn">
        Bu sayılar eksik bağlantıları gösterir;
        üretim miktarıyla gerçek stok miktarının
        birebir eşit olduğunu tek başına kanıtlamaz.
        Mevcut stok hareketlerine yeni kayıt eklenmez.
        </p>
        </section>
        <section>
        <h2>Eksik İşlemleri Tamamla</h2>
        <div class="actions">
        """);

        html.Append(Link(
            "/Production",
            "1. Üretim Emirleri",
            "Üretim emirlerini ve tamamlanan üretimleri kontrol et."));

        html.Append(Link(
            "/Packaging",
            "2. Paketleme Partileri",
            "Ürünün paketleme ve mevcut stok giriş kayıtlarını kontrol et."));

        html.Append(Link(
            "/ProductionPackaging",
            "3. Üretim-Paketleme-Lot Bağlantısı",
            "Mevcut sistem üzerinden üretimi, lotu ve paketlemeyi eşleştir."));

        html.Append(Link(
            "/MCProductionPlanning/stockTrace",
            "4. Gerçek Mamul Stok İzleme",
            "Paketleme partisine bağlı mamul girişlerini incele."));

        html.Append(Link(
            "/MCProductionPlanning/management",
            "5. Yönetim Merkezi",
            "Operasyonların genel durumunu görüntüle."));

        html.Append("""
        </div>
        </section>
        <section>
        <h2>Stok Güvenliği</h2>
        <p class="good">
        Bu modül gerçek stok hareketlerini yalnızca okur.
        Yeni giriş veya çıkış üretmez.
        Mevcut üretim-paketleme bağlantılarının
        üzerine ikinci bağlantı oluşturmaz.
        </p>
        </section>
        </main></body></html>
        """);

        return Content(
            html.ToString(),
            "text/html; charset=utf-8");
    }
}