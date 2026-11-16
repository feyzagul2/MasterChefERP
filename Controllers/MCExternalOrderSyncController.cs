using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using MasterChefTakip.Data;
using MasterChefTakip.Models;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.VisualBasic.FileIO;

namespace MasterChefTakip.Controllers;

// MC_EXTERNAL_ORDER_REAL_SYNC_V1
// Harici siparislerin salt-operasyonel entegrasyon kopyasi.
// Mevcut siparis sisteminin yerine gecmez.
[Authorize(Roles = AppRoles.Admin)]
[Route("MCExternalOrders")]
public sealed class MCExternalOrderSyncController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IAntiforgery _antiforgery;

    public MCExternalOrderSyncController(
        ApplicationDbContext db,
        IAntiforgery antiforgery)
    {
        _db = db;
        _antiforgery = antiforgery;
    }

    private sealed record ImportLine(
        int LineNumber,
        string OrderNo,
        string ProductCode,
        decimal Quantity,
        string CustomerCode,
        string OrderDate
    );

    private static string H(object? value) =>
        WebUtility.HtmlEncode(
            Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");

    private static string Q(string identifier)
    {
        if (identifier.Length == 0 ||
            identifier.Any(x => !(char.IsLetterOrDigit(x) || x == '_')))
            throw new InvalidOperationException("Gecersiz SQL alani.");

        return "\"" + identifier + "\"";
    }

    private static SqliteCommand Command(
        SqliteConnection db,
        string sql,
        SqliteTransaction? tx = null)
    {
        var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        return cmd;
    }

    private static void Param(
        SqliteCommand cmd,
        string name,
        object? value)
    {
        cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private static void Ensure(
        SqliteConnection db,
        SqliteTransaction tx)
    {
        using var cmd = Command(db, """
            CREATE TABLE IF NOT EXISTS MCExternalSyncImports (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FileHash TEXT NOT NULL UNIQUE,
                FileName TEXT NOT NULL,
                RowCount INTEGER NOT NULL,
                ImportedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS MCExternalSyncLines (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ImportId INTEGER NOT NULL,
                SourceLine INTEGER NOT NULL,
                ExternalOrderId TEXT NOT NULL,
                ProductCode TEXT NOT NULL,
                Quantity TEXT NOT NULL,
                CustomerCode TEXT,
                OrderDate TEXT,
                ProductId INTEGER,
                MatchStatus TEXT NOT NULL,
                FOREIGN KEY(ImportId)
                    REFERENCES MCExternalSyncImports(Id),
                UNIQUE(ImportId,SourceLine)
            );

            CREATE INDEX IF NOT EXISTS
                IX_MCExternalSyncLines_OrderNo
            ON MCExternalSyncLines(ExternalOrderId);

            CREATE INDEX IF NOT EXISTS
                IX_MCExternalSyncLines_Product
            ON MCExternalSyncLines(ProductCode);
            """, tx);

        cmd.ExecuteNonQuery();
    }

    private static HashSet<string> Columns(
        SqliteConnection db,
        string table,
        SqliteTransaction tx)
    {
        var result = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        using var cmd = Command(
            db, "PRAGMA table_info(" + Q(table) + ")", tx);

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
            result.Add(reader.GetString(1));

        return result;
    }

    private static string? ProductCodeColumn(
        SqliteConnection db,
        SqliteTransaction tx)
    {
        var columns = Columns(db, "Products", tx);

        foreach (var candidate in new[]
        {
            "ProductCode", "StockCode", "Code",
            "Sku", "SKU", "ItemCode", "Barcode"
        })
        {
            if (columns.Contains(candidate))
                return candidate;
        }

        return null;
    }

    private static (long? ProductId, string Status)
        MatchProduct(
            SqliteConnection db,
            SqliteTransaction tx,
            string? codeColumn,
            string productCode)
    {
        if (codeColumn == null)
            return (null, "KOD_ALANI_YOK");

        using var cmd = Command(
            db,
            "SELECT Id FROM Products WHERE " +
            Q(codeColumn) +
            " = @code COLLATE NOCASE LIMIT 2",
            tx);

        Param(cmd, "@code", productCode);

        var ids = new List<long>();

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
            ids.Add(reader.GetInt64(0));

        if (ids.Count == 0)
            return (null, "ESLESMEDI");

        if (ids.Count > 1)
            return (null, "BELIRSIZ");

        return (ids[0], "ESLESTI");
    }

    private string Token()
    {
        var token = _antiforgery
            .GetAndStoreTokens(HttpContext)
            .RequestToken;

        return "<input type='hidden' " +
            "name='__RequestVerificationToken' value='" +
            H(token) + "'>";
    }

    private static string Layout(string body)
    {
        return """
        <!doctype html><html lang='tr'><head>
        <meta charset='utf-8'>
        <meta name='viewport'
              content='width=device-width,initial-scale=1'>
        <title>MASTER CHEF - Sipariş Aktarımı</title>
        <style>
        body{font-family:Arial,sans-serif;background:#f1f5f9;
             color:#172033;margin:0;padding:28px}
        main{max-width:1200px;margin:auto}
        section{background:white;padding:24px;margin:18px 0;
                border:1px solid #e2e8f0;border-radius:12px}
        input{padding:12px}
        button{padding:12px 22px;background:#183b5a;
               border:0;border-radius:7px;color:white}
        table{width:100%;border-collapse:collapse}
        th,td{padding:11px;text-align:left;
              border-bottom:1px solid #e2e8f0}
        th{background:#eff6ff}
        .good{background:#dcfce7;padding:15px}
        .warn{background:#fff7ed;padding:15px}
        a{color:#1d4ed8}
        </style></head><body><main>
        <h1>MASTER CHEF - Sipariş Sistemi Entegrasyonu</h1>
        """ + body + "</main></body></html>";
    }

    [HttpGet("import")]
    public IActionResult Index()
    {
        var html = new StringBuilder();

        html.Append("""
        <section>
        <h2>Mevcut Siparişlerden TAKİP'e Aktarım</h2>
        <p>Mevcut sipariş programınızdan aldığınız CSV
        dosyasını yükleyin.</p>
        <p>Bu işlem siparişleri entegrasyon kayıtlarına aktarır.
        Yeni sipariş oluşturmaz ve stoktan ürün düşmez.</p>
        <form method='post'
              action='/MCExternalOrders/import'
              enctype='multipart/form-data'>
        """);

        html.Append(Token());

        html.Append("""
        <input type='file' name='file' accept='.csv' required>
        <button type='submit' name='validateOnly' value='true' style='background:#64748b;margin-right:10px'>Önce Kontrol Et</button><button type='submit' name='validateOnly' value='false'>Siparişleri Aktar</button>
        </form>
        </section>

        <section>
        <h2>CSV Dosyası</h2>
        <p>Zorunlu sütunlar:
        <strong>ExternalOrderId, ProductCode, Quantity</strong></p>
        <p>İsteğe bağlı:
        CustomerCode, OrderDate.</p>
        <p>Tekrarlanan dosyalar ikinci kez aktarılmaz.</p>
        </section>
        """);

        return Content(
            Layout(html.ToString()),
            "text/html; charset=utf-8");
    }

    [HttpPost("import")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(5242880)]
    public async Task<IActionResult> Import(IFormFile? file, bool validateOnly = false)
    {
        if (file == null || file.Length == 0 ||
            file.Length > 5242880)
            return BadRequest("CSV dosyasi gecersiz veya cok buyuk.");

        if (!string.Equals(
            Path.GetExtension(file.FileName),
            ".csv",
            StringComparison.OrdinalIgnoreCase))
            return BadRequest("Yalnizca CSV dosyasi kabul edilir.");

        byte[] bytes;

        using (var memory = new MemoryStream())
        {
            await file.CopyToAsync(memory);

            if (memory.Length > 5242880)
                return BadRequest("Dosya boyutu siniri asildi.");

            bytes = memory.ToArray();
        }

        var fileHash = Convert.ToHexString(
            SHA256.HashData(bytes));

        string text;

        using (var memory = new MemoryStream(bytes))
        using (var reader = new StreamReader(
            memory, new UTF8Encoding(false, true), true))
        {
            try
            {
                text = await reader.ReadToEndAsync();
            }
            catch (DecoderFallbackException)
            {
                return BadRequest(
                    "CSV dosyasi UTF-8 biciminde okunamadi.");
            }
        }

        var lines = new List<ImportLine>();

        try
        {
            using var textReader = new StringReader(text);
            using var parser = new TextFieldParser(textReader);

            parser.TextFieldType = FieldType.Delimited;
            parser.HasFieldsEnclosedInQuotes = true;

            var firstLine = text.Split(
                new[] { "\r\n", "\n" },
                StringSplitOptions.None).FirstOrDefault() ?? "";

            parser.SetDelimiters(
                firstLine.Count(x => x == ';') >
                firstLine.Count(x => x == ',') ? ";" : ",");

            var headers = parser.ReadFields();

            if (headers == null)
                return BadRequest("CSV basliklari bulunamadi.");

            var names = headers
                .Select(x => (x ?? "")
                    .Trim()
                    .TrimStart('\uFEFF'))
                .ToArray();

            int Col(string name) =>
                Array.FindIndex(names,
                    x => string.Equals(
                        x, name,
                        StringComparison.OrdinalIgnoreCase));

            int orderCol = Col("ExternalOrderId");
            int productCol = Col("ProductCode");
            int quantityCol = Col("Quantity");
            int customerCol = Col("CustomerCode");
            int dateCol = Col("OrderDate");

            if (orderCol < 0 ||
                productCol < 0 ||
                quantityCol < 0)
                return BadRequest(
                    "Zorunlu CSV sutunlari eksik.");

            var rowNumber = 1;

            while (!parser.EndOfData)
            {
                rowNumber++;

                if (rowNumber > 10001)
                    return BadRequest(
                        "En fazla 10000 satir aktarilabilir.");

                var values = parser.ReadFields() ??
                    Array.Empty<string>();

                string Value(int column) =>
                    column >= 0 && column < values.Length
                        ? (values[column] ?? "").Trim()
                        : "";

                var orderNo = Value(orderCol);
                var productCode = Value(productCol);
                var quantityText = Value(quantityCol);
                var customer = Value(customerCol);
                var date = Value(dateCol);

                if (orderNo.Length == 0 ||
                    orderNo.Length > 100 ||
                    productCode.Length == 0 ||
                    productCode.Length > 100 ||
                    customer.Length > 150 ||
                    date.Length > 50)
                    return BadRequest(
                        "CSV satir " + rowNumber +
                        ": Siparis veya urun bilgisi gecersiz.");

                if (!decimal.TryParse(
                    quantityText.Replace(",", "."),
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out var quantity) ||
                    quantity <= 0m ||
                    quantity > 100000000m)
                    return BadRequest(
                        "CSV satir " + rowNumber +
                        ": Gecersiz miktar.");

                lines.Add(new ImportLine(
                    rowNumber,
                    orderNo,
                    productCode,
                    quantity,
                    customer,
                    date));
            }
        }
        catch (MalformedLineException)
        {
            return BadRequest(
                "CSV dosyasinda hatali satir yapisi var.");
        }

        if (lines.Count == 0)
            return BadRequest("Aktarilacak siparis bulunamadi.");

        var connection = (SqliteConnection)
            _db.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        await using var tx =
            await connection.BeginTransactionAsync();

        var sqliteTx = (SqliteTransaction)tx;

        try
        {
            Ensure(connection, sqliteTx);

            using (var duplicate = Command(
                connection,
                "SELECT COUNT(*) FROM MCExternalSyncImports " +
                "WHERE FileHash=@hash",
                sqliteTx))
            {
                Param(duplicate, "@hash", fileHash);

                if (Convert.ToInt64(
                    duplicate.ExecuteScalar()) > 0)
                {
                    await tx.RollbackAsync();

                    return Content(Layout("""
                    <section>
                    <h2>Dosya Daha Önce Aktarılmış</h2>
                    <p class='warn'>
                    Aynı CSV dosyası tekrar aktarılmadı.
                    Mükerrer kayıt oluşturulmadı.</p>
                    <p><a href='/MCExternalOrders/import'>
                    Geri dön</a></p>
                    </section>
                    """), "text/html; charset=utf-8");
                }
            }

            // MC_EXTERNAL_ORDER_DUPLICATE_GUARD_V2
            // Farkli CSV dosyalarindan ayni siparis numarasinin
            // tekrar aktarilmasini engelle.
            // Mevcut siparis, stok ve finans kayitlari degismez.

            using (var mcCreateClaims = Command(
                connection,
                """
                CREATE TABLE IF NOT EXISTS MCExternalSyncOrderClaims (
                    ExternalOrderId TEXT PRIMARY KEY COLLATE NOCASE,
                    ImportId INTEGER NOT NULL
                );
                """,
                sqliteTx))
            {
                mcCreateClaims.ExecuteNonQuery();
            }

            var mcOrderNumbers = lines
                .Select(x => x.OrderNo.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var mcOrderNumber in mcOrderNumbers)
            {
                using var mcCheck = Command(
                    connection,
                    """
                    SELECT
                        (
                            SELECT COUNT(*)
                            FROM MCExternalSyncLines
                            WHERE ExternalOrderId = @order COLLATE NOCASE
                        )
                        +
                        (
                            SELECT COUNT(*)
                            FROM MCExternalSyncOrderClaims
                            WHERE ExternalOrderId = @order COLLATE NOCASE
                        )
                    """,
                    sqliteTx);

                Param(mcCheck,"@order",mcOrderNumber);

                var mcExisting = Convert.ToInt64(
                    mcCheck.ExecuteScalar() ?? 0L);

                if (mcExisting > 0)
                {
                    await tx.RollbackAsync();

                    return Conflict(
                        "SIPARIS AKTARIMI DURDURULDU. " +
                        "Bu siparis numarasi daha once aktarilmis: " +
                        mcOrderNumber +
                        ". Mevcut kayitlar korunmustur.");
                }
            }
            // MC_ORDER_IMPORT_DRY_RUN_V2
            // Dosya ayrıştırıldıktan ve mükerrer siparişler
            // kontrol edildikten sonra ürün eşleşmesi incelenir.
            // Ön kontrolde kalıcı sipariş kaydı yapılmaz.
            if (validateOnly)
            {
                var mcPreviewColumns = Columns(
                    connection, "Products", sqliteTx);

                var mcPreviewCodeColumn =
                    mcPreviewColumns.Contains("Id")
                    ? ProductCodeColumn(connection, sqliteTx)
                    : null;

                int mcMatched = 0;
                int mcUnmatched = 0;

                var mcUnmatchedCodes =
                    new System.Collections.Generic.HashSet<string>(
                        StringComparer.OrdinalIgnoreCase);

                var mcPreviewCache =
                    new System.Collections.Generic.Dictionary<
                        string, (long? ProductId, string Status)>(
                            StringComparer.OrdinalIgnoreCase);

                foreach (var mcLine in lines)
                {
                    if (!mcPreviewCache.TryGetValue(
                        mcLine.ProductCode, out var mcMatch))
                    {
                        mcMatch = MatchProduct(
                            connection,
                            sqliteTx,
                            mcPreviewCodeColumn,
                            mcLine.ProductCode);

                        mcPreviewCache[mcLine.ProductCode] = mcMatch;
                    }

                    if (mcMatch.ProductId.HasValue)
                    {
                        mcMatched++;
                    }
                    else
                    {
                        mcUnmatched++;
                        mcUnmatchedCodes.Add(mcLine.ProductCode);
                    }
                }

                var mcPreviewHtml = new StringBuilder();

                mcPreviewHtml.Append(
                    "<section><h2>Sipariş Ön Kontrol Sonucu</h2>");

                mcPreviewHtml.Append(
                    "<p class='good'>CSV dosyası kontrol edildi. " +
                    "Hiçbir sipariş kaydedilmedi.</p>");

                mcPreviewHtml.Append("<table>");

                void McPreviewRow(string label, object value)
                {
                    mcPreviewHtml.Append("<tr><th>");
                    mcPreviewHtml.Append(H(label));
                    mcPreviewHtml.Append("</th><td>");
                    mcPreviewHtml.Append(H(value));
                    mcPreviewHtml.Append("</td></tr>");
                }

                McPreviewRow(
                    "Toplam Sipariş Satırı",
                    lines.Count);

                McPreviewRow(
                    "Farklı Sipariş Numarası",
                    mcOrderNumbers.Count);

                McPreviewRow(
                    "Ürün Kartıyla Eşleşen",
                    mcMatched);

                McPreviewRow(
                    "Eşleşmeyen Satır",
                    mcUnmatched);

                McPreviewRow(
                    "Eşleşmeyen Farklı Ürün Kodu",
                    mcUnmatchedCodes.Count);

                mcPreviewHtml.Append("</table>");

                if (mcUnmatchedCodes.Count > 0)
                {
                    mcPreviewHtml.Append(
                        "<h3>Eşleşmeyen Ürün Kodları</h3>");

                    mcPreviewHtml.Append(
                        "<p class='warn'>Bu ürünlerin mevcut " +
                        "stok kartlarıyla eşleşmesi bulunamadı.</p>");

                    mcPreviewHtml.Append(
                        "<table><tr><th>Ürün Kodu</th></tr>");

                    foreach (var mcCode in mcUnmatchedCodes
                        .OrderBy(x => x)
                        .Take(200))
                    {
                        mcPreviewHtml.Append("<tr><td>");
                        mcPreviewHtml.Append(H(mcCode));
                        mcPreviewHtml.Append("</td></tr>");
                    }

                    mcPreviewHtml.Append("</table>");

                    if (mcUnmatchedCodes.Count > 200)
                    {
                        mcPreviewHtml.Append(
                            "<p>İlk 200 eşleşmeyen ürün gösteriliyor.</p>");
                    }
                }
                else
                {
                    mcPreviewHtml.Append(
                        "<p class='good'>Tüm ürün kodları " +
                        "mevcut ürün kartlarıyla eşleşti.</p>");
                }

                mcPreviewHtml.Append(
                    "<p><strong>Ön kontrol tamamlandı.</strong> " +
                    "Dosya aktarılmadı; stok, sevkiyat ve " +
                    "finans kayıtları değiştirilmedi.</p>");

                mcPreviewHtml.Append(
                    "<p>Gerçek aktarım için aynı CSV dosyasını " +
                    "yeniden seçip Siparişleri Aktar " +
                    "düğmesine basabilirsiniz.</p>");

                mcPreviewHtml.Append(
                    "<p><a href='/MCExternalOrders/import'>" +
                    "Aktarım ekranına dön</a></p></section>");

                await tx.RollbackAsync();

                return Content(
                    Layout(mcPreviewHtml.ToString()),
                    "text/html; charset=utf-8");
            }
            using var insertImport = Command(
                connection,
                """
                INSERT INTO MCExternalSyncImports
                    (FileHash,FileName,RowCount,ImportedAtUtc)
                VALUES (@hash,@name,@count,@time);
                SELECT last_insert_rowid();
                """,
                sqliteTx);

            Param(insertImport, "@hash", fileHash);
            Param(insertImport, "@name",
                Path.GetFileName(file.FileName));
            Param(insertImport, "@count", lines.Count);
            Param(insertImport, "@time",
                DateTime.UtcNow.ToString("O",
                    CultureInfo.InvariantCulture));

            var importId = Convert.ToInt64(
                insertImport.ExecuteScalar());
            // MC_EXTERNAL_ORDER_DUPLICATE_GUARD_V2
            // Siparis numaralarini ayni transaction icinde rezerve et.
            // SQLite benzersiz anahtar mukerrer kaydi engeller.
            foreach (var mcOrderNumber in mcOrderNumbers)
            {
                using var mcClaim = Command(
                    connection,
                    """
                    INSERT INTO MCExternalSyncOrderClaims
                        (ExternalOrderId,ImportId)
                    VALUES (@order,@import)
                    """,
                    sqliteTx);

                Param(mcClaim,"@order",mcOrderNumber);
                Param(mcClaim,"@import",importId);

                mcClaim.ExecuteNonQuery();
            }


            var productColumns = Columns(
                connection, "Products", sqliteTx);

            var codeColumn =
                productColumns.Contains("Id")
                ? ProductCodeColumn(connection, sqliteTx)
                : null;

            int matched = 0;
            int unmatched = 0;

            foreach (var line in lines)
            {
                var match = MatchProduct(
                    connection,
                    sqliteTx,
                    codeColumn,
                    line.ProductCode);

                if (match.ProductId.HasValue)
                    matched++;
                else
                    unmatched++;

                using var insertLine = Command(
                    connection,
                    """
                    INSERT INTO MCExternalSyncLines
                        (ImportId,SourceLine,ExternalOrderId,
                         ProductCode,Quantity,CustomerCode,
                         OrderDate,ProductId,MatchStatus)
                    VALUES
                        (@import,@line,@order,@product,@qty,
                         @customer,@date,@productId,@status)
                    """,
                    sqliteTx);

                Param(insertLine, "@import", importId);
                Param(insertLine, "@line", line.LineNumber);
                Param(insertLine, "@order", line.OrderNo);
                Param(insertLine, "@product", line.ProductCode);
                Param(insertLine, "@qty",
                    line.Quantity.ToString(
                        CultureInfo.InvariantCulture));
                Param(insertLine, "@customer", line.CustomerCode);
                Param(insertLine, "@date", line.OrderDate);
                Param(insertLine, "@productId", match.ProductId);
                Param(insertLine, "@status", match.Status);

                insertLine.ExecuteNonQuery();
            }

            await tx.CommitAsync();

            var result = new StringBuilder();

            result.Append("""
                <section>
                <h2>Sipariş Aktarımı Kaydedildi</h2>
                <p class='good'>
                Harici sipariş kayıtları TAKİP entegrasyon
                alanına aktarıldı.</p>
                <table>
                """);

            void Row(string label, object value)
            {
                result.Append("<tr><th>");
                result.Append(H(label));
                result.Append("</th><td>");
                result.Append(H(value));
                result.Append("</td></tr>");
            }

            Row("Aktarım No", importId);
            Row("Aktarılan Satır", lines.Count);
            Row("Ürün Kartıyla Eşleşen", matched);
            Row("Eşleşmeyen / Belirsiz", unmatched);

            result.Append("""
                </table>
                <p>Bu işlem stok, sevkiyat veya finans
                hareketi oluşturmadı.</p>
                <p><a href='/MCExternalOrders/import'>
                Yeni dosya aktar</a></p>
                </section>
                """);

            return Content(
                Layout(result.ToString()),
                "text/html; charset=utf-8");
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }
}