using MasterChefTakip.Models;
using System;
using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

namespace MasterChefTakip.Controllers;

[Authorize(Policy = AppPermissions.PurchaseManage)]
[Route("MCPurchaseSupplierCariLinks")]
public sealed class MCPurchaseSupplierCariLinksController : Controller
{
    private readonly IConfiguration _config;
    private readonly IAntiforgery _antiforgery;

    public MCPurchaseSupplierCariLinksController(
        IConfiguration config,
        IAntiforgery antiforgery)
    {
        _config = config;
        _antiforgery = antiforgery;
    }

    private static string H(object? value) =>
        WebUtility.HtmlEncode(Convert.ToString(value) ?? "");

    private async Task<SqliteConnection> OpenDb()
    {
        var cs = _config.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException("Veritabani baglantisi yok.");

        var db = new SqliteConnection(cs);
        await db.OpenAsync();
        return db;
    }

    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index()
    {
        await using var db = await OpenDb();

        var html = new StringBuilder();

        html.Append("""
            <!doctype html>
            <html lang="tr">
            <head><meta charset="utf-8">
            <title>MASTER CHEF - Tedarikçi Cari Eşleştirme</title>
            <style>
            body{font-family:Arial;background:#f1f5f9;padding:25px}
            main{max-width:1150px;margin:auto;background:white;
                 padding:25px;border-radius:12px}
            table{width:100%;border-collapse:collapse;margin:20px 0}
            th,td{padding:12px;border-bottom:1px solid #ddd;text-align:left}
            th{background:#edf2f7}
            select,button{padding:10px;border-radius:6px}
            button{background:#166534;color:#fff;border:0;cursor:pointer}
            .warn{background:#fff3cd;padding:14px;border-radius:7px}
            </style></head><body><main>
            <h1>Tedarikçi - Cari Hesap Eşleştirme</h1>
            <p class="warn">
            Bu işlem yalnızca tedarikçi ile mevcut cari hesabı eşleştirir.
            Stok hareketi veya finansal borç oluşturmaz.
            </p>
            """);

        var hasLinks = false;

        await using (var check = db.CreateCommand())
        {
            check.CommandText = """
                SELECT COUNT(*) FROM sqlite_master
                WHERE type='table'
                  AND name='MCPurchaseSupplierCariLinks'
                """;

            hasLinks =
                Convert.ToInt64(await check.ExecuteScalarAsync()) > 0;
        }

        html.Append("<h2>Mevcut Eşleştirmeler</h2>");

        if (!hasLinks)
        {
            html.Append("<p>Henüz eşleştirme yapılmamış.</p>");
        }
        else
        {
            html.Append("""
                <table><tr>
                <th>Tedarikçi ID</th>
                <th>Cari Hesap ID</th>
                <th>Para Birimi</th>
                <th>Kayıt Tarihi</th>
                </tr>
                """);

            await using var cmd = db.CreateCommand();

            cmd.CommandText = """
                SELECT SupplierId, CariAccountId,
                       Currency, CreatedAtUtc
                FROM MCPurchaseSupplierCariLinks
                ORDER BY SupplierId, CariAccountId
                """;

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                html.Append("<tr>");

                for (var i = 0; i < 4; i++)
                {
                    html.Append("<td>");
                    html.Append(H(reader.GetValue(i)));
                    html.Append("</td>");
                }

                html.Append("</tr>");
            }

            html.Append("</table>");
        }

