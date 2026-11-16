using MasterChefTakip.Models;
using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

namespace MasterChefTakip.Controllers;

[Authorize(Policy = AppPermissions.PurchaseManage)]
[Route("MCPurchaseInvoiceCariCheck")]
public sealed class MCPurchaseInvoiceCariCheckController : Controller
{
    private readonly IConfiguration _configuration;

    public MCPurchaseInvoiceCariCheckController(
        IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private static string H(object? value) =>
        WebUtility.HtmlEncode(
            Convert.ToString(value) ?? "");

    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index()
    {
        var cs = _configuration
            .GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(cs))
            return StatusCode(500, "Veritabani baglantisi bulunamadi.");

        await using var db = new SqliteConnection(cs);
        await db.OpenAsync();

        var html = new StringBuilder();

        html.Append("""
        <!doctype html>
        <html lang="tr">
        <head>
        <meta charset="utf-8">
        <meta name="viewport"
              content="width=device-width,initial-scale=1">
        <title>MASTER CHEF - Tedarikçi Faturası Cari Kontrolü</title>
        <style>
        body{
            font-family:Arial,sans-serif;
            background:#f1f5f9;
            color:#172033;
            padding:25px
        }
        main{
            max-width:1400px;
            margin:auto;
            background:#fff;
            padding:25px;
            border-radius:12px
        }
        table{
            width:100%;
            border-collapse:collapse;
            margin-top:20px
        }
        th,td{
            padding:12px;
            border-bottom:1px solid #ddd;
            text-align:left
        }
        th{background:#edf2f7}
        .ok{color:#166534;font-weight:bold}
        .warn{color:#b45309;font-weight:bold}
        .error{color:#b91c1c;font-weight:bold}
        .notice{
            background:#fef3c7;
            padding:15px;
            border-radius:8px
        }
        a{color:#18365f}.intro{background:#f8fafc;border:1px solid #dbe4ee;border-radius:10px;padding:14px 16px;margin:10px 0 16px;line-height:1.55}.flow{display:flex;gap:8px;flex-wrap:wrap;margin:14px 0 18px}.flow a{display:inline-flex;padding:9px 12px;border:1px solid #cbd5e1;border-radius:8px;background:#fff;color:#18365f;text-decoration:none;font-weight:700}.flow a.active{background:#18365f;color:#fff;border-color:#18365f}.link-status{margin:14px 0;padding:12px 14px;border-radius:9px;background:#f8fafc;border:1px solid #dbe4ee}.empty{text-align:center!important;padding:28px!important;color:#64748b;font-weight:700}.rules{background:#f8fafc;border:1px solid #dbe4ee;padding:15px;border-radius:9px;line-height:1.65;margin-top:18px}
        </style>
        </head>
        <body>
        <main>
        <a href="/MCPurchaseInvoiceEntry">
        ← Satın Alma Faturaları
        </a>
        <h1>Tedarikçi Faturası Cari Kontrolü</h1><div class="intro"><strong>Bu ekran ne işe yarar?</strong><br>Kesinleşmiş satın alma faturalarının tedarikçi cari hesabına doğru aktarılıp aktarılmadığını kontrol eder. Bu ekran kayıt oluşturmaz; yalnızca kontrol ve yönlendirme içindir.</div><div class="flow"><a href="/MCPurchaseInvoiceCariPosting">← Cari Aktarım</a><a class="active" href="/MCPurchaseInvoiceCariCheck">Cari Kontrol</a><a href="/MCPurchaseCariAudit">Sonraki Adım: Mutabakat →</a><a href="/MCPurchaseSupplierCariLinks">Tedarikçi-Cari Bağlantıları</a></div>
        <p class="notice"><strong>Ne yapmalısınız?</strong><br>Önce faturanın kesinleşmiş ve cari hesaba aktarılmış olduğunu kontrol edin. Eşleştirme eksikse <strong>Tedarikçi-Cari Bağlantıları</strong> ekranından tedarikçi ile cari hesabı eşleştirin.</p>
        """);

        var cariColumns = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        await using (var schema = db.CreateCommand())
        {
            schema.CommandText = "PRAGMA table_info(CariAccounts)";

            await using var reader =
                await schema.ExecuteReaderAsync();

            while (await reader.ReadAsync())
                cariColumns.Add(reader.GetString(1));
        }

        var hasSupplierLink =
            cariColumns.Contains("SupplierId");
        html.Append("<div class='link-status'><strong>Tedarikçi-Cari Eşleştirmesi:</strong> ");
        if (hasSupplierLink)
            html.Append("<span class='ok'>Eşleştirme alanı hazır.</span>");
        else
            html.Append("<span class='warn'>Bazı tedarikçilerin cari hesap eşleştirmesi eksik olabilir. Tedarikçi-Cari Bağlantıları ekranından kontrol edin.</span>");
        html.Append("</div>");

        html.Append("""
        <table>
        <thead>
        <tr>
        <th>Fatura</th>
        <th>Tarih</th>
        <th>Tedarikçi No</th>
        <th>Para Birimi</th>
        <th>Genel Toplam</th>
        <th>Durum</th>
        <th>Cari Kontrol</th>
        <th>Detay</th>
        </tr>
        </thead>
        <tbody>
        """);

        var rowCount = 0;

        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                SELECT
                    Id,
                    InvoiceNumber,
                    InvoiceDate,
                    SupplierId,
                    Currency,
                    GrandTotal,
                    Status
                FROM MCPurchaseInvoices
                ORDER BY Id DESC
                LIMIT 250
                """;

            await using var reader =
                await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                rowCount++;
                var invoiceId = reader.GetInt64(0);
                var invoiceNumber = reader.GetString(1);
                var date = reader.GetString(2);
                var supplierId = reader.GetInt64(3);
                var currency = reader.GetString(4);
                var total = reader.GetString(5);
                var status = reader.GetString(6);

                html.Append("<tr><td>");
                html.Append(H(invoiceNumber));
                html.Append("</td><td>");
                html.Append(H(date));
                html.Append("</td><td>");
                html.Append(supplierId);
                html.Append("</td><td>");
                html.Append(H(currency));
                html.Append("</td><td>");
                html.Append(H(total));
                html.Append("</td><td>");
                html.Append(H(status));
                html.Append("</td><td>");

                if (status != "Finalized")
                {
                    html.Append(
                        "<span class='warn'>Kesinleştirilmemiş</span>");
                }
                else
                {
                    html.Append(
                        "<span class='warn'>" +
                        "Cari borç aktarımı henüz doğrulanmadı" +
                        "</span>");
                }

                html.Append("</td><td><a href='");
                html.Append("/MCPurchaseInvoiceEntry/Details?id=");
                html.Append(invoiceId);
                html.Append("'>Faturayı Aç</a></td></tr>");
            }
        }

        if (rowCount == 0)
            html.Append("<tr><td colspan='8' class='empty'>Kontrol edilecek fatura bulunmuyor. Önce Fatura Girişi ve Fatura Kontrolü adımlarını tamamlayın.</td></tr>");

        html.Append("""
        </tbody>
        </table>
        <div class="rules"><strong>Kontrol sırasında neye bakılır?</strong><br>1. Fatura kesinleşmiş mi?<br>2. Tedarikçi doğru cari hesaba bağlı mı?<br>3. Fatura ve cari hesap para birimi uyumlu mu?<br>4. Aynı fatura daha önce cari borç olarak aktarılmış mı?<br>5. Fatura ile cari borç kaydı doğru şekilde birbirine bağlı mı?<br><br><strong>Sorun varsa:</strong> önce Cari Aktarım veya Tedarikçi-Cari Bağlantıları ekranındaki eksikliği düzeltin; ardından bu ekrana dönüp tekrar kontrol edin.</div>
        </main>
        </body>
        </html>
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