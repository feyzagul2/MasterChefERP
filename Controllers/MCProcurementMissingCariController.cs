using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

[Authorize(Roles = "Admin,Yonetici")]
[Route("MCProcurementMissingCari")]
public class MCProcurementMissingCariController : Controller
{
    private readonly IWebHostEnvironment _env;

    public MCProcurementMissingCariController(IWebHostEnvironment env)
    {
        _env = env;
    }

    private static string H(object? value)
    {
        return WebUtility.HtmlEncode(
            Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
    }

    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index()
    {
        var db = Path.Combine(_env.ContentRootPath, "app.db");

        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = db,
            Mode = SqliteOpenMode.ReadOnly
        };

        await using var connection = new SqliteConnection(cs.ToString());
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();

        cmd.CommandText = """
            SELECT
                i.Id,
                COALESCE(i.InvoiceNumber, ''),
                i.SupplierId,
                i.GrandTotal,
                COALESCE(i.Currency, '')
            FROM MCPurchaseInvoices i
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM MCPurchaseInvoiceCariPostings p
                WHERE p.PurchaseInvoiceId = i.Id
            )
            ORDER BY i.Id DESC
            LIMIT 500
            """;

        var html = new StringBuilder();

        html.Append("<section style='padding:20px'>");
        html.Append("<h2>Cari Aktarımı Eksik Satın Alma Faturaları</h2>");
        html.Append("<p><a href='/MCProcurementAudit'>");
        html.Append("← Satın Alma Kontrol Merkezine Dön");
        html.Append("</a></p>");

        html.Append("<p style='color:#64748b'>");
        html.Append("Bu listede cari aktarım kaydı bulunmayan faturalar gösterilir.");
        html.Append("</p>");

        html.Append("<div style='overflow-x:auto'>");
        html.Append("<table style='width:100%;border-collapse:collapse;background:white'>");
        html.Append("<thead><tr style='background:#24496c;color:white'>");

        foreach (var title in new[]
        {
            "Fatura ID", "Fatura No", "Tedarikçi ID",
            "Fatura Tutarı", "Para Birimi", "Durum", "İşlem"
        })
        {
            html.Append("<th style='padding:12px;text-align:left'>");
            html.Append(H(title));
            html.Append("</th>");
        }

        html.Append("</tr></thead><tbody>");

        await using var reader = await cmd.ExecuteReaderAsync();

        long count = 0;

        while (await reader.ReadAsync())
        {
            count++;

            var id = Convert.ToInt64(reader.GetValue(0));

            html.Append("<tr style='border-bottom:1px solid #e2e8f0'>");

            for (var col = 0; col < reader.FieldCount; col++)
            {
                html.Append("<td style='padding:11px'>");
                html.Append(H(reader.IsDBNull(col) ? "" : reader.GetValue(col)));
                html.Append("</td>");
            }

            html.Append("<td style='padding:11px;color:#b45309'>");
            html.Append("CARİ AKTARIM BEKLİYOR");
            html.Append("</td>");

            html.Append("<td style='padding:11px'>");
            html.Append("<a href='/MCPurchaseCariAudit/InvoiceDetail/");
            html.Append(id);
            html.Append("'>Detay</a>");
            html.Append("</td>");

            html.Append("</tr>");
        }

        if (count == 0)
        {
            html.Append("<tr><td colspan='7' style='padding:20px'>");
            html.Append("Cari aktarımı eksik satın alma faturası bulunamadı.");
            html.Append("</td></tr>");
        }

        html.Append("</tbody></table></div>");

        html.Append("<p style='margin-top:15px'>");
        html.Append("<strong>Listelenen Fatura: </strong>");
        html.Append(count);
        html.Append("</p>");

        html.Append("<p style='color:#64748b'>");
        html.Append("En fazla son 500 kayıt gösterilir. ");
        html.Append("Bu ekran yalnızca görüntüleme yapar; ");
        html.Append("cari hesap veya stok kayıtlarını değiştirmez.");
        html.Append("</p>");

        html.Append("</section>");

        return View(
            "~/Views/MCPurchaseCariAudit/Index.cshtml",
            html.ToString()
        );
    }
}