using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using System.Text;

namespace MasterChefTakip.Controllers;

[Authorize(Roles = "Admin")]
[Route("MCPurchaseCariExport")]
public class MCPurchaseCariExportController : Controller
{
    private readonly IConfiguration _configuration;

    public MCPurchaseCariExportController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
    {
        var connectionString =
            _configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
            return StatusCode(503, "Veritabani baglantisi bulunamadi.");

        var builder = new SqliteConnectionStringBuilder(connectionString)
        {
            Mode = SqliteOpenMode.ReadOnly
        };

        await using var db = new SqliteConnection(builder.ToString());
        await db.OpenAsync();

        await using var cmd = db.CreateCommand();

        cmd.CommandText = """
            SELECT
                p.PurchaseInvoiceId,
                COALESCE(i.InvoiceNumber, ''),
                COALESCE(i.SupplierId, 0),
                p.CariAccountId,
                p.CariMovementId,
                COALESCE(p.PostedAtUtc, ''),
                COALESCE(p.PostedBy, ''),
                COALESCE(p.RequestToken, '')
            FROM MCPurchaseInvoiceCariPostings p
            LEFT JOIN MCPurchaseInvoices i
                ON i.Id = p.PurchaseInvoiceId
            ORDER BY p.PostedAtUtc DESC, p.PurchaseInvoiceId DESC
            """;

                // MC_CARI_DATE_SAFE_V2
        if (startDate.HasValue && endDate.HasValue &&
            startDate.Value.Date > endDate.Value.Date)
            return BadRequest("Baslangic tarihi bitis tarihinden buyuk olamaz.");

        var filterStart = startDate?.Date;
        var filterEnd = endDate?.Date;

                // MC_CARI_EXPORT_METADATA_V1
        var csv = new StringBuilder();

        static string ReportDate(DateTime? value)
        {
            return value.HasValue
                ? value.Value.ToString("dd.MM.yyyy")
                : "Tum tarihler";
        }

        csv.AppendLine("CARI AKTARIM RAPORU");
        csv.AppendLine(
            "Baslangic Tarihi;" + ReportDate(startDate)
        );
        csv.AppendLine(
            "Bitis Tarihi;" + ReportDate(endDate)
        );
        csv.AppendLine(
            "Rapor Zamani;" +
            DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss")
        );
        csv.AppendLine();

        csv.AppendLine(
            "Fatura ID;Fatura Numarasi;Tedarikci ID;" +
            "Cari Hesap ID;Cari Hareket ID;Aktarim Tarihi;" +
            "Islemi Yapan;Islem Belirteci"
        );

        static string Cell(object value)
        {
            var text = Convert.ToString(
                value,
                System.Globalization.CultureInfo.InvariantCulture
            ) ?? "";

            // Excel formula injection korumasi
            if (text.Length > 0 &&
                (text[0] == '=' ||
                 text[0] == '+' ||
                 text[0] == '-' ||
                 text[0] == '@' ||
                 text[0] == '\t' ||
                 text[0] == '\r'))
            {
                text = "'" + text;
            }

            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        // MC_CARI_EXPORT_COUNT_V1
        var exportCount = 0;
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var cells = new List<string>();

            for (var i = 0; i < reader.FieldCount; i++)
            {
                cells.Add(
                    Cell(reader.IsDBNull(i) ? "" : reader.GetValue(i))
                );
            }

                        var postedAtText = reader.IsDBNull(5)
                ? ""
                : Convert.ToString(reader.GetValue(5)) ?? "";

            if (filterStart.HasValue || filterEnd.HasValue)
            {
                if (!DateTime.TryParse(
                    postedAtText,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out var postedAt))
                    continue;

                if (filterStart.HasValue &&
                    postedAt.Date < filterStart.Value)
                    continue;

                if (filterEnd.HasValue &&
                    postedAt.Date > filterEnd.Value)
                    continue;
            }

            csv.AppendLine(string.Join(";", cells));
            exportCount++;
        }

                csv.AppendLine();
        csv.AppendLine("TOPLAM KAYIT;" + exportCount);

        if (exportCount == 0)
            csv.AppendLine("BILGI;Secilen donemde cari aktarim kaydi bulunamadi.");

        var utf8 = new UTF8Encoding(true);

        Response.Headers.CacheControl = "no-store";

        return File(
            utf8.GetBytes(csv.ToString()),
            "text/csv; charset=utf-8",
            "Cari_Aktarim_Raporu_" +
                DateTime.Now.ToString("yyyyMMdd") + ".csv"
        );
    }
}