using MasterChefTakip.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using System.Text;
using System.Net;

namespace MasterChefTakip.Controllers;

[Authorize(Policy = AppPermissions.PurchaseManage)]
[Route("MCPurchaseInvoiceStatus")]
public class MCPurchaseInvoiceStatusController : Controller
{
    private readonly IConfiguration _config;

    public MCPurchaseInvoiceStatusController(IConfiguration config)
    {
        _config = config;
    }

    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index()
    {
        var connectionString =
            _config.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
            return Content("Veritabanı bağlantısı bulunamadı.");

        await using var db = new SqliteConnection(connectionString);
        await db.OpenAsync();

        var html = new StringBuilder();

        html.Append("""
        <!doctype html>
        <html lang="tr">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width,initial-scale=1">
        <title>Satın Alma Fatura Durumları</title>
        <style>
        body{font-family:Arial;background:#f4f7fa;color:#203247;margin:0;padding:28px}
        .container{max-width:1250px;margin:auto}
        h1{font-size:24px;margin-bottom:8px}
        .sub{color:#69798b;margin-bottom:24px}
        .cards{display:grid;grid-template-columns:repeat(3,1fr);gap:14px;margin-bottom:22px}
        .card{background:white;border:1px solid #dce5ed;border-radius:12px;padding:20px}
        .value{font-size:30px;font-weight:800;color:#173e60}
        .label{font-size:13px;color:#617184;margin-top:8px}
        table{width:100%;border-collapse:collapse;background:white}
        th,td{padding:13px;text-align:left;border-bottom:1px solid #e5eaf0}
        th{background:#edf3f8;font-size:13px}
        .box{background:white;border:1px solid #dce5ed;border-radius:12px;overflow:hidden}
        .badge{padding:5px 10px;border-radius:8px;font-size:12px;font-weight:700;background:#edf3f8}
        a{color:#174968;text-decoration:none}
        @media(max-width:700px){.cards{grid-template-columns:1fr}}
        </style>
        </head>
        <body>
        <div class="container">
        <h1>Satın Alma Fatura Durumları</h1>
        <div class="sub">Fatura kesinleşme ve cari aktarım takibi</div>
        <a href="/MCPurchaseInvoiceStatus/Excel" style="display:inline-block;background:#176447;color:white;padding:10px 16px;border-radius:8px;text-decoration:none;margin:0 0 16px;font-weight:600">Excel / CSV Aktar ↓</a>
        """);

        long draft = 0, finalized = 0, posted = 0;

        await using (var command = db.CreateCommand())
        {
            command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM MCPurchaseInvoices WHERE Status='Draft'),
                (SELECT COUNT(*) FROM MCPurchaseInvoices WHERE Status='Finalized'),
                (SELECT COUNT(*) FROM MCPurchaseInvoiceCariPostings)
            """;

            await using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                draft = reader.GetInt64(0);
                finalized = reader.GetInt64(1);
                posted = reader.GetInt64(2);
            }
        }

        // MC_INVOICE_TOTALS_START
        html.Append("""
        <div style="background:white;border:1px solid #dce5ed;
        border-radius:12px;padding:18px;margin-bottom:18px">
        <h3 style="margin-top:0">Para Birimine Göre Fatura Toplamları</h3>
        <div style="display:flex;gap:14px;flex-wrap:wrap">
        """);

        await using (var totalsCommand = db.CreateCommand())
        {
            totalsCommand.CommandText = """
                SELECT Currency, COALESCE(SUM(GrandTotal),0)
                FROM MCPurchaseInvoices
                WHERE Status = 'Finalized'
                GROUP BY Currency
                ORDER BY Currency
                """;

            await using var totalsReader =
                await totalsCommand.ExecuteReaderAsync();

            while (await totalsReader.ReadAsync())
            {
                var currency = WebUtility.HtmlEncode(
                    Convert.ToString(totalsReader.GetValue(0)) ?? "");

                var amount = Convert.ToDecimal(
                    totalsReader.GetValue(1));

                html.Append($"""
                <div style="padding:14px 18px;background:#edf4f9;
                border-radius:9px;min-width:170px">
                <div style="font-size:13px;color:#617184">{currency}</div>
                <strong style="font-size:22px;color:#173e60">
                {amount:N2}
                </strong>
                </div>
                """);
            }
        }

        html.Append("</div></div>");
// MC_INVOICE_TOTALS_END
html.Append($"""
        <div class="cards">
        <div class="card"><div class="value">{draft}</div><div class="label">Taslak Faturalar</div></div>
        <div class="card"><div class="value">{finalized}</div><div class="label">Kesinleşmiş Faturalar</div></div>
        <div class="card"><div class="value">{posted}</div><div class="label">Cariye Aktarılanlar</div></div>
        </div>
        <div class="box">
        <table>
        <thead>
        <tr>
        <th>Fatura No</th>
        <th>Tarih</th>
        <th>Para Birimi</th><th>Fatura Tutarı</th>
        <th>Durum</th>
        <th>Cari Aktarım</th>
        </tr>
        </thead>
        <tbody>
        """);

        await using (var command = db.CreateCommand())
        {
            command.CommandText = """
            SELECT i.InvoiceNumber,
                   i.InvoiceDate,
                   i.Currency,
                   i.Status,
                   CASE WHEN p.PurchaseInvoiceId IS NULL
                        THEN 'Aktarılmadı'
                        ELSE 'Aktarıldı' END
            FROM MCPurchaseInvoices i
            LEFT JOIN MCPurchaseInvoiceCariPostings p
              ON p.PurchaseInvoiceId=i.Id
            ORDER BY i.Id DESC
            LIMIT 200
            """;

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                string Value(int index) =>
                    reader.IsDBNull(index)
                        ? ""
                        : WebUtility.HtmlEncode(
                            Convert.ToString(reader.GetValue(index)) ?? "");

                html.Append("<tr>");

                for (var i = 0; i < 6; i++)
                    html.Append("<td>" + Value(i) + "</td>");

                html.Append("</tr>");
            }
        }

        html.Append("""
        </tbody></table></div>
        <script>
        (function(){
            const table = document.querySelector("table");
            if (!table) return;

            const box = document.createElement("div");
            box.style.cssText =
                "display:flex;gap:10px;flex-wrap:wrap;margin:0 0 15px";

            const search = document.createElement("input");
            search.placeholder = "Fatura numarası veya durum ara...";
            search.style.cssText =
                "padding:11px;border:1px solid #dce5ed;border-radius:8px;flex:1;min-width:220px";

            const select = document.createElement("select");
            select.style.cssText =
                "padding:11px;border:1px solid #dce5ed;border-radius:8px";

            [
                ["","Tüm Faturalar"],
                ["Draft","Taslak"],
                ["Finalized","Kesinleşmiş"]
            ].forEach(function(item){
                const option = document.createElement("option");
                option.value = item[0];
                option.textContent = item[1];
                select.appendChild(option);
            });

            box.append(search,select);
            table.parentElement.before(box);

            function filter(){
                const query = search.value.toLocaleLowerCase("tr-TR");
                const status = select.value;

                table.querySelectorAll("tbody tr").forEach(function(row){
                    const cells = row.querySelectorAll("td");
                    const text = row.textContent.toLocaleLowerCase("tr-TR");
                    const invoiceStatus = cells.length > 4
                        ? cells[4].textContent.trim()
                        : "";

                    row.hidden = !text.includes(query) ||
                        (status && invoiceStatus !== status);
                });
            }

            search.addEventListener("input",filter);
            select.addEventListener("change",filter);
        })();
        </script>
        <p style="margin-top:20px">
        <a href="/MCPurchaseInvoiceEntry">← Satın Alma Faturalarına Dön</a>
        </p>
        </div></body></html>
        """);

        var reportHtml = html.ToString();

        var bodyMatch = System.Text.RegularExpressions.Regex.Match(
            reportHtml,
            @"(?is)<body[^>]*>(.*?)</body>"
        );

        if (bodyMatch.Success)
            reportHtml = bodyMatch.Groups[1].Value;

        var styleMatches =
            System.Text.RegularExpressions.Regex.Matches(
                html.ToString(),
                @"(?is)<style[^>]*>.*?</style>"
            );

        var reportStyles = new System.Text.StringBuilder();

        foreach (System.Text.RegularExpressions.Match style in styleMatches)
            reportStyles.AppendLine(style.Value);

        ViewData["Title"] = "Satın Alma Fatura Durumları";
        ViewData["ReportHtml"] = reportHtml;
        ViewData["ReportStyles"] = reportStyles.ToString();

        return View("~/Views/MCPurchaseInvoiceStatus/Index.cshtml");
    }

    // MC_INVOICE_EXCEL_EXPORT_V1
    [HttpGet("Excel")]
    public async Task<IActionResult> Excel()
    {
        var cs = _config.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs))
            return BadRequest("Veritabanı bağlantısı bulunamadı.");

        await using var db = new SqliteConnection(cs);
        await db.OpenAsync();

        var csv = new StringBuilder();
        csv.AppendLine("Fatura No;Tarih;Para Birimi;Tutar;Durum;Cari Aktarım");

        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT i.InvoiceNumber, i.InvoiceDate, i.Currency,
                   i.GrandTotal, i.Status,
                   CASE WHEN p.PurchaseInvoiceId IS NULL
                        THEN 'Aktarılmadı' ELSE 'Aktarıldı' END
            FROM MCPurchaseInvoices i
            LEFT JOIN MCPurchaseInvoiceCariPostings p
              ON p.PurchaseInvoiceId = i.Id
            ORDER BY i.Id DESC
            """;

        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var cells = new string[6];

            for (int i = 0; i < cells.Length; i++)
            {
                var value = reader.IsDBNull(i)
                    ? ""
                    : Convert.ToString(reader.GetValue(i)) ?? "";

                value = value.Replace(";", ",")
                             .Replace("\r", " ")
                             .Replace("\n", " ");

                if (value.Length > 0 &&
                    "=+-@".Contains(value[0]))
                    value = "'" + value;

                cells[i] = value;
            }

            csv.AppendLine(string.Join(";", cells));
        }

        var bytes = new System.Text.UTF8Encoding(true)
            .GetBytes(csv.ToString());

        return File(bytes, "text/csv; charset=utf-8",
            "Satin_Alma_Fatura_Durumlari.csv");
    }
}