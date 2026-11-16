using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using MasterChefTakip.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.VisualBasic.FileIO;

namespace MasterChefTakip.Controllers;

// MC_EXTERNAL_ORDER_INTEGRATION_V1
[Authorize(Roles = AppRoles.Admin)]
[Route("MCExternalOrders")]
public sealed class MCExternalOrderIntegrationController : Controller
{
    private readonly IAntiforgery _antiforgery;

    public MCExternalOrderIntegrationController(
        IAntiforgery antiforgery)
    {
        _antiforgery = antiforgery;
    }

    private static string H(object? value) =>
        WebUtility.HtmlEncode(
            Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");

    private string Token() =>
        "<input type='hidden' name='__RequestVerificationToken' value='" +
        H(_antiforgery.GetAndStoreTokens(HttpContext).RequestToken) +
        "'>";

    private static string Layout(string content) => """
        <!doctype html>
        <html lang='tr'>
        <head>
        <meta charset='utf-8'>
        <meta name='viewport' content='width=device-width,initial-scale=1'>
        <title>MASTER CHEF - Sipariş Entegrasyonu</title>
        <style>
        *{box-sizing:border-box}
        body{font-family:Arial;background:#f1f5f9;color:#172033;
             margin:0;padding:25px}
        main{max-width:1200px;margin:auto}
        section{background:white;padding:24px;margin:20px 0;
                border-radius:12px;border:1px solid #e2e8f0}
        input{padding:12px;margin:10px 0}
        button{background:#183b5a;color:white;border:0;
               border-radius:7px;padding:12px 20px}
        table{width:100%;border-collapse:collapse}
        th,td{padding:10px;border-bottom:1px solid #ddd;text-align:left}
        th{background:#eff6ff}
        .warn{background:#fff7ed;padding:15px;border-radius:8px}
        .good{background:#dcfce7;padding:15px;border-radius:8px}
        </style></head><body><main>
        <h1>MASTER CHEF - Mevcut Sipariş Sistemi Entegrasyonu</h1>
        """ + content + "</main></body></html>";

    [HttpGet("")]
    public IActionResult Index()
    {
        var page = new StringBuilder();

        page.Append("""
        <section>
        <h2>Harici Sipariş Dosyası</h2>
        <p>Mevcut sipariş programınızdan alınan CSV dosyasını
        kontrol etmek için kullanın.</p>
        <p>Bu modül yeni sipariş oluşturmaz.
        Ürün stoklarını ve finans kayıtlarını değiştirmez.</p>
        <form method='post' action='/MCExternalOrders/preview'
              enctype='multipart/form-data'>
        """);

        page.Append(Token());

        page.Append("""
        <input type='file' name='file'
               accept='.csv,text/csv,text/plain' required>
        <button type='submit'>Siparişleri Kontrol Et</button>
        </form>
        </section>
        <section>
        <h2>Beklenen CSV Alanları</h2>
        <table>
        <tr><th>Alan</th><th>Açıklama</th></tr>
        <tr><td>ExternalOrderId</td><td>Mevcut sistemdeki sipariş numarası</td></tr>
        <tr><td>ProductCode</td><td>Ürün kodu</td></tr>
        <tr><td>Quantity</td><td>Sipariş miktarı</td></tr>
        </table>
        <p>İsteğe bağlı: CustomerCode, OrderDate.</p>
        <p>CSV ayıracı noktalı virgül veya virgül olabilir.</p>
        </section>
        """);

        return Content(Layout(page.ToString()),"text/html; charset=utf-8");
    }

    [HttpPost("preview")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(5242880)]
    public async Task<IActionResult> Preview(IFormFile? file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("Sipariş dosyası seçilmedi.");

        if (file.Length > 5242880)
            return BadRequest("Dosya 5 MB sınırını aşıyor.");

        if (!string.Equals(
            Path.GetExtension(file.FileName),".csv",
            StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Yalnızca CSV dosyası kabul edilir.");
        }

        var rows = new List<(string Order,string Product,
                             string Quantity,string Status)>();

        try
        {
            using var stream = file.OpenReadStream();
            using var memory = new MemoryStream();

            await stream.CopyToAsync(memory);

            if (memory.Length > 5242880)
                return BadRequest("Dosya boyutu sınırı aşıldı.");

            memory.Position = 0;

            using var reader = new StreamReader(
                memory,new UTF8Encoding(false),true);

            var text = await reader.ReadToEndAsync();

            using var parserReader = new StringReader(text);
            using var parser = new TextFieldParser(parserReader);

            parser.TextFieldType = FieldType.Delimited;
            parser.HasFieldsEnclosedInQuotes = true;

            var firstLine = text.Split(
                new[] { "\r\n", "\n" },
                StringSplitOptions.None).FirstOrDefault() ?? "";

            var delimiter =
                firstLine.Count(x => x == ';') >
                firstLine.Count(x => x == ',') ? ";" : ",";

            parser.SetDelimiters(delimiter);

            var headers = parser.ReadFields();

            if (headers == null)
                return BadRequest("CSV başlığı bulunamadı.");

            var normalized = headers
                .Select(x => (x ?? "").Trim().TrimStart('\uFEFF'))
                .ToArray();

            int Column(string name) =>
                Array.FindIndex(
                    normalized,
                    x => string.Equals(
                        x,name,StringComparison.OrdinalIgnoreCase));

            var orderColumn = Column("ExternalOrderId");
            var productColumn = Column("ProductCode");
            var quantityColumn = Column("Quantity");

            if (orderColumn < 0 ||
                productColumn < 0 ||
                quantityColumn < 0)
            {
                return BadRequest(
                    "CSV sütunları eksik. Gerekli alanlar: " +
                    "ExternalOrderId, ProductCode, Quantity.");
            }

            var lineNumber = 1;

            while (!parser.EndOfData)
            {
                lineNumber++;

                if (lineNumber > 10001)
                    return BadRequest(
                        "Tek dosyada en fazla 10.000 sipariş satırı olabilir.");

                var values = parser.ReadFields() ??
                    Array.Empty<string>();

                string Value(int index) =>
                    index < values.Length ?
                    (values[index] ?? "").Trim() : "";

                var order = Value(orderColumn);
                var product = Value(productColumn);
                var quantity = Value(quantityColumn);

                var valid =
                    order.Length > 0 &&
                    order.Length <= 100 &&
                    product.Length > 0 &&
                    product.Length <= 100 &&
                    decimal.TryParse(
                        quantity.Replace(",","."),
                        NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture,
                        out var parsedQuantity) &&
                    parsedQuantity > 0 &&
                    parsedQuantity <= 100000000m;

                rows.Add((
                    order,
                    product,
                    quantity,
                    valid ? "UYGUN" : "HATALI"));
            }
        }
        catch (MalformedLineException)
        {
            return BadRequest("CSV dosyasında hatalı satır yapısı var.");
        }
        catch (DecoderFallbackException)
        {
            return BadRequest("CSV dosyasının karakter kodlaması okunamadı.");
        }

        var errors = rows.Count(x => x.Status == "HATALI");
        var orders = rows
            .Where(x => x.Status == "UYGUN")
            .Select(x => x.Order)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var html = new StringBuilder();

        html.Append("<section><h2>Dosya Kontrol Sonucu</h2>");

        html.Append("<p>Toplam satır: <strong>");
        html.Append(rows.Count);
        html.Append("</strong></p>");

        html.Append("<p>Farklı sipariş: <strong>");
        html.Append(orders);
        html.Append("</strong></p>");

        html.Append("<p>Hatalı satır: <strong>");
        html.Append(errors);
        html.Append("</strong></p>");

        html.Append(errors == 0
            ? "<p class='good'>Dosya biçimi kontrolü başarılı.</p>"
            : "<p class='warn'>Hatalı satırlar düzeltilmelidir.</p>");

        html.Append("""
        <p>Bu kontrol yalnızca CSV biçimini ve miktarları doğrular.
        Ürün kodlarının gerçek stok kartlarıyla eşleşmesi
        henüz kontrol edilmez.</p>
        <table>
        <tr><th>Sipariş No</th><th>Ürün Kodu</th>
        <th>Miktar</th><th>Durum</th></tr>
        """);

        foreach (var row in rows.Take(300))
        {
            html.Append("<tr><td>");
            html.Append(H(row.Order));
            html.Append("</td><td>");
            html.Append(H(row.Product));
            html.Append("</td><td>");
            html.Append(H(row.Quantity));
            html.Append("</td><td>");
            html.Append(H(row.Status));
            html.Append("</td></tr>");
        }

        html.Append("</table>");

        if (rows.Count > 300)
            html.Append(
                "<p>İlk 300 satır gösteriliyor; " +
                "bütün satırlar kontrol edildi.</p>");

        html.Append("""
        <p><strong>Bu aşamada siparişler veritabanına
        aktarılmadı.</strong></p>
        <p><a href='/MCExternalOrders'>Başka dosya kontrol et</a></p>
        </section>
        """);

        return Content(Layout(html.ToString()),
            "text/html; charset=utf-8");
    }
}