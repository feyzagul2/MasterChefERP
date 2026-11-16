using System;
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

// MC_EXTERNAL_PRODUCTION_LINK_V1
[Authorize(Roles = AppRoles.Admin)]
[Route("MCExternalOrders/productionLinks")]
public sealed class MCExternalOrderProductionLinksController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IAntiforgery _antiforgery;

    public MCExternalOrderProductionLinksController(
        ApplicationDbContext db,
        IAntiforgery antiforgery)
    {
        _db = db;
        _antiforgery = antiforgery;
    }

    private static string H(object? value) =>
        WebUtility.HtmlEncode(
            Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");

    private static string Q(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private SqliteConnection Open()
    {
        if (_db.Database.ProviderName?.Contains(
            "Sqlite", StringComparison.OrdinalIgnoreCase) != true)
        {
            throw new InvalidOperationException(
                "SQLite veritabani gerekli.");
        }

        var db = (SqliteConnection)_db.Database.GetDbConnection();

        if (db.State != ConnectionState.Open)
            _db.Database.OpenConnection();

        return db;
    }

    private static bool Exists(SqliteConnection db, string name)
    {
        using var cmd = db.CreateCommand();

        cmd.CommandText =
            "SELECT COUNT(*) FROM sqlite_master " +
            "WHERE type='table' AND name=@name";

        cmd.Parameters.AddWithValue("@name", name);

        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    private static bool HasColumns(
        SqliteConnection db,
        string table,
        params string[] required)
    {
        if (!Exists(db, table))
            return false;

        using var cmd = db.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(" + Q(table) + ")";

        var names = new System.Collections.Generic.HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
            names.Add(reader.GetString(1));

        return required.All(names.Contains);
    }

    private static (string? Orders, string? Recipes) Schema(
        SqliteConnection db)
    {
        string? orders = null;
        string? recipes = null;

        foreach (var candidate in new[]
        {
            "ProductionOrders", "ProductionOrder"
        })
        {
            if (HasColumns(db, candidate,
                "Id", "RecipeId", "Status", "PlannedQuantityKg"))
            {
                orders = candidate;
                break;
            }
        }

        foreach (var candidate in new[]
        {
            "ProductionRecipes", "Recipes", "ProductionRecipe"
        })
        {
            if (HasColumns(db, candidate, "Id", "ProductId"))
            {
                recipes = candidate;
                break;
            }
        }

        return (orders, recipes);
    }

    private static string Page(string body) =>
        "<!doctype html><html lang='tr'><head>" +
        "<meta charset='utf-8'>" +
        "<meta name='viewport' content='width=device-width,initial-scale=1'>" +
        "<title>MASTER CHEF - Siparis Uretim Baglantisi</title>" +
        "<style>" +
        "*{box-sizing:border-box}" +
        "body{font-family:Arial;background:#f1f5f9;" +
        "color:#172033;margin:0;padding:28px}" +
        "main{max-width:1300px;margin:auto}" +
        "section{background:#fff;border:1px solid #e2e8f0;" +
        "border-radius:12px;padding:24px;margin:20px 0}" +
        ".warn{background:#fff7ed;border:1px solid #fdba74;" +
        "padding:14px;border-radius:9px}" +
        ".good{background:#ecfdf5;border:1px solid #86efac;" +
        "padding:14px;border-radius:9px}" +
        "table{width:100%;border-collapse:collapse}" +
        "th,td{text-align:left;padding:11px;" +
        "border-bottom:1px solid #e2e8f0}" +
        "th{background:#eff6ff}" +
        ".scroll{overflow-x:auto}" +
        "input{padding:12px;border:1px solid #cbd5e1;" +
        "border-radius:7px;margin:5px;max-width:100%}" +
        "button,.button{background:#183b5a;color:#fff;" +
        "border:0;border-radius:7px;padding:12px 17px;" +
        "cursor:pointer;text-decoration:none;" +
        "display:inline-block;margin:6px}" +
        "</style></head><body><main>" +
        body + "</main></body></html>";

    private static string DataTable(
        SqliteConnection db,
        string sql,
        string[] headers)
    {
        var html = new StringBuilder();

        html.Append("<div class='scroll'><table><tr>");

        foreach (var header in headers)
            html.Append("<th>" + H(header) + "</th>");

        html.Append("</tr>");

        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;

        using var reader = cmd.ExecuteReader();

        var count = 0;

        while (reader.Read())
        {
            count++;
            html.Append("<tr>");

            for (int i = 0; i < reader.FieldCount; i++)
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
        {
            html.Append("<tr><td colspan='" +
                headers.Length + "'>Kayit bulunamadi.</td></tr>");
        }

        html.Append("</table></div>");

        return html.ToString();
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        var db = Open();

        var html = new StringBuilder();

        html.Append(
            "<h1>MASTER CHEF - Siparis / Uretim Emri Baglantisi</h1>" +
            "<p>Harici siparislerin mevcut uretim " +
            "emirleriyle kontrollu eslestirilmesi.</p>");

        var schema = Schema(db);

        if (schema.Orders == null ||
            schema.Recipes == null ||
            !HasColumns(db, "MCExternalSyncLines",
                "ExternalOrderId", "ProductId", "MatchStatus", "ImportId"))
        {
            html.Append(
                "<section><p class='warn'>" +
                "Siparis veya uretim tablolari beklenen " +
                "yapida degil. Kayit yapilmadi.</p></section>");

            return Content(
                Page(html.ToString()),
                "text/html; charset=utf-8");
        }

        html.Append(
            "<p class='warn'><strong>Onemli:</strong> " +
            "Bu ekran uretim emrini siparisle iliskilendirir. " +
            "Uretim miktarini siparise tahsis etmez. " +
            "Ayni urun icin birden fazla siparis olabilir. " +
            "Gercek siparis iliskisini kontrol ederek kaydedin." +
            "</p>");

        string token = H(
            _antiforgery.GetAndStoreTokens(HttpContext).RequestToken);

        html.Append(
            "<section><h2>Siparis ve Uretim Emri Bagla</h2>" +
            "<form method='post' " +
            "action='/MCExternalOrders/productionLinks'>" +
            "<input type='hidden' " +
            "name='__RequestVerificationToken' value='" +
            token + "'>" +
            "<label>Uretim Emri ID</label>" +
            "<input type='number' name='productionOrderId' " +
            "min='1' required>" +
            "<label>Harici Siparis Numarasi</label>" +
            "<input type='text' name='externalOrderId' " +
            "maxlength='150' required>" +
            "<button type='submit'>Baglantiyi Kaydet</button>" +
            "</form></section>");

        string orders = Q(schema.Orders);
        string recipes = Q(schema.Recipes);

        html.Append(
            "<section><h2>Acik Uretim Emirleri</h2>" +
            "<p>Son 100 acik uretim emri.</p>");

        html.Append(DataTable(db,
            "SELECT o.Id, r.ProductId, o.Status, " +
            "o.PlannedQuantityKg " +
            "FROM " + orders + " o " +
            "JOIN " + recipes + " r ON r.Id=o.RecipeId " +
            "WHERE o.Status IS NOT NULL AND " +
            "LOWER(TRIM(o.Status)) NOT IN " +
            "('completed','complete','finished'," +
            "'cancelled','canceled','iptal','tamamlandi') " +
            "ORDER BY o.Id DESC LIMIT 100",
            new[]
            {
                "Uretim Emri ID", "Urun Kart ID",
                "Durum", "Planlanan Kg"
            }));

        html.Append("</section>");

        html.Append(
            "<section><h2>Harici Siparisler</h2>" +
            "<p>Urun karti eslesen siparisler. " +
            "Tekrarlanan aktarimlar ayrica kontrol edilmelidir.</p>");

        html.Append(DataTable(db, """
            SELECT
                ExternalOrderId,
                ProductId,
                COUNT(*) AS LineCount
            FROM MCExternalSyncLines
            WHERE ProductId IS NOT NULL
              AND MatchStatus='ESLESTI'
            GROUP BY
                ExternalOrderId COLLATE NOCASE,
                ProductId
            ORDER BY MAX(Id) DESC
            LIMIT 150
            """,
            new[]
            {
                "Harici Siparis No", "Urun Kart ID", "Satir"
            }));

        html.Append("</section>");

        html.Append("<section><h2>Kaydedilmis Baglantilar</h2>");

        if (Exists(db, "MCExternalProductionOrderLinks"))
        {
            html.Append(DataTable(db, """
                SELECT
                    ProductionOrderId,
                    ExternalOrderId,
                    ProductId,
                    LinkedAtUtc
                FROM MCExternalProductionOrderLinks
                ORDER BY ProductionOrderId DESC
                LIMIT 150
                """,
                new[]
                {
                    "Uretim Emri ID", "Harici Siparis",
                    "Urun Kart ID", "Kayit Tarihi UTC"
                }));
        }
        else
        {
            html.Append("<p>Henuz baglanti kaydi bulunmuyor.</p>");
        }

        html.Append("</section>");

        html.Append(
            "<section>" +
            "<a class='button' " +
            "href='/MCExternalOrders/stockReview/productionCoverage'>" +
            "Uretim Emri Karsilastirmasi</a>" +
            "<a class='button' " +
            "href='/MCProductionPlanning/management'>" +
            "Yonetim Merkezi</a>" +
            "</section>");

        return Content(
            Page(html.ToString()),
            "text/html; charset=utf-8");
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public IActionResult Save(
        long productionOrderId,
        string externalOrderId)
    {
        externalOrderId = (externalOrderId ?? "").Trim();

        if (productionOrderId <= 0 ||
            externalOrderId.Length == 0 ||
            externalOrderId.Length > 150)
        {
            return BadRequest("Gecersiz baglanti bilgisi.");
        }

        var db = Open();
        var schema = Schema(db);

        if (schema.Orders == null ||
            schema.Recipes == null ||
            !HasColumns(db, "MCExternalSyncLines",
                "ExternalOrderId", "ProductId",
                "MatchStatus", "ImportId"))
        {
            return BadRequest(
                "Uretim veya siparis tablolari dogrulanamadi.");
        }

        using var tx = db.BeginTransaction();

        try
        {
            long productId;

            using (var cmd = db.CreateCommand())
            {
                cmd.Transaction = tx;

                cmd.CommandText =
                    "SELECT r.ProductId " +
                    "FROM " + Q(schema.Orders) + " o " +
                    "JOIN " + Q(schema.Recipes) +
                    " r ON r.Id=o.RecipeId " +
                    "WHERE o.Id=@id AND o.Status IS NOT NULL " +
                    "AND LOWER(TRIM(o.Status)) NOT IN " +
                    "('completed','complete','finished'," +
                    "'cancelled','canceled','iptal','tamamlandi')";

                cmd.Parameters.AddWithValue(
                    "@id", productionOrderId);

                var value = cmd.ExecuteScalar();

                if (value == null || value == DBNull.Value)
                {
                    return BadRequest(
                        "Acik uretim emri bulunamadi.");
                }

                productId = Convert.ToInt64(value);
            }

            using (var cmd = db.CreateCommand())
            {
                cmd.Transaction = tx;

                cmd.CommandText = """
                    SELECT
                        COUNT(*),
                        COUNT(DISTINCT ImportId),
                        SUM(
                            CASE
                            WHEN ProductId=@product
                             AND MatchStatus='ESLESTI'
                            THEN 1 ELSE 0 END
                        ),
                        SUM(
                            CASE
                            WHEN ProductId IS NULL
                              OR MatchStatus<>'ESLESTI'
                            THEN 1 ELSE 0 END
                        )
                    FROM MCExternalSyncLines
                    WHERE ExternalOrderId=@order COLLATE NOCASE
                    """;

                cmd.Parameters.AddWithValue(
                    "@order", externalOrderId);

                cmd.Parameters.AddWithValue(
                    "@product", productId);

                using var reader = cmd.ExecuteReader();

                if (!reader.Read() ||
                    Convert.ToInt64(reader.GetValue(0)) == 0)
                {
                    return BadRequest(
                        "Harici siparis bulunamadi.");
                }

                long files = Convert.ToInt64(reader.GetValue(1));

                long matching = reader.IsDBNull(2)
                    ? 0
                    : Convert.ToInt64(reader.GetValue(2));

                long unmatched = reader.IsDBNull(3)
                    ? 0
                    : Convert.ToInt64(reader.GetValue(3));

                if (files > 1)
                {
                    return Conflict(
                        "Bu siparis birden fazla dosyada mevcut. " +
                        "Once mukerrer aktarimlar incelenmeli.");
                }

                if (matching == 0)
                {
                    return BadRequest(
                        "Uretim emrinin urunu bu sipariste bulunmuyor.");
                }

                if (unmatched > 0)
                {
                    return BadRequest(
                        "Sipariste eslesmeyen urun satirlari var. " +
                        "Once urun eslestirmelerini tamamlayin.");
                }
            }

            using (var create = db.CreateCommand())
            {
                create.Transaction = tx;

                create.CommandText = """
                    CREATE TABLE IF NOT EXISTS
                    MCExternalProductionOrderLinks (
                        ProductionOrderId INTEGER PRIMARY KEY,
                        ExternalOrderId TEXT NOT NULL
                            COLLATE NOCASE,
                        ProductId INTEGER NOT NULL,
                        LinkedAtUtc TEXT NOT NULL
                    )
                    """;

                create.ExecuteNonQuery();
            }

            using (var cmd = db.CreateCommand())
            {
                cmd.Transaction = tx;

                cmd.CommandText = """
                    SELECT ExternalOrderId,ProductId
                    FROM MCExternalProductionOrderLinks
                    WHERE ProductionOrderId=@id
                    """;

                cmd.Parameters.AddWithValue(
                    "@id", productionOrderId);

                using var reader = cmd.ExecuteReader();

                if (reader.Read())
                {
                    var existingOrder = reader.GetString(0);

                    long existingProduct =
                        Convert.ToInt64(reader.GetValue(1));

                    if (string.Equals(
                            existingOrder,
                            externalOrderId,
                            StringComparison.OrdinalIgnoreCase) &&
                        existingProduct == productId)
                    {
                        return Redirect(
                            "/MCExternalOrders/productionLinks");
                    }

                    return Conflict(
                        "Bu uretim emri baska bir siparise " +
                        "baglanmis. Kayit degistirilmedi.");
                }
            }

            using (var insert = db.CreateCommand())
            {
                insert.Transaction = tx;

                insert.CommandText = """
                    INSERT INTO MCExternalProductionOrderLinks
                    (
                        ProductionOrderId,
                        ExternalOrderId,
                        ProductId,
                        LinkedAtUtc
                    )
                    VALUES
                    (
                        @production,
                        @order,
                        @product,
                        @date
                    )
                    """;

                insert.Parameters.AddWithValue(
                    "@production", productionOrderId);

                insert.Parameters.AddWithValue(
                    "@order", externalOrderId);

                insert.Parameters.AddWithValue(
                    "@product", productId);

                insert.Parameters.AddWithValue(
                    "@date", DateTime.UtcNow.ToString("O"));

                insert.ExecuteNonQuery();
            }

            tx.Commit();

            return Redirect(
                "/MCExternalOrders/productionLinks");
        }
        catch (SqliteException ex)
            when (ex.SqliteErrorCode == 19)
        {
            return Conflict(
                "Bu uretim emri icin kayit zaten mevcut. " +
                "Mukerrer baglanti olusturulmadi.");
        }
    }
}