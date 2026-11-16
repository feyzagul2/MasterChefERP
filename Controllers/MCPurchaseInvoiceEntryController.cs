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
[Route("MCPurchaseInvoiceEntry")]
public sealed class MCPurchaseInvoiceEntryController : Controller
{
    private readonly IConfiguration _configuration;
    private readonly IAntiforgery _antiforgery;

    public MCPurchaseInvoiceEntryController(
        IConfiguration configuration,
        IAntiforgery antiforgery)
    {
        _configuration = configuration;
        _antiforgery = antiforgery;
    }

    private async Task<SqliteConnection> OpenDb()
    {
        var cs = _configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException("Veritabani baglantisi yok.");

        var db = new SqliteConnection(cs);
        await db.OpenAsync();
        return db;
    }

    private static string H(object? value) =>
        WebUtility.HtmlEncode(Convert.ToString(value) ?? "");

    private static string D(decimal value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static bool ParseDecimal(string? text, out decimal value) =>
        decimal.TryParse(
            text,
            NumberStyles.AllowDecimalPoint |
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out value);

    private static decimal Money(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private sealed record ReceiptLine(
        long Id,
        string Code,
        string Description,
        string Unit,
        decimal AvailableQuantity);

    private async Task<(long SupplierId, string ReceiptNumber,
        List<ReceiptLine> Lines)?> LoadReceipt(
        SqliteConnection db,
        long receiptId,
        SqliteTransaction? transaction = null)
    {
        long supplierId;
        string receiptNumber;

        await using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = """
                SELECT d.SupplierId, r.ReceiptNumber
                FROM PurchaseGoodsReceipts r
                INNER JOIN PurchaseDispatchNotes d
                    ON d.Id = r.PurchaseDispatchNoteId
                WHERE r.Id = @id
                """;

            cmd.Parameters.AddWithValue("@id", receiptId);

            await using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return null;

            supplierId = reader.GetInt64(0);
            receiptNumber = reader.IsDBNull(1)
                ? "" : reader.GetString(1);
        }

        var lines = new List<ReceiptLine>();

        await using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = transaction;

            cmd.CommandText = """
                SELECT
                    l.Id,
                    COALESCE(c.MaterialCode, ''),
                    COALESCE(l.ProductDescription, ''),
                    COALESCE(l.Unit, ''),
                    l.AcceptedQuantity
                FROM PurchaseGoodsReceiptLines l
                LEFT JOIN MCMaterialReceiptLinks ml
                    ON ml.PurchaseGoodsReceiptLineId = l.Id
                LEFT JOIN MCMaterialCatalog c
                    ON c.Id = ml.MaterialCatalogId
                WHERE l.PurchaseGoodsReceiptId = @id
                ORDER BY l.LineNumber, l.Id
                """;

            cmd.Parameters.AddWithValue("@id", receiptId);

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var quantity = Convert.ToDecimal(
                    reader.GetValue(4),
                    CultureInfo.InvariantCulture);

                lines.Add(new ReceiptLine(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    quantity));
            }
        }

