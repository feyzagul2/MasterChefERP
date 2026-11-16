using System;
using System.Collections.Generic;
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
[Route("MCPurchaseInvoiceReceiptAudit")]
public class MCPurchaseInvoiceReceiptAuditController : Controller
{
    private readonly IWebHostEnvironment _env;

    public MCPurchaseInvoiceReceiptAuditController(IWebHostEnvironment env)
    {
        _env = env;
    }

    private static string H(object? value) =>
        WebUtility.HtmlEncode(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");

    // MC_SAFE_QUANTITY_PARSER_V2
    private static bool TryQuantity(object value, out decimal quantity)
    {
        quantity = 0m;

        var text = Convert.ToString(
            value,
            CultureInfo.InvariantCulture
        )?.Trim() ?? "";

        text = text.Replace(" ", "").Replace("\u00A0", "");

        if (text.Length == 0)
            return false;

        if (System.Text.RegularExpressions.Regex.IsMatch(
            text, @"^-?\d{1,3}(\.\d{3})+,\d{1,2}$"))
        {
            text = text.Replace(".", "").Replace(",", ".");
        }
        else if (System.Text.RegularExpressions.Regex.IsMatch(
            text, @"^-?\d{1,3}(,\d{3})+\.\d{1,2}$"))
        {
            text = text.Replace(",", "");
        }
        else if (System.Text.RegularExpressions.Regex.IsMatch(
            text, @"^-?\d+[,.]\d{1,2}$"))
        {
            text = text.Replace(",", ".");
        }
        else if (System.Text.RegularExpressions.Regex.IsMatch(
            text, @"^-?\d{1,3}[,.]\d{3}$"))
        {
            return false;
        }
        else if (!System.Text.RegularExpressions.Regex.IsMatch(
            text, @"^-?\d+$"))
        {
            return false;
        }

        return decimal.TryParse(
            text,
            NumberStyles.AllowLeadingSign |
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out quantity);
    }

    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index([FromQuery] bool onlyProblems = false)
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
                l.Id,
                l.PurchaseInvoiceId,
                COALESCE(l.ProductDescription, ''),
                COALESCE(l.Unit, ''),
                COALESCE(l.Quantity, ''),
                l.PurchaseGoodsReceiptLineId,
                COALESCE(r.ProductDescription, ''),
                COALESCE(r.Unit, ''),
                COALESCE(r.AcceptedQuantity, ''),
                (
                    SELECT COUNT(*)
                    FROM MCPurchaseInvoiceLines x
                    WHERE x.PurchaseGoodsReceiptLineId = l.PurchaseGoodsReceiptLineId
                      AND l.PurchaseGoodsReceiptLineId IS NOT NULL
                ) AS LinkedInvoiceLineCount
                            ,
                l.RawMaterialId AS InvoiceRawMaterialId,
                (
                    SELECT COUNT(*)
                    FROM RawMaterialReceiptTransfers t
                    WHERE t.PurchaseGoodsReceiptLineId = l.PurchaseGoodsReceiptLineId
                      AND (
                          l.RawMaterialId IS NULL
                          OR t.RawMaterialId IS NULL
                          OR t.RawMaterialId <> l.RawMaterialId
                      )
                ) AS MismatchedTransferCount,
                (
                    SELECT COUNT(*)
                    FROM RawMaterialReceiptTransfers t
                    WHERE t.PurchaseGoodsReceiptLineId = l.PurchaseGoodsReceiptLineId
                ) AS TotalTransferCount
            FROM MCPurchaseInvoiceLines l
            LEFT JOIN PurchaseGoodsReceiptLines r
                ON r.Id = l.PurchaseGoodsReceiptLineId
            ORDER BY l.Id DESC
            LIMIT 1000
            """;

        var html = new StringBuilder();

        html.Append("<section style='padding:20px'>");
                // MC_INVOICE_RECEIPT_PROBLEM_FILTER_V1
        html.Append("<h2>Satın Alma Faturası - Mal Kabul Kontrolü</h2>");
        html.Append("<p style='display:flex;gap:15px;flex-wrap:wrap'>");
        html.Append("<a href='/MCPurchaseInvoiceReceiptAudit'>Tüm Kayıtlar</a>");
        html.Append("<a href='/MCPurchaseInvoiceReceiptAudit?onlyProblems=true'>Yalnızca Sorunlular</a>");
        html.Append("<a href='/MCProcurementAudit'>Kontrol Merkezine Dön</a>");
        html.Append("</p>");
        html.Append("<p>Fatura miktarı ile kabul edilen miktar karşılaştırılır.</p>");
        html.Append("<div style='overflow-x:auto'>");
        html.Append("<table style='width:100%;border-collapse:collapse;background:white'>");
        html.Append("<thead><tr style='background:#24496c;color:white'>");

        foreach (var title in new[]
        {
            "Fatura ID", "Fatura Satırı", "Ürün",
            "Fatura Miktarı", "Kabul Edilen",
            "Fark", "Birim", "Durum"
        })
        {
            html.Append("<th style='padding:12px;text-align:left'>");
            html.Append(H(title));
            html.Append("</th>");
        }

        html.Append("</tr></thead><tbody>");

        await using var reader = await cmd.ExecuteReaderAsync();

        var shown = 0;
        var total = 0;
        var matched = 0;
        var problems = 0;

        while (await reader.ReadAsync())
        {
            total++;

            var lineId = reader.GetInt64(0);
            var invoiceId = Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture) ?? "";
            var description = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var invoiceUnit = reader.IsDBNull(3) ? "" : reader.GetString(3);
            var invoiceText = reader.IsDBNull(4) ? "" : reader.GetString(4);
            var receiptLineId = reader.IsDBNull(5) ? "" :
                Convert.ToString(reader.GetValue(5), CultureInfo.InvariantCulture) ?? "";
            var receiptUnit = reader.IsDBNull(7) ? "" : reader.GetString(7);
            var receivedText = reader.IsDBNull(8) ? "" : reader.GetString(8);

            var invoiceValid = TryQuantity(invoiceText, out var invoiceQty);
            var receivedValid = TryQuantity(receivedText, out var receivedQty);

            var linked = !string.IsNullOrWhiteSpace(receiptLineId) &&
                         !reader.IsDBNull(6);

            var unitMatches = string.Equals(
                invoiceUnit.Trim(),
                receiptUnit.Trim(),
                StringComparison.OrdinalIgnoreCase);

            var difference = invoiceQty - receivedQty;

                        // MC_INVOICE_RECEIPT_DUPLICATE_LINK_V1
            var linkedLineCount = Convert.ToInt64(reader.GetValue(9));

                        // MC_NONPOSITIVE_QUANTITY_AUDIT_V1
                                    // MC_ALL_TRANSFER_PRODUCT_CHECK_V2
            var mismatchedTransferCount = Convert.ToInt64(reader.GetValue(11));
            var totalTransferCount = Convert.ToInt64(reader.GetValue(12));

            var productMismatch = mismatchedTransferCount > 0;
            var productNotVerified = linked && totalTransferCount == 0;
            var status = productNotVerified
                ? "URUN KIMLIGI DOGRULANAMADI"
                : productMismatch
                ? "URUN KIMLIGI FARKI"
                : linked && invoiceValid && receivedValid &&
                         (invoiceQty <= 0 || receivedQty < 0)
                ? "GECERSIZ MIKTAR"
                : linkedLineCount > 1
                ? "COKLU FATURA BAGLANTISI"
                : !linked
                ? "MAL KABUL BAGLANTISI YOK"
                : !invoiceValid || !receivedValid
                    ? "MIKTAR OKUNAMADI"
                    : !unitMatches
                        ? "BIRIM FARKI"
                        : difference > 0
                            ? "FATURA MIKTARI FAZLA"
                            : difference < 0
                                ? "KABUL MIKTARI FAZLA"
                                : "ESLESEN";

            if (status == "ESLESEN")
                matched++;
            else
                problems++;

            if (onlyProblems && status == "ESLESEN")
                continue;

            shown++;

            var background = status == "ESLESEN" ? "#f0fdf4" : "#fff1f2";

            html.Append("<tr style='background:");
            html.Append(background);
            html.Append(";border-bottom:1px solid #e2e8f0'>");

            var values = new[]
            {
                invoiceId,
                lineId.ToString(CultureInfo.InvariantCulture),
                description,
                invoiceText,
                linked ? receivedText : "—",
                linked && invoiceValid && receivedValid && unitMatches
                    ? difference.ToString("0.###", CultureInfo.InvariantCulture)
                    : "—",
                invoiceUnit,
                status
            };

            foreach (var value in values)
            {
                html.Append("<td style='padding:11px'>");
                html.Append(H(value));
                html.Append("</td>");
            }

            html.Append("</tr>");
        }

        if (shown == 0)
        {
            html.Append(
                "<tr><td colspan='8' style='padding:20px'>" +
                "Seçilen filtrede gösterilecek fatura satırı bulunmuyor.</td></tr>");
        }

        html.Append("</tbody></table></div>");

        html.Append("<div style='margin-top:20px;padding:15px;background:#f1f5f9'>");
        html.Append("<strong>Kontrol Edilen Satır: ");
        html.Append(total);
        html.Append("</strong><br/>Eşleşen: ");
        html.Append(matched);
        html.Append("<br/>Görüntülenen: ");
        html.Append(shown);
        html.Append("<br/>Kontrol Gerektiren: ");
        html.Append(problems);
        html.Append("</div>");

        html.Append("<p style='margin-top:12px'>");
        html.Append("Bilgi: Bu ekran yalnızca fatura satırlarına açıkça bağlanmış ");
        html.Append("mal kabul satırlarını karşılaştırır. Birim dönüşümü yapmaz. ");
        html.Append("Veritabanını değiştirmez.");
        html.Append("</p></section>");

        return View(
            "~/Views/MCPurchaseCariAudit/Index.cshtml",
            html.ToString()
        );
    }
}