using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using MasterChefTakip.Data;
using MasterChefTakip.Models;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

// MC_EXTERNAL_SHIPMENT_ORDER_LINK_V1
[Authorize(Roles = AppRoles.Admin)]
[Route("MCExternalOrders/shipmentLinks")]
public sealed class MCExternalOrderShipmentLinksController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IAntiforgery _antiforgery;

    public MCExternalOrderShipmentLinksController(
        ApplicationDbContext db,
        IAntiforgery antiforgery)
    {
        _db = db;
        _antiforgery = antiforgery;
    }

    private static string H(object? value) =>
        WebUtility.HtmlEncode(
            Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");

    private static string Q(string value) =>
        "\"" + value.Replace("\"", "\"\"") + "\"";

    private SqliteConnection Open()
    {
        if (_db.Database.ProviderName?.Contains(
            "Sqlite", StringComparison.OrdinalIgnoreCase) != true)
            throw new InvalidOperationException("SQLite gerekli.");

        var db = (SqliteConnection)_db.Database.GetDbConnection();

        if (db.State != ConnectionState.Open)
            _db.Database.OpenConnection();

        return db;
    }

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
        cmd.CommandText = "PRAGMA table_info(" + Q(table) + ")";

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
            result.Add(reader.GetString(1));

        return result;
    }

    private static bool Has(
        SqliteConnection db,
        string table,
        params string[] names)
    {
        var columns = Columns(db, table);
        return names.All(columns.Contains);
    }

    private static string? Table(
        SqliteConnection db,
        string[] candidates,
        params string[] columns)
    {
        foreach (var candidate in candidates)
        {
            if (Has(db, candidate, columns))
                return candidate;
        }

        return null;
    }

    private sealed record Mapping(
        string Shipments,
        string Batches,
        string? Pallets,
        string BatchExpression,
        string PalletJoin
    );

    private static Mapping? Detect(SqliteConnection db)
    {
        string? shipments = Table(db,
            new[] { "Shipments", "Shipment" },
            "Id");

        string? batches = Table(db,
            new[] { "PackagingBatches", "PackagingBatch" },
            "Id", "ProductId");

        if (shipments == null || batches == null ||
            !Has(db, "StockMovements",
                "Type", "RelatedShipmentId"))
        {
            return null;
        }

        var columns = Columns(db, "StockMovements");

        bool direct = columns.Contains("PackagingBatchId");

        string? pallets = null;

        if (columns.Contains("PalletItemId"))
        {
            pallets = Table(db,
                new[] { "PalletItems", "PalletItem" },
                "Id", "PackagingBatchId");
        }

        if (!direct && pallets == null)
            return null;

        string join = pallets == null
            ? ""
            : " LEFT JOIN " + Q(pallets) +
              " pi ON pi.Id=m.PalletItemId ";

        string reference =
            direct && pallets != null
                ? "COALESCE(m.PackagingBatchId,pi.PackagingBatchId)"
                : direct
                    ? "m.PackagingBatchId"
                    : "pi.PackagingBatchId";

        return new Mapping(
            shipments, batches, pallets, reference, join);
    }

    private static bool ShipmentHasProduct(
        SqliteConnection db,
        SqliteTransaction tx,
        Mapping map,
        long shipmentId,
        long productId)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;

        cmd.CommandText =
            "SELECT COUNT(*) " +
            "FROM StockMovements m " +
            map.PalletJoin +
            "JOIN " + Q(map.Batches) +
            " b ON b.Id=" + map.BatchExpression + " " +
            "WHERE m.Type=2 " +
            "AND m.RelatedShipmentId=@shipment " +
            "AND b.ProductId=@product";

        cmd.Parameters.AddWithValue("@shipment", shipmentId);
        cmd.Parameters.AddWithValue("@product", productId);

        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    private static string Page(string body) =>
        "<!doctype html><html lang='tr'><head>" +
        "<meta charset='utf-8'>" +
        "<meta name='viewport' content='width=device-width,initial-scale=1'>" +
        "<title>MASTER CHEF - Siparis Sevkiyat Baglantisi</title>" +
        "<style>" +
        "*{box-sizing:border-box}" +
        "body{font-family:Arial;background:#f1f5f9;" +
        "color:#172033;padding:26px;margin:0}" +
        "main{max-width:1250px;margin:auto}" +
        "section{background:white;padding:24px;" +
        "border:1px solid #e2e8f0;border-radius:12px;" +
        "margin:20px 0}" +
        ".warn{background:#fff7ed;border:1px solid #fdba74;" +
        "padding:15px;border-radius:8px}" +
        ".scroll{overflow-x:auto}" +
        "table{border-collapse:collapse;width:100%}" +
        "th,td{padding:11px;text-align:left;" +
        "border-bottom:1px solid #e2e8f0;white-space:nowrap}" +
        "th{background:#eff6ff}" +
        "input{padding:11px;margin:6px;border:1px solid #cbd5e1;" +
        "border-radius:7px}" +
        "button,.button{background:#183b5a;color:white;" +
        "border:0;border-radius:7px;padding:12px 16px;" +
        "text-decoration:none;cursor:pointer;display:inline-block}" +
        "</style></head><body><main>" +
        body + "</main></body></html>";

    [HttpGet("")]
    public IActionResult Index()
    {
        var db = Open();
        var map = Detect(db);

        var html = new StringBuilder();

        html.Append(
            "<h1>MASTER CHEF - Siparis / Sevkiyat Baglantisi</h1>" +
            "<p>Gercek sevkiyat kaydini mevcut harici " +
            "siparis ve urun kartiyla iliskilendirme.</p>");

        html.Append(
            "<p class='warn'><strong>Onemli:</strong> " +
            "Bu islem yalnizca sevkiyat ile siparis " +
            "arasinda kayitli bir iliski kurar. " +
            "Sevk edilen miktari siparise tahsis etmez, " +
            "teslimat veya fatura onayi vermez. " +
            "Ayni sevkiyat birden fazla siparis " +
            "iceriyorsa urun bazli baglanti " +
            "tek basina yeterli olmayabilir.</p>");

        if (map == null ||
            !Has(db, "MCExternalSyncLines",
                "ExternalOrderId", "ProductId",
                "MatchStatus", "ImportId"))
        {
            html.Append(
                "<section><p class='warn'>" +
                "Sevkiyat, paketleme veya siparis semasi " +
                "dogrulanamadi. Baglanti kaydi kapali.</p>" +
                "</section>");

            return Content(
                Page(html.ToString()),
                "text/html; charset=utf-8");
        }

        string token = H(
            _antiforgery.GetAndStoreTokens(HttpContext).RequestToken);

        html.Append(
            "<section><h2>Sevkiyat ile Siparisi Iliskilendir</h2>" +
            "<form method='post' " +
            "action='/MCExternalOrders/shipmentLinks'>" +
            "<input type='hidden' " +
            "name='__RequestVerificationToken' value='" +
            token + "'>" +
            "<p><label>Gercek Sevkiyat ID</label>" +
            "<input name='shipmentId' type='number' " +
            "min='1' required></p>" +
            "<p><label>Harici Siparis Numarasi</label>" +
            "<input name='externalOrderId' type='text' " +
            "maxlength='150' required></p>" +
            "<p><label>Urun Kart ID</label>" +
            "<input name='productId' type='number' " +
            "min='1' required></p>" +
            "<button type='submit'>Baglantiyi Dogrula ve Kaydet</button>" +
            "</form></section>");

        if (Exists(db, "MCExternalOrderShipmentLinks"))
        {
            html.Append(
                "<section><h2>Kayitli Siparis - Sevkiyat Baglantilari</h2>" +
                "<div class='scroll'><table><tr>" +
                "<th>Sevkiyat ID</th><th>Siparis No</th>" +
                "<th>Urun Kart ID</th><th>Baglanti Tarihi UTC</th>" +
                "</tr>");

            using var cmd = db.CreateCommand();

            cmd.CommandText = """
                SELECT ShipmentId,ExternalOrderId,
                       ProductId,LinkedAtUtc
                FROM MCExternalOrderShipmentLinks
                ORDER BY LinkedAtUtc DESC
                LIMIT 200
                """;

            using var reader = cmd.ExecuteReader();

            int count = 0;

            while (reader.Read())
            {
                count++;
                html.Append("<tr>");

                for (int i = 0; i < 4; i++)
                {
                    html.Append("<td>");
                    html.Append(reader.IsDBNull(i)
                        ? "—"
                        : H(reader.GetValue(i)));
                    html.Append("</td>");
                }

                html.Append("</tr>");
            }

            if (count == 0)
                html.Append(
                    "<tr><td colspan='4'>Kayit yok.</td></tr>");

            html.Append("</table></div></section>");
        }

        html.Append(
            "<section>" +
            "<a class='button' " +
            "href='/MCExternalOrders/shipmentTrace'>" +
            "Sevkiyat Hareket Takibi</a> " +
            "<a class='button' " +
            "href='/MCProductionPlanning/management'>" +
            "Yonetim Merkezi</a></section>");

        return Content(
            Page(html.ToString()),
            "text/html; charset=utf-8");
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public IActionResult Save(
        long shipmentId,
        string externalOrderId,
        long productId)
    {
        externalOrderId = (externalOrderId ?? "").Trim();

        if (shipmentId <= 0 ||
            productId <= 0 ||
            externalOrderId.Length == 0 ||
            externalOrderId.Length > 150)
        {
            return BadRequest("Gecersiz baglanti bilgisi.");
        }

        var db = Open();
        var map = Detect(db);

        if (map == null ||
            !Has(db, "MCExternalSyncLines",
                "ExternalOrderId", "ProductId",
                "MatchStatus", "ImportId"))
        {
            return BadRequest(
                "Siparis veya sevkiyat semasi dogrulanamadi.");
        }

        using var tx = db.BeginTransaction();

        using (var check = db.CreateCommand())
        {
            check.Transaction = tx;
            check.CommandText =
                "SELECT COUNT(*) FROM " + Q(map.Shipments) +
                " WHERE Id=@id";

            check.Parameters.AddWithValue("@id", shipmentId);

            if (Convert.ToInt64(check.ExecuteScalar()) != 1)
                return BadRequest("Gercek sevkiyat kaydi bulunamadi.");
        }

        using (var check = db.CreateCommand())
        {
            check.Transaction = tx;

            check.CommandText = """
                SELECT COUNT(*),
                       COUNT(DISTINCT ImportId),
                       SUM(CASE
                           WHEN ProductId=@product
                            AND MatchStatus='ESLESTI'
                           THEN 1 ELSE 0 END),
                       SUM(CASE
                           WHEN ProductId IS NULL
                             OR MatchStatus<>'ESLESTI'
                           THEN 1 ELSE 0 END)
                FROM MCExternalSyncLines
                WHERE ExternalOrderId=@order COLLATE NOCASE
                """;

            check.Parameters.AddWithValue(
                "@order", externalOrderId);

            check.Parameters.AddWithValue(
                "@product", productId);

            using var reader = check.ExecuteReader();

            if (!reader.Read() ||
                Convert.ToInt64(reader.GetValue(0)) == 0)
            {
                return BadRequest("Harici siparis bulunamadi.");
            }

            long fileCount = Convert.ToInt64(reader.GetValue(1));

            long matched = reader.IsDBNull(2)
                ? 0 : Convert.ToInt64(reader.GetValue(2));

            long unmatched = reader.IsDBNull(3)
                ? 0 : Convert.ToInt64(reader.GetValue(3));

            if (fileCount > 1)
                return Conflict(
                    "Bu siparis birden fazla aktarim dosyasinda mevcut.");

            if (matched == 0)
                return BadRequest(
                    "Bu urun harici siparisle eslesmiyor.");

            if (unmatched > 0)
                return BadRequest(
                    "Sipariste eslesmeyen urun satirlari var.");
        }

        if (!ShipmentHasProduct(
            db, tx, map, shipmentId, productId))
        {
            return BadRequest(
                "Bu sevkiyatin stok cikis hareketlerinde " +
                "secili urun bulunamadi. Baglanti kaydedilmedi.");
        }

        using (var create = db.CreateCommand())
        {
            create.Transaction = tx;

            create.CommandText = """
                CREATE TABLE IF NOT EXISTS
                MCExternalOrderShipmentLinks (
                    ShipmentId INTEGER NOT NULL,
                    ExternalOrderId TEXT NOT NULL COLLATE NOCASE,
                    ProductId INTEGER NOT NULL,
                    LinkedAtUtc TEXT NOT NULL,
                    PRIMARY KEY(ShipmentId,ProductId)
                )
                """;

            create.ExecuteNonQuery();
        }

        try
        {
            using (var insert = db.CreateCommand())
            {
                insert.Transaction = tx;

                insert.CommandText = """
                    INSERT INTO MCExternalOrderShipmentLinks
                    (ShipmentId,ExternalOrderId,ProductId,LinkedAtUtc)
                    VALUES (@shipment,@order,@product,@date)
                    """;

                insert.Parameters.AddWithValue(
                    "@shipment", shipmentId);

                insert.Parameters.AddWithValue(
                    "@order", externalOrderId);

                insert.Parameters.AddWithValue(
                    "@product", productId);

                insert.Parameters.AddWithValue(
                    "@date", DateTime.UtcNow.ToString("O"));

                insert.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch (SqliteException ex)
            when (ex.SqliteErrorCode == 19)
        {
            return Conflict(
                "Bu sevkiyat ve urun icin daha once " +
                "bir siparis baglantisi kaydedilmis. " +
                "Mevcut baglanti degistirilmedi.");
        }

        return Redirect("/MCExternalOrders/shipmentLinks");
    }
}