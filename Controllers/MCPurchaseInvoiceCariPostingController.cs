using MasterChefTakip.Models;
using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

namespace MasterChefTakip.Controllers;

[Authorize(Policy = AppPermissions.PurchaseManage)]
[Route("MCPurchaseInvoiceCariPosting")]
public sealed class MCPurchaseInvoiceCariPostingController : Controller
{
    private readonly IConfiguration _config;
    private readonly IAntiforgery _antiforgery;

    public MCPurchaseInvoiceCariPostingController(
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

    private static async Task<bool> HasSupplierLink(SqliteConnection db)
    {
        await using var cmd = db.CreateCommand();
        // MC_PURCHASE_CARI_LINK_INTEGRATION_V3
        cmd.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type='table'
              AND name='MCPurchaseSupplierCariLinks'
            """;

        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
    }

    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index()
    {
        await using var db = await OpenDb();

        var supplierLink = await HasSupplierLink(db);
        var token = _antiforgery.GetAndStoreTokens(HttpContext)
            .RequestToken ?? "";

        var html = new StringBuilder();

        html.Append("""
        <!doctype html>
        <html lang="tr"><head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width,initial-scale=1">
        <title>MASTER CHEF - Fatura Cari Aktarımı</title>
        <style>
        body{font-family:Arial,sans-serif;background:#f1f5f9;
        color:#172033;padding:24px}
        main{max-width:1250px;margin:auto;background:white;
        padding:25px;border-radius:12px}
        table{width:100%;border-collapse:collapse;margin-top:20px}
        th,td{padding:10px;text-align:left;border-bottom:1px solid #ddd}
        th{background:#edf2f7}
        button{background:#166534;color:white;border:0;
        border-radius:6px;padding:9px 14px;cursor:pointer}
        input{padding:8px;max-width:100px}
        .warn{background:#fff3cd;padding:14px;border-radius:7px}
        .ok{color:#166534;font-weight:bold}
        a{color:#2563eb}
        </style></head><body><main>
        <a href="/MCPurchaseInvoiceEntry">← Faturalar</a>
        <h1>Satın Alma Faturası - Cari Borç Aktarımı</h1><div style="display:flex;gap:10px;flex-wrap:wrap;margin:14px 0 18px"><a href="/MCPurchaseInvoiceCariCheck" style="background:#eff6ff;padding:9px 13px;border-radius:7px;text-decoration:none">Cari Kontrol →</a><a href="/MCPurchaseCariAudit" style="background:#f0fdf4;padding:9px 13px;border-radius:7px;text-decoration:none">Mutabakat →</a></div>
        <p class="warn">
        Yalnızca kesinleştirilmiş faturalar aktarılabilir.
        Cari hesap, tedarikçi ve para birimi eşleşmelidir.
        Aynı fatura ikinci kez aktarılmaz.
        </p>
        """);

        if (!supplierLink)
        {
            html.Append("""
            <p class="warn">
            <strong>Aktarım güvenlik nedeniyle kapalı.</strong>
            Tedarikçi-cari eşleştirme tablosu henüz oluşturulmamış.
            Doğru tedarikçi-cari ilişkisinin belirlenmesi gerekiyor.
            </p>
            """);
        }

        html.Append("""
        <table><thead><tr>
        <th>Fatura</th><th>Tarih</th><th>Tedarikçi</th>
        <th>Para Birimi</th><th>Toplam</th><th>Durum</th>
        <th>Cari Aktarım</th>
        </tr></thead><tbody>
        """);

        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                SELECT i.Id, i.InvoiceNumber, i.InvoiceDate,
                       i.SupplierId, i.Currency, i.GrandTotal,
                       i.Status,
                       p.CariAccountId
                FROM MCPurchaseInvoices i
                LEFT JOIN MCPurchaseInvoiceCariPostings p
                    ON p.PurchaseInvoiceId = i.Id
                ORDER BY i.Id DESC
                LIMIT 200
                """;

            // The posting table is installed automatically only on POST.
            // Before first posting, use a schema-safe SELECT.
            await using var exists = db.CreateCommand();
            exists.CommandText = """
                SELECT COUNT(*) FROM sqlite_master
                WHERE type='table'
                  AND name='MCPurchaseInvoiceCariPostings'
                """;

            var installed =
                Convert.ToInt32(await exists.ExecuteScalarAsync()) > 0;

            if (!installed)
                cmd.CommandText = """
                    SELECT Id, InvoiceNumber, InvoiceDate,
                           SupplierId, Currency, GrandTotal,
                           Status, NULL
                    FROM MCPurchaseInvoices
                    ORDER BY Id DESC
                    LIMIT 200
                    """;

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                long id = reader.GetInt64(0);
                long supplierId = reader.GetInt64(3);
                string currency = Convert.ToString(reader.GetValue(4)) ?? "";
                string status = Convert.ToString(reader.GetValue(6)) ?? "";
                bool posted = !reader.IsDBNull(7);

                html.Append("<tr>");

                for (int c = 1; c <= 6; c++)
                {
                    html.Append("<td>");
                    html.Append(H(reader.GetValue(c)));
                    html.Append("</td>");
                }

                html.Append("<td>");

                if (posted)
                {
                    html.Append("<span class='ok'>Cari hesaba aktarılmış</span>");
                }
                else if (status != "Finalized")
                {
                    html.Append("Önce faturayı kesinleştirin");
                }
                else if (!supplierLink)
                {
                    html.Append("Tedarikçi-cari bağlantısı eksik");
                }
                else
                {
                                        // MC_CARI_REVIEW_LINK_FIX_V2
                    html.Append("<a href='/MCPurchaseInvoiceCariPosting/Review?invoiceId=");
                    html.Append(id);
                    html.Append("'>Tedarikçi Cari Hesabını Seç</a>");
                }

                html.Append("</td></tr>");
            }
        }

        html.Append("</tbody></table></main></body></html>");

        return View(
    "~/Views/Shared/MCPurchaseEmbeddedPage.cshtml",
    new MasterChefTakip.Models.MCPurchaseEmbeddedPageModel
    {
        Html = html.ToString()
    }
);
    }

    // MC_INVOICE_CARI_REVIEW_V1
    [HttpGet("Review")]
    public async Task<IActionResult> Review([FromQuery] long invoiceId)
    {
        if (invoiceId <= 0)
            return BadRequest("Geçersiz fatura.");

        await using var db = await OpenDb();

        var html = new StringBuilder();

        html.Append("""
        <!doctype html>
        <html lang="tr">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width,initial-scale=1">
        <title>MASTER CHEF - Cari Borç Aktarımı</title>
        <style>
        body{font-family:Arial,sans-serif;background:#f1f5f9;padding:25px;color:#172033}
        main{max-width:1000px;margin:auto;background:white;padding:28px;border-radius:12px}
        table{width:100%;border-collapse:collapse;margin:20px 0}
        th,td{padding:12px;text-align:left;border-bottom:1px solid #ddd}
        th{background:#edf2f7}
        select{padding:12px;width:100%;max-width:600px}
        button{background:#166534;color:white;border:0;border-radius:7px;padding:12px 20px;cursor:pointer}
        .warn{background:#fff3cd;padding:15px;border-radius:7px;margin:15px 0}
        .ok{background:#dcfce7;padding:15px;border-radius:7px;margin:15px 0}
        a{color:#2563eb}
        </style>
        </head>
        <body><main>
        <a href="/MCPurchaseInvoiceCariPosting">← Fatura Listesi</a>
        <h1>Tedarikçi Cari Borç Aktarımı</h1>
        """);

        long supplierId;
        string invoiceNumber;
        string invoiceDate;
        string currency;
        string amount;
        string status;

        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                SELECT SupplierId, InvoiceNumber, InvoiceDate,
                       Currency, GrandTotal, Status
                FROM MCPurchaseInvoices
                WHERE Id=@id
                """;

            cmd.Parameters.AddWithValue("@id", invoiceId);

            await using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return NotFound("Fatura bulunamadı.");

            supplierId = reader.GetInt64(0);
            invoiceNumber = reader.GetString(1);
            invoiceDate = reader.GetString(2);
            currency = reader.GetString(3);
            amount = Convert.ToString(reader.GetValue(4)) ?? "0";
            status = reader.GetString(5);
        }

        html.Append("<table>");

        string[] labels =
        {
            "Fatura Numarası",
            "Fatura Tarihi",
            "Tedarikçi ID",
            "Para Birimi",
            "Fatura Toplamı",
            "Fatura Durumu"
        };

        string[] values =
        {
            invoiceNumber,
            invoiceDate,
            supplierId.ToString(CultureInfo.InvariantCulture),
            currency,
            amount,
            status
        };

        for (var i = 0; i < labels.Length; i++)
        {
            html.Append("<tr><th>");
            html.Append(H(labels[i]));
            html.Append("</th><td>");
            html.Append(H(values[i]));
            html.Append("</td></tr>");
        }

        html.Append("</table>");

        if (status != "Finalized")
        {
            html.Append("""
                <div class="warn">
                Fatura henüz kesinleştirilmedi.
                Önce fatura detay ekranından kesinleştiriniz.
                </div>
                """);

            html.Append("<a href='/MCPurchaseInvoiceEntry/Details?id=");
            html.Append(invoiceId);
            html.Append("'>Fatura Detayına Git</a></main></body></html>");

            return View(
    "~/Views/Shared/MCPurchaseEmbeddedPage.cshtml",
    new MasterChefTakip.Models.MCPurchaseEmbeddedPageModel
    {
        Html = html.ToString()
    }
);
        }

        var postingTableExists = false;

        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                SELECT COUNT(*) FROM sqlite_master
                WHERE type='table'
                  AND name='MCPurchaseInvoiceCariPostings'
                """;

            postingTableExists =
                Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
        }

        if (postingTableExists)
        {
            await using var cmd = db.CreateCommand();

            cmd.CommandText = """
                SELECT COUNT(*)
                FROM MCPurchaseInvoiceCariPostings
                WHERE PurchaseInvoiceId=@id
                """;

            cmd.Parameters.AddWithValue("@id", invoiceId);

            if (Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0)
            {
                html.Append("""
                    <div class="ok">
                    Bu fatura daha önce cari hesaba aktarılmıştır.
                    Tekrar aktarım yapılamaz.
                    </div></main></body></html>
                    """);

                return View(
    "~/Views/Shared/MCPurchaseEmbeddedPage.cshtml",
    new MasterChefTakip.Models.MCPurchaseEmbeddedPageModel
    {
        Html = html.ToString()
    }
);
            }
        }

        var requestToken = "MC-PURCHASE-INVOICE-" + invoiceId;

        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                SELECT COUNT(*)
                FROM MCCariMovements
                WHERE RequestToken=@token
                """;

            cmd.Parameters.AddWithValue("@token", requestToken);

            if (Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0)
            {
                html.Append("""
                    <div class="warn">
                    Bu faturaya ait cari hareket zaten mevcut.
                    Mükerrer işlem güvenlik nedeniyle engellendi.
                    </div></main></body></html>
                    """);

                return View(
    "~/Views/Shared/MCPurchaseEmbeddedPage.cshtml",
    new MasterChefTakip.Models.MCPurchaseEmbeddedPageModel
    {
        Html = html.ToString()
    }
);
            }
        }

        if (!await HasSupplierLink(db))
        {
            html.Append("""
                <div class="warn">
                Tedarikçi-cari eşleştirme tablosu bulunamadı.
                Tedarikçi ile cari hesap arasındaki ilişki doğrulanamadığı
                için borç aktarımı kapalıdır.
                </div></main></body></html>
                """);

            return View(
    "~/Views/Shared/MCPurchaseEmbeddedPage.cshtml",
    new MasterChefTakip.Models.MCPurchaseEmbeddedPageModel
    {
        Html = html.ToString()
    }
);
        }

        var accounts = new List<(long Id, string Name, string Currency)>();

        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                SELECT a.Id, a.Currency
                FROM CariAccounts a
                INNER JOIN MCPurchaseSupplierCariLinks l
                    ON l.CariAccountId = a.Id
                WHERE l.SupplierId = @supplier
                  AND a.IsActive = 1
                  AND UPPER(TRIM(a.Currency)) =
                      UPPER(TRIM(@currency))
                  AND UPPER(TRIM(l.Currency)) =
                      UPPER(TRIM(@currency))
                ORDER BY a.Id
                """;

            cmd.Parameters.AddWithValue("@supplier", supplierId);
            cmd.Parameters.AddWithValue("@currency", currency);

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var id = reader.GetInt64(0);
                var accountCurrency = reader.GetString(1);

                accounts.Add((id, "Cari Hesap #" + id, accountCurrency));
            }
        }

        if (accounts.Count == 0)
        {
            html.Append("""
                <div class="warn">
                Bu tedarikçiye ve fatura para birimine uygun
                aktif cari hesap bulunamadı.
                Yanlış hesaba borç yazılmaması için aktarım durduruldu.
                </div></main></body></html>
                """);

            return View(
    "~/Views/Shared/MCPurchaseEmbeddedPage.cshtml",
    new MasterChefTakip.Models.MCPurchaseEmbeddedPageModel
    {
        Html = html.ToString()
    }
);
        }

        var token = _antiforgery
            .GetAndStoreTokens(HttpContext)
            .RequestToken ?? "";

        html.Append("""
            <div class="ok">
            Tedarikçi ve para birimi eşleşen cari hesaplar bulundu.
            Aktarım yapmadan önce doğru hesabı seçiniz.
            </div>
            <form method="post"
                  action="/MCPurchaseInvoiceCariPosting/Post"
                  onsubmit="return confirm('Bu faturanın toplam tutarı seçilen cari hesaba borç olarak aktarılacak. Onaylıyor musunuz?');">
            """);

        html.Append("<input type='hidden' name='__RequestVerificationToken' value='");
        html.Append(H(token));
        html.Append("'>");

        html.Append("<input type='hidden' name='invoiceId' value='");
        html.Append(invoiceId);
        html.Append("'>");

        html.Append("""
            <h3>Tedarikçi Cari Hesabı</h3>
            <select name="cariAccountId" required>
            <option value="">Cari hesap seçiniz</option>
            """);

        foreach (var account in accounts)
        {
            html.Append("<option value='");
            html.Append(account.Id);
            html.Append("'>");
            html.Append(H(account.Name));
            html.Append(" - ");
            html.Append(H(account.Currency));
            html.Append("</option>");
        }

        html.Append("""
            </select>
            <p class="warn">
            Seçilen hesaba fatura toplamı borç olarak kaydedilir.
            Fatura ikinci kez aktarılamaz.
            </p>
            <button type="submit">Fatura Borcunu Cari Hesaba Aktar</button>
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
    [HttpPost("Post")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Post(
        [FromForm] long invoiceId,
        [FromForm] long cariAccountId)
    {
        if (invoiceId <= 0 || cariAccountId <= 0)
            return BadRequest("Geçersiz fatura veya cari hesap.");

        await using var db = await OpenDb();

        if (!await HasSupplierLink(db))
            return Conflict(
                "Tedarikçi-cari bağlantısı doğrulanamadı. Aktarım yapılmadı.");

        await using var tx = (SqliteTransaction)
            await db.BeginTransactionAsync();

        await using (var create = db.CreateCommand())
        {
            create.Transaction = tx;
            create.CommandText = """
                CREATE TABLE IF NOT EXISTS MCPurchaseInvoiceCariPostings
                (
                    PurchaseInvoiceId INTEGER PRIMARY KEY,
                    CariAccountId INTEGER NOT NULL,
                    CariMovementId INTEGER NOT NULL UNIQUE,
                    RequestToken TEXT NOT NULL UNIQUE,
                    PostedAtUtc TEXT NOT NULL,
                    PostedBy TEXT NOT NULL
                )
                """;
            await create.ExecuteNonQueryAsync();
        }

        string invoiceNumber;
        string invoiceDate;
        string currency;
        decimal amount;
        long supplierId;

        await using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT InvoiceNumber, InvoiceDate, Currency,
                       GrandTotal, SupplierId
                FROM MCPurchaseInvoices
                WHERE Id=@id AND Status='Finalized'
                """;
            cmd.Parameters.AddWithValue("@id", invoiceId);

            await using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return Conflict("Fatura kesinleştirilmemiş veya bulunamadı.");

            invoiceNumber = reader.GetString(0);
            invoiceDate = reader.GetString(1);
            currency = reader.GetString(2);
            amount = decimal.Parse(
                Convert.ToString(reader.GetValue(3)) ?? "0",
                CultureInfo.InvariantCulture);
            supplierId = reader.GetInt64(4);
        }

        if (amount <= 0)
            return Conflict("Sıfır veya negatif tutar aktarılamaz.");

        await using (var check = db.CreateCommand())
        {
            check.Transaction = tx;
            check.CommandText = """
                SELECT COUNT(*)
                FROM CariAccounts a
                INNER JOIN MCPurchaseSupplierCariLinks l
                    ON l.CariAccountId = a.Id
                WHERE a.Id = @cari
                  AND l.SupplierId = @supplier
                  AND a.IsActive = 1
                  AND UPPER(TRIM(a.Currency)) =
                      UPPER(TRIM(@currency))
                  AND UPPER(TRIM(l.Currency)) =
                      UPPER(TRIM(@currency))
                """;
            check.Parameters.AddWithValue("@cari", cariAccountId);
            check.Parameters.AddWithValue("@supplier", supplierId);
            check.Parameters.AddWithValue("@currency", currency);

            if (Convert.ToInt32(await check.ExecuteScalarAsync()) != 1)
                return Conflict(
                    "Cari hesap, tedarikçi veya para birimi uyuşmuyor.");
        }

        string requestToken = "MC-PURCHASE-INVOICE-" + invoiceId;

        await using (var check = db.CreateCommand())
        {
            check.Transaction = tx;
            check.CommandText = """
                SELECT
                  (SELECT COUNT(*) FROM MCPurchaseInvoiceCariPostings
                   WHERE PurchaseInvoiceId=@id)
                  +
                  (SELECT COUNT(*) FROM MCCariMovements
                   WHERE RequestToken=@token)
                """;
            check.Parameters.AddWithValue("@id", invoiceId);
            check.Parameters.AddWithValue("@token", requestToken);

            if (Convert.ToInt64(await check.ExecuteScalarAsync()) != 0)
                return Conflict("Bu fatura için daha önce cari aktarım yapılmış.");
        }

        long movementId;

        await using (var insert = db.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT INTO MCCariMovements
                (
                    CariAccountId, MovementDate, MovementType,
                    Debit, Credit, Currency, DueDate,
                    Description, ReferenceNumber, RequestToken,
                    RelatedBankMovementId, CreatedAtUtc, CreatedBy
                )
                VALUES
                (
                    @cari, @date, 'Purchase',
                    '0', @credit, @currency, NULL,
                    @description, @reference, @token,
                    NULL, @utc, @actor
                );
                SELECT last_insert_rowid();
                """;

            insert.Parameters.AddWithValue("@cari", cariAccountId);
            insert.Parameters.AddWithValue("@date", invoiceDate);
            insert.Parameters.AddWithValue(
                "@credit", amount.ToString(CultureInfo.InvariantCulture));
            insert.Parameters.AddWithValue("@currency", currency);
            insert.Parameters.AddWithValue(
                "@description", "Satın alma faturası: " + invoiceNumber);
            insert.Parameters.AddWithValue("@reference", invoiceNumber);
            insert.Parameters.AddWithValue("@token", requestToken);
            insert.Parameters.AddWithValue(
                "@utc", DateTime.UtcNow.ToString("O"));
            insert.Parameters.AddWithValue(
                "@actor", User.Identity?.Name ?? "AuthenticatedUser");

            movementId = Convert.ToInt64(
                await insert.ExecuteScalarAsync());
        }

        await using (var link = db.CreateCommand())
        {
            link.Transaction = tx;
            link.CommandText = """
                INSERT INTO MCPurchaseInvoiceCariPostings
                (
                    PurchaseInvoiceId, CariAccountId,
                    CariMovementId, RequestToken,
                    PostedAtUtc, PostedBy
                )
                VALUES
                (
                    @invoice, @cari, @movement, @token, @utc, @actor
                )
                """;
            link.Parameters.AddWithValue("@invoice", invoiceId);
            link.Parameters.AddWithValue("@cari", cariAccountId);
            link.Parameters.AddWithValue("@movement", movementId);
            link.Parameters.AddWithValue("@token", requestToken);
            link.Parameters.AddWithValue(
                "@utc", DateTime.UtcNow.ToString("O"));
            link.Parameters.AddWithValue(
                "@actor", User.Identity?.Name ?? "AuthenticatedUser");
            await link.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();

        return RedirectToAction(nameof(Index));
    }
}