        var suppliers = new List<long>();

        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                SELECT DISTINCT SupplierId
                FROM PurchaseDispatchNotes
                WHERE SupplierId IS NOT NULL
                UNION
                SELECT DISTINCT SupplierId
                FROM PurchaseOrders
                WHERE SupplierId IS NOT NULL
                ORDER BY SupplierId
                """;

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
                suppliers.Add(reader.GetInt64(0));
        }

        var accounts = new List<(long Id, string Currency)>();

        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                SELECT Id, Currency
                FROM CariAccounts
                WHERE IsActive = 1
                ORDER BY Id
                """;

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
                accounts.Add((
                    reader.GetInt64(0),
                    reader.IsDBNull(1) ? "" : reader.GetString(1)));
        }

        var token = _antiforgery
            .GetAndStoreTokens(HttpContext)
            .RequestToken ?? "";

        html.Append("""
            <h2>Yeni Eşleştirme</h2>
            <form method="post"
                  action="/MCPurchaseSupplierCariLinks/Save">
            """);

        html.Append("<input type='hidden' name='__RequestVerificationToken' value='");
        html.Append(H(token));
        html.Append("'>");

        html.Append("<p><label>Tedarikçi</label><br>");
        html.Append("<select name='supplierId' required>");
        html.Append("<option value=''>Tedarikçi seçiniz</option>");

        foreach (var id in suppliers)
        {
            html.Append("<option value='");
            html.Append(id);
            html.Append("'>Tedarikçi #");
            html.Append(id);
            html.Append("</option>");
        }

        html.Append("</select></p>");

        html.Append("<p><label>Cari Hesap</label><br>");
        html.Append("<select name='cariAccountId' required>");
        html.Append("<option value=''>Cari hesap seçiniz</option>");

        foreach (var account in accounts)
        {
            html.Append("<option value='");
            html.Append(account.Id);
            html.Append("'>Cari #");
            html.Append(account.Id);
            html.Append(" - ");
            html.Append(H(account.Currency));
            html.Append("</option>");
        }

        html.Append("""
            </select></p>
            <button type="submit">Tedarikçi Cari Hesabını Eşleştir</button>
            </form>
            </main></body></html>
            """);

        return View(
    "~/Views/Shared/MCPurchaseEmbeddedPage.cshtml",
    new MasterChefTakip.Models.MCPurchaseEmbeddedPageModel
    {
        Html = html.ToString()
    }
);
    }

    [HttpPost("Save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(
        [FromForm] long supplierId,
        [FromForm] long cariAccountId)
    {
        if (supplierId <= 0 || cariAccountId <= 0)
            return BadRequest("Gecersiz tedarikci veya cari hesap.");

        await using var db = await OpenDb();

        await using var tx =
            (SqliteTransaction)await db.BeginTransactionAsync();

        string currency;

        await using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT Currency
                FROM CariAccounts
                WHERE Id=@id AND IsActive=1
                """;

            cmd.Parameters.AddWithValue("@id", cariAccountId);

            var result = await cmd.ExecuteScalarAsync();

            if (result == null)
                return BadRequest("Aktif cari hesap bulunamadi.");

            currency = Convert.ToString(result)?.Trim() ?? "";
        }

        if (string.IsNullOrWhiteSpace(currency))
            return BadRequest("Cari hesap para birimi bos olamaz.");

        await using (var check = db.CreateCommand())
        {
            check.Transaction = tx;
            check.CommandText = """
                SELECT COUNT(*)
                FROM (
                    SELECT SupplierId FROM PurchaseOrders
                    WHERE SupplierId=@supplier
                    UNION
                    SELECT SupplierId FROM PurchaseDispatchNotes
                    WHERE SupplierId=@supplier
                )
                """;

            check.Parameters.AddWithValue("@supplier", supplierId);

            if (Convert.ToInt64(await check.ExecuteScalarAsync()) == 0)
                return BadRequest(
                    "Tedarikci satin alma kayitlarinda bulunamadi.");
        }

        await using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS MCPurchaseSupplierCariLinks
                (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SupplierId INTEGER NOT NULL,
                    CariAccountId INTEGER NOT NULL UNIQUE,
                    Currency TEXT NOT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    CreatedBy TEXT NOT NULL,
                    UNIQUE(SupplierId, Currency)
                )
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO MCPurchaseSupplierCariLinks
                (
                    SupplierId, CariAccountId, Currency,
                    CreatedAtUtc, CreatedBy
                )
                VALUES
                (
                    @supplier, @cari, @currency,
                    @utc, @actor
                )
                """;

            cmd.Parameters.AddWithValue("@supplier", supplierId);
            cmd.Parameters.AddWithValue("@cari", cariAccountId);
            cmd.Parameters.AddWithValue("@currency", currency);
            cmd.Parameters.AddWithValue(
                "@utc", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue(
                "@actor", User.Identity?.Name ?? "AuthenticatedUser");

            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                return Conflict(
                    "Bu tedarikci veya cari hesap daha once eslestirilmis.");
            }
        }

        await tx.CommitAsync();

        return RedirectToAction(nameof(Index));
    }
}