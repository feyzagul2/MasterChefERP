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

// MC_EXTERNAL_ORDER_MATCH_REFRESH_V1
[Authorize(Roles = AppRoles.Admin)]
[Route("MCExternalOrders/refreshMatches")]
public sealed class MCExternalOrderMatchRefreshController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IAntiforgery _antiforgery;

    public MCExternalOrderMatchRefreshController(
        ApplicationDbContext db,
        IAntiforgery antiforgery)
    {
        _db = db;
        _antiforgery = antiforgery;
    }

    private static string H(object? value) =>
        WebUtility.HtmlEncode(
            Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");

    private static bool Exists(SqliteConnection db, string table)
    {
        using var cmd = db.CreateCommand();

        cmd.CommandText =
            "SELECT COUNT(*) FROM sqlite_master " +
            "WHERE type='table' AND name=@name";

        cmd.Parameters.AddWithValue("@name", table);

        return Convert.ToInt64(cmd.ExecuteScalar()) == 1;
    }

    private static HashSet<string> Columns(
        SqliteConnection db,
        string table)
    {
        var columns = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        using var cmd = db.CreateCommand();

        cmd.CommandText = "PRAGMA table_info(\"" + table + "\")";

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
            columns.Add(reader.GetString(1));

        return columns;
    }

    private static string? CodeColumn(SqliteConnection db)
    {
        if (!Exists(db, "Products"))
            return null;

        var columns = Columns(db, "Products");

        if (!columns.Contains("Id"))
            return null;

        foreach (var name in new[]
        {
            "ProductCode",
            "StockCode",
            "Code",
            "Sku",
            "SKU",
            "ItemCode",
            "Barcode"
        })
        {
            if (columns.Contains(name))
                return name;
        }

        return null;
    }

    private static (long? Id, string Status) Match(
        SqliteConnection db,
        string? column,
        string code)
    {
        if (column == null)
            return (null, "KOD_ALANI_YOK");

        using var cmd = db.CreateCommand();

        cmd.CommandText =
            "SELECT Id FROM Products WHERE \"" +
            column +
            "\" = @code COLLATE NOCASE LIMIT 2";

        cmd.Parameters.AddWithValue("@code", code);

        var ids = new List<long>();

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
            ids.Add(Convert.ToInt64(reader.GetValue(0)));

        if (ids.Count == 0)
            return (null, "ESLESMEDI");

        if (ids.Count > 1)
            return (null, "BELIRSIZ");

        return (ids[0], "ESLESTI");
    }

    private sealed record Line(
        long Id,
        string ProductCode,
        long? OldProductId,
        string OldStatus
    );

    private static List<Line> Unmatched(SqliteConnection db)
    {
        var result = new List<Line>();

        using var cmd = db.CreateCommand();

        cmd.CommandText = """
            SELECT Id, ProductCode, ProductId, MatchStatus
            FROM MCExternalSyncLines
            WHERE ProductId IS NULL
               OR MatchStatus <> 'ESLESTI'
            ORDER BY Id
            """;

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            result.Add(new Line(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.IsDBNull(2)
                    ? null
                    : Convert.ToInt64(reader.GetValue(2)),
                reader.GetString(3)
            ));
        }

        return result;
    }

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

    private string Token() =>
        "<input type='hidden' name='__RequestVerificationToken' value='" +
        H(_antiforgery.GetAndStoreTokens(HttpContext).RequestToken) +
        "'>";

    private static string Page(string body) =>
        "<!doctype html><html lang='tr'><head>" +
        "<meta charset='utf-8'>" +
        "<meta name='viewport' content='width=device-width,initial-scale=1'>" +
        "<title>MASTER CHEF - Urun Eslestirme</title>" +
        "<style>" +
        "body{font-family:Arial;background:#f1f5f9;color:#172033;" +
        "padding:28px;margin:0}" +
        "main{max-width:1100px;margin:auto}" +
        "section{background:white;border-radius:12px;padding:24px;" +
        "margin:18px 0;border:1px solid #e2e8f0}" +
        "table{width:100%;border-collapse:collapse}" +
        "th,td{padding:11px;border-bottom:1px solid #e2e8f0;" +
        "text-align:left}" +
        "th{background:#eff6ff}" +
        "button{background:#183b5a;color:white;border:0;" +
        "border-radius:7px;padding:13px 20px;cursor:pointer}" +
        "a{color:#1d4ed8}" +
        "</style></head><body><main>" +
        "<h1>MASTER CHEF - Siparis Urun Eslestirme</h1>" +
        body +
        "</main></body></html>";

    [HttpGet("")]
    public IActionResult Index()
    {
        var db = Open();

        if (!Exists(db, "MCExternalSyncLines"))
        {
            return Content(Page(
                "<section><p>Henuz aktarilmis siparis bulunmuyor.</p>" +
                "<a href='/MCExternalOrders/import'>Siparis aktarimi</a>" +
                "</section>"),
                "text/html; charset=utf-8");
        }

        var lines = Unmatched(db);
        var column = CodeColumn(db);

        var cache = new Dictionary<
            string, (long? Id, string Status)>(
                StringComparer.OrdinalIgnoreCase);

        var html = new StringBuilder();

        html.Append("<section><h2>Guncel Eslestirme Kontrolu</h2>");
        html.Append("<p>Eslesmeyen siparis satiri: <strong>");
        html.Append(lines.Count);
        html.Append("</strong></p>");

        if (column == null)
        {
            html.Append(
                "<p>Urun kodu sutunu bulunamadi. " +
                "Eslesme yenilemesi yapilamaz.</p>");
        }

        int newlyMatched = 0;
        int remaining = 0;

        html.Append("<table><tr><th>Urun Kodu</th>");
        html.Append("<th>Mevcut Durum</th>");
        html.Append("<th>Yeni Durum</th></tr>");

        foreach (var line in lines)
        {
            if (!cache.TryGetValue(line.ProductCode, out var match))
            {
                match = Match(db, column, line.ProductCode);
                cache[line.ProductCode] = match;
            }

            if (match.Id.HasValue)
                newlyMatched++;
            else
                remaining++;

            if (newlyMatched + remaining <= 200)
            {
                html.Append("<tr><td>");
                html.Append(H(line.ProductCode));
                html.Append("</td><td>");
                html.Append(H(line.OldStatus));
                html.Append("</td><td>");
                html.Append(H(match.Status));
                html.Append("</td></tr>");
            }
        }

        html.Append("</table>");

        html.Append("<p>Yeni eslesebilecek satir: <strong>");
        html.Append(newlyMatched);
        html.Append("</strong></p>");

        html.Append("<p>Eslesmeden kalacak satir: <strong>");
        html.Append(remaining);
        html.Append("</strong></p>");

        if (lines.Count > 200)
            html.Append("<p>Ilk 200 satir gosteriliyor.</p>");

        html.Append(
            "<p>Bu ekran sadece kontrol yapti. " +
            "Henuz hicbir kayit guncellenmedi.</p>");

        if (column != null && newlyMatched > 0)
        {
            html.Append(
                "<form method='post' " +
                "action='/MCExternalOrders/refreshMatches'>");

            html.Append(Token());

            html.Append(
                "<button type='submit'>" +
                "Urun Eslestirmelerini Guncelle" +
                "</button></form>");
        }

        html.Append("</section>");

        html.Append(
            "<section><a href='/MCExternalOrders/reconcile'>" +
            "Siparis Mutabakat Merkezi</a></section>");

        return Content(
            Page(html.ToString()),
            "text/html; charset=utf-8");
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async System.Threading.Tasks.Task<IActionResult> Refresh()
    {
        var db = Open();

        if (!Exists(db, "MCExternalSyncLines"))
            return BadRequest("Aktarilmis siparis bulunamadi.");

        var column = CodeColumn(db);

        if (column == null)
            return BadRequest("Urun kodu alani bulunamadi.");

        var lines = Unmatched(db);

        var cache = new Dictionary<
            string, (long? Id, string Status)>(
                StringComparer.OrdinalIgnoreCase);

        var updates = new List<(long LineId, long ProductId)>();

        foreach (var line in lines)
        {
            if (!cache.TryGetValue(line.ProductCode, out var match))
            {
                match = Match(db, column, line.ProductCode);
                cache[line.ProductCode] = match;
            }

            if (match.Id.HasValue)
                updates.Add((line.Id, match.Id.Value));
        }

        if (updates.Count == 0)
        {
            return Content(Page(
                "<section><p>Guncellenecek urun eslesmesi yok.</p>" +
                "<a href='/MCExternalOrders/refreshMatches'>" +
                "Kontrol ekranina don</a></section>"),
                "text/html; charset=utf-8");
        }

        await using var tx = await db.BeginTransactionAsync();

        try
        {
            foreach (var item in updates)
            {
                using var cmd = db.CreateCommand();

                cmd.Transaction = (SqliteTransaction)tx;

                cmd.CommandText = """
                    UPDATE MCExternalSyncLines
                    SET ProductId=@product,
                        MatchStatus='ESLESTI'
                    WHERE Id=@id
                      AND (
                          ProductId IS NULL
                          OR MatchStatus<>'ESLESTI'
                      )
                    """;

                cmd.Parameters.AddWithValue("@product", item.ProductId);
                cmd.Parameters.AddWithValue("@id", item.LineId);

                cmd.ExecuteNonQuery();
            }

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        return Content(Page(
            "<section><h2>Urun Eslestirmeleri Guncellendi</h2>" +
            "<p>Eslesen satir sayisi: <strong>" +
            updates.Count.ToString(CultureInfo.InvariantCulture) +
            "</strong></p>" +
            "<p>Mevcut siparis numaralari ve miktarlari degistirilmedi.</p>" +
            "<p>Stok, sevkiyat ve finans hareketi olusturulmadi.</p>" +
            "<a href='/MCExternalOrders/reconcile'>" +
            "Siparis Mutabakat Merkezine Don</a>" +
            "</section>"),
            "text/html; charset=utf-8");
    }
}