        return (supplierId, receiptNumber, lines);
    }

    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index()
    {
        await using var db = await OpenDb();

        var html = new StringBuilder();

        BeginHtml(html, "Satın Alma Faturaları");

        html.Append("""
            <h1>Satın Alma Faturaları</h1>
            <p>Mal kabulü tamamlanan irsaliyelerden fatura oluşturabilirsiniz.</p>
            <h2>Mal Kabul İrsaliyeleri</h2>
            <table>
            <tr>
              <th>İrsaliye</th>
              <th>Tarih</th>
              <th>Tedarikçi ID</th>
              <th>İşlem</th>
            </tr>
            """);

        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                SELECT r.Id,
                       r.ReceiptNumber,
                       r.ReceiptDate,
                       d.SupplierId
                FROM PurchaseGoodsReceipts r
                INNER JOIN PurchaseDispatchNotes d
                  ON d.Id = r.PurchaseDispatchNoteId
                ORDER BY r.Id DESC
                LIMIT 150
                """;

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var id = reader.GetInt64(0);

                html.Append("<tr><td>");
                html.Append(H(reader.GetValue(1)));
                html.Append("</td><td>");
                html.Append(H(reader.GetValue(2)));
                html.Append("</td><td>");
                html.Append(H(reader.GetValue(3)));
                html.Append("</td><td><a href='/MCPurchaseInvoiceEntry/Create?receiptId=");
                html.Append(id);
                html.Append("'>Fatura Oluştur</a></td></tr>");
            }
        }

        html.Append("</table><h2>Kaydedilmiş Faturalar</h2>");

        html.Append("""
            <table>
            <tr>
              <th>Fatura</th>
              <th>Tarih</th>
              <th>Tedarikçi ID</th>
              <th>Genel Toplam</th>
              <th>Durum</th>
            </tr>
            """);

        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                SELECT InvoiceNumber, InvoiceDate,
                       SupplierId, GrandTotal, Status, Id
                FROM MCPurchaseInvoices
                ORDER BY Id DESC
                LIMIT 150
                """;

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                html.Append("<tr>");

                // MC_INVOICE_LIST_DETAILS_LINK_V3
                for (var i = 0; i < 5; i++)
                {
                    html.Append("<td>");

                    if (i == 0)
                    {
                        html.Append("<a href='/MCPurchaseInvoiceEntry/Details?id=");
                        html.Append(reader.GetInt64(5));
                        html.Append("'>");
                        html.Append(H(reader.GetValue(i)));
                        html.Append("</a>");
                    }
                    else
                    {
                        html.Append(H(reader.GetValue(i)));
                    }

                    html.Append("</td>");
                }

                html.Append("</tr>");
            }
        }

        html.Append("</table>");

        EndHtml(html);

        return View(
    "~/Views/Shared/MCPurchaseEmbeddedPage.cshtml",
    new MasterChefTakip.Models.MCPurchaseEmbeddedPageModel
    {
        Html = html.ToString()
    }
);
    }

    [HttpGet("Create")]
    public async Task<IActionResult> Create([FromQuery] long receiptId)
    {
        if (receiptId <= 0)
            return BadRequest("Gecerli irsaliye seciniz.");

        await using var db = await OpenDb();

        var receipt = await LoadReceipt(db, receiptId);

        if (receipt == null)
            return NotFound("Irsaliye bulunamadi.");

        var token =
            _antiforgery.GetAndStoreTokens(HttpContext).RequestToken ?? "";

        var html = new StringBuilder();

        BeginHtml(html, "Satın Alma Faturası Girişi");

        html.Append("<h1>Satın Alma Faturası Girişi</h1>");
        html.Append("<p><strong>İrsaliye:</strong> ");
        html.Append(H(receipt.Value.ReceiptNumber));
        html.Append("</p>");

        html.Append("""
            <p class="notice">
            Fatura taslak kaydedilir. Stok hareketi veya cari borç oluşturulmaz.
            KDV oranlarını gelen faturadan kontrol ederek giriniz.
            </p>
            <form method="post" action="/MCPurchaseInvoiceEntry/Save">
            """);

        html.Append("<input type='hidden' name='__RequestVerificationToken' value='");
        html.Append(H(token));
        html.Append("'>");

        html.Append("<input type='hidden' name='receiptId' value='");
        html.Append(receiptId);
        html.Append("'>");

        html.Append("""
            <div class="fields">
              <label>Fatura Numarası
                <input name="invoiceNumber" maxlength="100" required>
              </label>
              <label>Fatura Tarihi
                <input type="date" name="invoiceDate" required>
              </label>
              <label>Para Birimi
                <select name="currency">
                  <option value="TRY">TRY</option>
                  <option value="USD">USD</option>
                  <option value="EUR">EUR</option>
                </select>
              </label>
            </div>

            <h2>Fatura Kalemleri</h2>

            <table>
            <tr>
              <th>Malzeme</th>
              <th>Birim</th>
              <th>Kabul Miktarı</th>
              <th>Fatura Miktarı</th>
              <th>Birim Fiyat</th>
              <th>İskonto %</th>
              <th>KDV %</th>
            </tr>
            """);

        foreach (var line in receipt.Value.Lines)
        {
            html.Append("<tr><td><strong>");
            html.Append(H(line.Code));
            html.Append("</strong><br>");
            html.Append(H(line.Description));
            html.Append("</td><td>");
            html.Append(H(line.Unit));
            html.Append("</td><td>");
            html.Append(D(line.AvailableQuantity));
            html.Append("</td>");

            Input(html, "qty_" + line.Id, D(line.AvailableQuantity));
            Input(html, "price_" + line.Id, "0");
            Input(html, "discount_" + line.Id, "0");
            Input(html, "vat_" + line.Id, "0");

            html.Append("</tr>");
        }

        html.Append("""
            </table>
            <p class="notice">
            Fatura miktarı kabul miktarını aşamaz.
            Birim fiyat KDV hariç girilir.
            Hesaplamalar kaydetme sırasında sunucuda yapılır.
            </p>
            <label>Fatura Notu
              <textarea name="notes" maxlength="2000" rows="3"></textarea>
            </label>
            <br><br>
            <button type="submit">Faturayı Taslak Kaydet</button>
            </form>
            """);

        EndHtml(html);

        return View(
    "~/Views/Shared/MCPurchaseEmbeddedPage.cshtml",
    new MasterChefTakip.Models.MCPurchaseEmbeddedPageModel
    {
        Html = html.ToString()
    }
);
    }

    private static void Input(
        StringBuilder html,
        string name,
        string value)
    {
        html.Append("<td><input type='number' step='0.0001' min='0' name='");
        html.Append(H(name));
        html.Append("' value='");
        html.Append(H(value));
        html.Append("' required></td>");
    }

    [HttpPost("Save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(
        [FromForm] IFormCollection form)
    {
        if (!long.TryParse(form["receiptId"], out var receiptId)
            || receiptId <= 0)
            return BadRequest("Gecersiz irsaliye.");

        var invoiceNumber = form["invoiceNumber"].ToString().Trim();
        var dateText = form["invoiceDate"].ToString();
        var currency = form["currency"].ToString().Trim().ToUpperInvariant();
        var notes = form["notes"].ToString().Trim();

        if (invoiceNumber.Length == 0 || invoiceNumber.Length > 100)
            return BadRequest("Fatura numarasi gecersiz.");

        if (!DateTime.TryParseExact(
            dateText, "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _))
            return BadRequest("Fatura tarihi gecersiz.");

        if (currency is not ("TRY" or "USD" or "EUR"))
            return BadRequest("Para birimi gecersiz.");

        if (notes.Length > 2000)
            return BadRequest("Fatura notu cok uzun.");

        await using var db = await OpenDb();

        await using var transaction = (SqliteTransaction)
            await db.BeginTransactionAsync();

        var receipt = await LoadReceipt(db, receiptId, transaction);

        if (receipt == null || receipt.Value.Lines.Count == 0)
            return BadRequest("Faturalandirabilecek irsaliye satiri yok.");

        var prepared = new List<(
            ReceiptLine Line,
            decimal Quantity,
            decimal Price,
            decimal Discount,
            decimal Vat,
            decimal DiscountAmount,
            decimal Net,
            decimal VatAmount,
            decimal Gross)>();

        decimal subtotal = 0m;
        decimal discountTotal = 0m;
        decimal vatTotal = 0m;
        decimal grandTotal = 0m;

        foreach (var line in receipt.Value.Lines)
        {
            if (!ParseDecimal(form["qty_" + line.Id], out var quantity)
                || !ParseDecimal(form["price_" + line.Id], out var price)
                || !ParseDecimal(form["discount_" + line.Id], out var discount)
                || !ParseDecimal(form["vat_" + line.Id], out var vat))
                return BadRequest("Fatura kalemlerinde gecersiz sayi var.");

            if (quantity < 0 || quantity > line.AvailableQuantity)
                return BadRequest("Fatura miktari kabul miktarini asamaz.");

            if (price < 0 || price > 1000000000m)
                return BadRequest("Birim fiyat gecersiz.");

            if (discount < 0 || discount > 100
                || vat < 0 || vat > 100)
                return BadRequest("Iskonto veya KDV orani gecersiz.");

            if (quantity == 0)
                continue;

            var grossBeforeDiscount = Money(quantity * price);
            var discountAmount = Money(grossBeforeDiscount * discount / 100m);
            var net = grossBeforeDiscount - discountAmount;
            var vatAmount = Money(net * vat / 100m);
            var gross = net + vatAmount;

            prepared.Add((
                line, quantity, price, discount, vat,
                discountAmount, net, vatAmount, gross));

            subtotal += grossBeforeDiscount;
            discountTotal += discountAmount;
            vatTotal += vatAmount;
            grandTotal += gross;
        }

        if (prepared.Count == 0)
            return BadRequest("En az bir kalemde pozitif miktar giriniz.");

        // Prevent invoicing the same accepted quantity twice.
        foreach (var item in prepared)
        {
            await using var check = db.CreateCommand();
            check.Transaction = transaction;

            check.CommandText = """
                SELECT COALESCE(SUM(
                    CAST(l.Quantity AS REAL)), 0)
                FROM MCPurchaseInvoiceLines l
                INNER JOIN MCPurchaseInvoices h
                    ON h.Id = l.PurchaseInvoiceId
                WHERE l.PurchaseGoodsReceiptLineId = @lineId
                  AND h.Status <> 'Cancelled'
                """;

            check.Parameters.AddWithValue("@lineId", item.Line.Id);

            var already = Convert.ToDecimal(
                await check.ExecuteScalarAsync() ?? 0,
                CultureInfo.InvariantCulture);

            if (already + item.Quantity > item.Line.AvailableQuantity)
                return Conflict(
                    "Irsaliye satiri daha once faturalanmis. " +
                    "Kabul miktari asiliyor: " + item.Line.Description);
        }

        long invoiceId;

        await using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = transaction;

            cmd.CommandText = """
                INSERT INTO MCPurchaseInvoices
                (
                    SupplierId, PurchaseGoodsReceiptId,
                    InvoiceNumber, InvoiceDate,
                    Currency, Notes, Status,
                    Subtotal, DiscountTotal,
                    VatTotal, GrandTotal,
                    CreatedByUserId, CreatedAtUtc
                )
                VALUES
                (
                    @supplier, @receipt,
                    @number, @date,
                    @currency, @notes, 'Draft',
                    @subtotal, @discount,
                    @vat, @grand,
                    @user, @now
                );

                SELECT last_insert_rowid();
                """;

            cmd.Parameters.AddWithValue("@supplier", receipt.Value.SupplierId);
            cmd.Parameters.AddWithValue("@receipt", receiptId);
            cmd.Parameters.AddWithValue("@number", invoiceNumber);
            cmd.Parameters.AddWithValue("@date", dateText);
            cmd.Parameters.AddWithValue("@currency", currency);
            cmd.Parameters.AddWithValue("@notes", notes);
            cmd.Parameters.AddWithValue("@subtotal", D(subtotal));
            cmd.Parameters.AddWithValue("@discount", D(discountTotal));
            cmd.Parameters.AddWithValue("@vat", D(vatTotal));
            cmd.Parameters.AddWithValue("@grand", D(grandTotal));
            cmd.Parameters.AddWithValue(
                "@user", User.Identity?.Name ?? "");
            cmd.Parameters.AddWithValue(
                "@now", DateTime.UtcNow.ToString("O"));

            try
            {
                invoiceId = Convert.ToInt64(
                    await cmd.ExecuteScalarAsync());
            }
            catch (SqliteException ex)
                when (ex.SqliteErrorCode == 19)
            {
                return Conflict(
                    "Bu tedarikci ve fatura numarasi zaten kayitli.");
            }
        }

        foreach (var item in prepared)
        {
            await using var cmd = db.CreateCommand();
            cmd.Transaction = transaction;

            cmd.CommandText = """
                INSERT INTO MCPurchaseInvoiceLines
                (
                    PurchaseInvoiceId,
                    PurchaseGoodsReceiptLineId,
                    ProductCode,
                    ProductDescription,
                    Unit, Quantity, UnitPrice,
                    VatRate, DiscountRate, DiscountAmount,
                    NetAmount, VatAmount, GrossAmount
                )
                VALUES
                (
                    @invoice, @line, @code, @description,
                    @unit, @quantity, @price,
                    @vat, @discountRate, @discountAmount,
                    @net, @vatAmount, @gross
                )
                """;

            cmd.Parameters.AddWithValue("@invoice", invoiceId);
            cmd.Parameters.AddWithValue("@line", item.Line.Id);
            cmd.Parameters.AddWithValue("@code", item.Line.Code);
            cmd.Parameters.AddWithValue(
                "@description", item.Line.Description);
            cmd.Parameters.AddWithValue("@unit", item.Line.Unit);
            cmd.Parameters.AddWithValue("@quantity", D(item.Quantity));
            cmd.Parameters.AddWithValue("@price", D(item.Price));
            cmd.Parameters.AddWithValue("@vat", D(item.Vat));
            cmd.Parameters.AddWithValue("@discountRate", D(item.Discount));
            cmd.Parameters.AddWithValue(
                "@discountAmount", D(item.DiscountAmount));
            cmd.Parameters.AddWithValue("@net", D(item.Net));
            cmd.Parameters.AddWithValue("@vatAmount", D(item.VatAmount));
            cmd.Parameters.AddWithValue("@gross", D(item.Gross));

            await cmd.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        return RedirectToAction(nameof(Index));
    }

    // MC_PURCHASE_INVOICE_DETAILS_V2
    [HttpGet("Details")]
    public async Task<IActionResult> Details([FromQuery] long id)
    {
        if (id <= 0)
            return BadRequest("Gecersiz fatura numarasi.");

        await using var db = await OpenDb();

        var html = new StringBuilder();
        BeginHtml(html, "Satın Alma Faturası Detayı");

        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                SELECT
                    i.InvoiceNumber,
                    i.InvoiceDate,
                    i.SupplierId,
                    i.Currency,
                    i.Status,
                    i.Subtotal,
                    i.DiscountTotal,
                    i.VatTotal,
                    i.GrandTotal,
                    COALESCE(i.Notes, ''),
                    COALESCE(r.ReceiptNumber, '')
                FROM MCPurchaseInvoices i
                LEFT JOIN PurchaseGoodsReceipts r
                    ON r.Id = i.PurchaseGoodsReceiptId
                WHERE i.Id = @id
                """;

            cmd.Parameters.AddWithValue("@id", id);

            await using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return NotFound("Fatura bulunamadi.");

            html.Append("<h1>Satın Alma Faturası</h1><table>");

            string[] labels =
            {
                "Fatura Numarası",
                "Fatura Tarihi",
                "Tedarikçi ID",
                "Para Birimi",
                "Durum",
                "Ara Toplam",
                "İskonto",
                "KDV",
                "Genel Toplam",
                "Notlar",
                "Mal Kabul İrsaliyesi"
            };

            for (var i = 0; i < labels.Length; i++)
            {
                html.Append("<tr><th>");
                html.Append(H(labels[i]));
                html.Append("</th><td>");
                html.Append(H(reader.GetValue(i)));
                html.Append("</td></tr>");
            }

            html.Append("</table>");

            // MC_INVOICE_FINALIZE_BUTTON_V1
            var status = Convert.ToString(reader.GetValue(4)) ?? "";

            if (status == "Draft")
            {
                var token = _antiforgery
                    .GetAndStoreTokens(HttpContext)
                    .RequestToken ?? "";

                html.Append("""
                    <div class="notice">
                    <strong>Fatura taslak durumunda.</strong>
                    <p>Kesinleştirme işleminden sonra fatura
                    taslak durumuna geri dönmez.</p>
                    <form method="post"
                          action="/MCPurchaseInvoiceEntry/Finalize"
                          onsubmit="return confirm('Bu faturayı kesinleştirmek istediğinize emin misiniz?');">
                    """);

                html.Append("<input type='hidden' name='__RequestVerificationToken' value='");
                html.Append(H(token));
                html.Append("'>");

                html.Append("<input type='hidden' name='id' value='");
                html.Append(id);
                html.Append("'>");

                html.Append("""
                    <button type="submit">Faturayı Kesinleştir</button>
                    </form>
                    </div>
                    """);
            }
            else if (status == "Finalized")
            {
                html.Append("""
                    <p class="notice">
                    <strong>Bu fatura kesinleştirilmiştir.</strong>
                    Cari borç aktarım durumu ayrıca kontrol edilmelidir.
                    </p>
                    """);
            }
        }

        html.Append("""
            <h2>Fatura Kalemleri</h2>
            <table>
            <thead>
            <tr>
              <th>Stok Kodu</th>
              <th>Malzeme</th>
              <th>Birim</th>
              <th>Miktar</th>
              <th>Birim Fiyat</th>
              <th>İskonto %</th>
              <th>İskonto Tutarı</th>
              <th>KDV Hariç</th>
              <th>KDV %</th>
              <th>KDV Tutarı</th>
              <th>KDV Dahil</th>
            </tr>
            </thead>
            <tbody>
            """);

        var count = 0;

        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                SELECT
                    COALESCE(ProductCode, ''),
                    ProductDescription,
                    Unit,
                    Quantity,
                    UnitPrice,
                    DiscountRate,
                    DiscountAmount,
                    NetAmount,
                    VatRate,
                    VatAmount,
                    GrossAmount
                FROM MCPurchaseInvoiceLines
                WHERE PurchaseInvoiceId = @id
                ORDER BY Id
                """;

            cmd.Parameters.AddWithValue("@id", id);

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                count++;
                html.Append("<tr>");

                for (var i = 0; i < 11; i++)
                {
                    html.Append("<td>");
                    html.Append(H(reader.GetValue(i)));
                    html.Append("</td>");
                }

                html.Append("</tr>");
            }
        }

        html.Append("</tbody></table>");

        html.Append("<p><strong>Kalem sayısı:</strong> ");
        html.Append(count);
        html.Append("</p>");

        html.Append("""
            <p class="notice">
            Fatura taslak aşamasındadır.
            Bu ekrandan stok veya cari hareketi oluşturulmaz.
            </p>
            """);

        EndHtml(html);

        return View(
    "~/Views/Shared/MCPurchaseEmbeddedPage.cshtml",
    new MasterChefTakip.Models.MCPurchaseEmbeddedPageModel
    {
        Html = html.ToString()
    }
);
    }
    // MC_PURCHASE_INVOICE_FINALIZE_V1
    [HttpPost("Finalize")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FinalizeInvoice([FromForm] long id)
    {
        if (id <= 0)
            return BadRequest("Gecersiz fatura.");

        await using var db = await OpenDb();

        await using var transaction =
            (SqliteTransaction)await db.BeginTransactionAsync();

        decimal subtotal = 0m;
        decimal discountTotal = 0m;
        decimal vatTotal = 0m;
        decimal grandTotal = 0m;
        var lineCount = 0;

        await using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = """
                SELECT Quantity, UnitPrice, DiscountRate,
                       VatRate, DiscountAmount,
                       NetAmount, VatAmount, GrossAmount
                FROM MCPurchaseInvoiceLines
                WHERE PurchaseInvoiceId = @id
                """;

            cmd.Parameters.AddWithValue("@id", id);

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lineCount++;

                decimal Read(int index) =>
                    decimal.Parse(
                        Convert.ToString(reader.GetValue(index)) ?? "0",
                        CultureInfo.InvariantCulture);

                var quantity = Read(0);
                var unitPrice = Read(1);
                var discountRate = Read(2);
                var vatRate = Read(3);

                var savedDiscount = Read(4);
                var savedNet = Read(5);
                var savedVat = Read(6);
                var savedGross = Read(7);

                if (quantity <= 0 || unitPrice < 0 ||
                    discountRate < 0 || discountRate > 100 ||
                    vatRate < 0 || vatRate > 100)
                    return BadRequest("Fatura kalemlerinde gecersiz deger.");

                var baseAmount = Money(quantity * unitPrice);
                var discount = Money(
                    baseAmount * discountRate / 100m);

                var net = baseAmount - discount;
                var vat = Money(net * vatRate / 100m);
                var gross = net + vat;

                if (savedDiscount != discount ||
                    savedNet != net ||
                    savedVat != vat ||
                    savedGross != gross)
                    return Conflict(
                        "Fatura tutarlari kalem hesaplariyla uyusmuyor.");

                subtotal += baseAmount;
                discountTotal += discount;
                vatTotal += vat;
                grandTotal += gross;
            }
        }

        if (lineCount == 0)
            return BadRequest("Kalemsiz fatura kesinlestirilemez.");

        await using var update = db.CreateCommand();
        update.Transaction = transaction;

        update.CommandText = """
            UPDATE MCPurchaseInvoices
            SET Status = 'Finalized'
            WHERE Id = @id
              AND Status = 'Draft'
              AND Subtotal = @subtotal
              AND DiscountTotal = @discount
              AND VatTotal = @vat
              AND GrandTotal = @grand
            """;

        update.Parameters.AddWithValue("@id", id);
        update.Parameters.AddWithValue("@subtotal", D(subtotal));
        update.Parameters.AddWithValue("@discount", D(discountTotal));
        update.Parameters.AddWithValue("@vat", D(vatTotal));
        update.Parameters.AddWithValue("@grand", D(grandTotal));

        var affected = await update.ExecuteNonQueryAsync();

        if (affected != 1)
            return Conflict(
                "Fatura daha once kesinlestirilmis veya toplamlar uyusmuyor.");

        await transaction.CommitAsync();

        return RedirectToAction(nameof(Details), new { id });
    }
    private static void BeginHtml(StringBuilder html, string title)
    {
        html.Append("""
            <!doctype html>
            <html lang="tr">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <title>
            """);

        html.Append(H(title));

        html.Append("""
            </title>
            <style>
            body{font-family:Arial,sans-serif;background:#f3f6fa;
                 color:#182333;margin:0;padding:25px}
            main{max-width:1300px;margin:auto;background:white;
                 padding:25px;border-radius:12px}
            h1{margin-top:0}
            table{width:100%;border-collapse:collapse;margin:20px 0}
            th,td{padding:10px;text-align:left;border-bottom:1px solid #ddd}
            th{background:#edf2f7}
            input,select,textarea{padding:9px;border:1px solid #cbd5e1;
                 border-radius:6px;box-sizing:border-box;max-width:100%}
            input[type=number]{width:115px}
            .fields{display:flex;gap:15px;flex-wrap:wrap;margin:20px 0}
            .fields label{display:flex;flex-direction:column;gap:7px}
            .notice{background:#fff5d9;padding:12px;border-radius:6px}
            button{background:#166534;color:white;border:0;
                 padding:12px 20px;border-radius:7px;cursor:pointer}
            a{color:#14569a}
            </style></head><body><main>
            <a href="/MCPurchaseInvoiceEntry">← Fatura Listesi</a>
            """);
    }

    private static void EndHtml(StringBuilder html)
    {
        html.Append("</main></body></html>");
    }
}