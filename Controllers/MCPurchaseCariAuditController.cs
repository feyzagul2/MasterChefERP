using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using System.Text;

namespace MasterChefTakip.Controllers
{
    [Authorize(Roles = "Admin,Yonetici")]
    [Route("MCPurchaseCariAudit")]
    public class MCPurchaseCariAuditController : Controller
    {
        private readonly IWebHostEnvironment _env;

        public MCPurchaseCariAuditController(IWebHostEnvironment env)
        {
            _env = env;
        }

        // MC_CARI_AUDIT_DETAIL_EXPORT_V1
        [HttpGet("DetailsCsv")]
        public async Task<IActionResult> DetailsCsv([FromQuery] bool onlyProblems = false, [FromQuery] string? issueType = null)
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
                    p.PurchaseInvoiceId,
                    COALESCE(i.InvoiceNumber, ''),
                    i.GrandTotal,
                    i.Currency,
                    p.CariAccountId,
                    p.CariMovementId,
                    m.CariAccountId,
                    m.Currency,
                    m.Debit,
                    m.Credit,
                    CASE
                        WHEN i.Id IS NULL THEN 'FATURA_YOK'
                        WHEN m.Id IS NULL THEN 'CARI_HAREKET_YOK'
                        WHEN p.CariAccountId IS NULL
                          OR m.CariAccountId IS NULL
                          OR p.CariAccountId <> m.CariAccountId
                          THEN 'CARI_HESAP_FARKI'
                        WHEN NULLIF(TRIM(i.Currency), '') IS NULL
                          OR NULLIF(TRIM(m.Currency), '') IS NULL
                          OR UPPER(TRIM(i.Currency)) <> UPPER(TRIM(m.Currency))
                          THEN 'PARA_BIRIMI_FARKI'
                        WHEN i.GrandTotal IS NULL
                          OR m.Debit IS NULL OR m.Credit IS NULL
                          OR ABS(ABS(CAST(i.GrandTotal AS REAL))
                          - ABS(CAST(m.Debit AS REAL) - CAST(m.Credit AS REAL))) > 0.01
                          THEN 'TUTAR_FARKI'
                        ELSE 'ESLESEN'
                    END
                FROM MCPurchaseInvoiceCariPostings p
                LEFT JOIN MCPurchaseInvoices i ON i.Id = p.PurchaseInvoiceId
                LEFT JOIN MCCariMovements m ON m.Id = p.CariMovementId
                ORDER BY p.PurchaseInvoiceId DESC
                """;

            var csv = new StringBuilder();
            csv.AppendLine("Fatura ID;Fatura No;Fatura Tutari;Fatura Para Birimi;Aktarim Cari Hesap;Cari Hareket ID;Hareket Cari Hesap;Hareket Para Birimi;Borc;Alacak;Durum");

            static string Cell(object? value)
            {
                var s = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
                if (s.Length > 0 && "=+-@".Contains(s[0]))
                    s = "'" + s;
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            }

            await using var reader = await cmd.ExecuteReaderAsync();

            long exportCount = 0;

            while (await reader.ReadAsync())
            {
                var cells = new string[reader.FieldCount];
                for (var index = 0; index < reader.FieldCount; index++)
                    cells[index] = Cell(reader.IsDBNull(index) ? "" : reader.GetValue(index));

                exportCount++;
                csv.AppendLine(string.Join(";", cells));
            }

                        // MC_PROBLEM_FILTER_V5
                        // MC_CARI_ISSUE_TYPE_FILTER_V1
            var allowedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "FATURA_YOK",
                "CARI_HAREKET_YOK",
                "CARI_HESAP_FARKI",
                "PARA_BIRIMI_FARKI",
                "TUTAR_FARKI"
            };

            if (!string.IsNullOrWhiteSpace(issueType) &&
                !allowedTypes.Contains(issueType))
                return BadRequest("Gecersiz sorun turu.");

            if (onlyProblems || !string.IsNullOrWhiteSpace(issueType))
            {
                var lines = csv.ToString().Split(
                    new[] { "\r\n", "\n" },
                    StringSplitOptions.None
                );

                var filtered = new StringBuilder();

                if (lines.Length > 0)
                    filtered.AppendLine(lines[0]);

                foreach (var line in lines.Skip(1))
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var match = System.Text.RegularExpressions.Regex.Match(
                        line,
                        "\"([A-Z_]+)\"\\s*$"
                    );

                    if (!match.Success)
                        continue;

                    var status = match.Groups[1].Value;

                    if (onlyProblems && status == "ESLESEN")
                        continue;

                    if (!string.IsNullOrWhiteSpace(issueType) &&
                        !string.Equals(status, issueType,
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    filtered.AppendLine(line);
                }

                csv.Clear();
                csv.Append(filtered.ToString());
            }
            // MC_CARI_EXPORT_COUNT_RESULT_V2
            var reportLines = csv.ToString().Split(
                new[] { "\r\n", "\n" },
                StringSplitOptions.RemoveEmptyEntries
            );

                        // MC_CARI_EXPORT_COUNT_CHECK_V3
                        // MC_CARI_EXPORT_COUNT_SIMPLIFY_V4
            long actualExportCount = Math.Max(0, reportLines.Length - 1);

            if (actualExportCount != exportCount && !onlyProblems &&
                string.IsNullOrWhiteSpace(issueType))
            {
                return StatusCode(
                    500,
                    "CSV kayit sayisi ile okunan veri sayisi uyusmuyor."
                );
            }

            csv.AppendLine();
            csv.AppendLine("TOPLAM KAYIT;" + actualExportCount);

            if (actualExportCount == 0)
                csv.AppendLine("BILGI;Secilen filtreye uygun kayit bulunamadi.");
            var bytes = new System.Text.UTF8Encoding(true).GetPreamble()
                .Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();

            return File(
                bytes,
                "text/csv; charset=utf-8",
                (onlyProblems ? "Cari_Mutabakat_Sorunlu_" : "Cari_Mutabakat_Detay_") + DateTime.Now.ToString("yyyyMMdd") + ".csv"
            );
        }
        // MC_PURCHASE_INVOICE_DETAIL_LIST_V1
        [HttpGet("InvoiceList")]
        public async Task<IActionResult> InvoiceList()
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
                    COALESCE(i.Currency, ''),
                    CASE
                        WHEN COUNT(p.PurchaseInvoiceId) = 0
                            THEN 'AKTARILMADI'
                        WHEN COUNT(p.PurchaseInvoiceId) > 1
                            THEN 'MUKERRER_AKTARIM'
                        WHEN SUM(
                            CASE WHEN m.Id IS NULL THEN 1 ELSE 0 END
                        ) > 0
                            THEN 'CARI_HAREKET_EKSIK'
                        ELSE 'AKTARILDI'
                    END
                FROM MCPurchaseInvoices i
                LEFT JOIN MCPurchaseInvoiceCariPostings p
                    ON p.PurchaseInvoiceId = i.Id
                LEFT JOIN MCCariMovements m
                    ON m.Id = p.CariMovementId
                GROUP BY
                    i.Id,
                    i.InvoiceNumber,
                    i.SupplierId,
                    i.GrandTotal,
                    i.Currency
                ORDER BY i.Id DESC
                LIMIT 500
                """;

            var html = new StringBuilder();

            html.Append("""
                <section style="padding:20px">
                <h2>Satın Alma Faturaları - Cari Aktarım Durumu</h2>
                <div style="overflow-x:auto">
                <table style="width:100%;border-collapse:collapse;background:white">
                <thead>
                <tr style="background:#24496c;color:white">
                <th style="padding:12px">Detay</th><th style="padding:12px">Fatura ID</th>
                <th style="padding:12px">Fatura No</th>
                <th style="padding:12px">Tedarikçi ID</th>
                <th style="padding:12px">Tutar</th>
                <th style="padding:12px">Para Birimi</th>
                <th style="padding:12px">Cari Durum</th>
                </tr>
                </thead>
                <tbody>
                """);

            await using var reader = await cmd.ExecuteReaderAsync();

            long count = 0;

            while (await reader.ReadAsync())
            {
                count++;

                                html.Append("<tr style='border-bottom:1px solid #e2e8f0'>");

                var invoiceId = reader.GetInt64(0);
                html.Append("<td style='padding:11px'>");
                html.Append("<a href='/MCPurchaseCariAudit/InvoiceDetail/");
                html.Append(invoiceId);
                html.Append("'>Detay</a></td>");

                for (var col = 0; col < reader.FieldCount; col++)
                {
                    var value = reader.IsDBNull(col)
                        ? ""
                        : Convert.ToString(
                            reader.GetValue(col),
                            System.Globalization.CultureInfo.InvariantCulture
                        ) ?? "";

                    html.Append("<td style='padding:11px'>");
                    html.Append(System.Net.WebUtility.HtmlEncode(value));
                    html.Append("</td>");
                }

                html.Append("</tr>");
            }

            if (count == 0)
                html.Append("<tr><td colspan='7' style='padding:20px'>Henüz satın alma faturası bulunmuyor.</td></tr>");

            html.Append("""
                </tbody>
                </table>
                </div>
                <p style="margin-top:15px;color:#64748b">
                En fazla son 500 fatura gösterilmektedir.
                </p>
                </section>
                """);

            return View(
                "~/Views/MCPurchaseCariAudit/Index.cshtml",
                html.ToString()
            );
        }
        [HttpGet]
                // MC_PURCHASE_CARI_DETAIL_V1
        [HttpGet("InvoiceDetail/{id:long}")]
        public async Task<IActionResult> InvoiceDetail(long id)
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
                    i.InvoiceNumber,
                    i.SupplierId,
                    i.GrandTotal,
                    i.Currency,
                    p.CariAccountId,
                    p.CariMovementId,
                    m.Debit,
                    m.Credit,
                    m.Currency
                FROM MCPurchaseInvoices i
                LEFT JOIN MCPurchaseInvoiceCariPostings p
                    ON p.PurchaseInvoiceId = i.Id
                LEFT JOIN MCCariMovements m
                    ON m.Id = p.CariMovementId
                WHERE i.Id = $id
                """;

            cmd.Parameters.AddWithValue("$id", id);

            var html = new StringBuilder();
            html.Append("<section style='padding:20px'>");
            html.Append("<h2>Fatura ve Cari Hareket Detayı</h2>");
            html.Append("<p><a href='/MCPurchaseCariAudit/InvoiceList'>← Fatura Listesine Dön</a></p>");
            html.Append("<table style='width:100%;border-collapse:collapse;background:white'>");

            var headers = new[]
            {
                "Fatura ID", "Fatura No", "Tedarikçi ID",
                "Fatura Tutarı", "Fatura Para Birimi",
                "Cari Hesap ID", "Cari Hareket ID",
                "Borç", "Alacak", "Cari Para Birimi"
            };

            html.Append("<thead><tr style='background:#24496c;color:white'>");

            foreach (var header in headers)
            {
                html.Append("<th style='padding:10px'>");
                html.Append(System.Net.WebUtility.HtmlEncode(header));
                html.Append("</th>");
            }

            html.Append("</tr></thead><tbody>");

            await using var reader = await cmd.ExecuteReaderAsync();

            var found = false;

            while (await reader.ReadAsync())
            {
                found = true;
                html.Append("<tr style='border-bottom:1px solid #ddd'>");

                for (var index = 0; index < reader.FieldCount; index++)
                {
                    var value = reader.IsDBNull(index)
                        ? "—"
                        : Convert.ToString(
                            reader.GetValue(index),
                            System.Globalization.CultureInfo.InvariantCulture
                        ) ?? "";

                    html.Append("<td style='padding:10px'>");
                    html.Append(System.Net.WebUtility.HtmlEncode(value));
                    html.Append("</td>");
                }

                html.Append("</tr>");
            }

            if (!found)
                return NotFound("Fatura bulunamadi.");

            html.Append("</tbody></table></section>");

            return View(
                "~/Views/MCPurchaseCariAudit/Index.cshtml",
                html.ToString()
            );
        }
public async Task<IActionResult> Index()
        {
            var db = Path.Combine(_env.ContentRootPath, "app.db");

            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = db,
                Mode = SqliteOpenMode.ReadOnly
            };

            await using var connection =
                new SqliteConnection(builder.ToString());

            await connection.OpenAsync();

            var sql = """
                SELECT
                    (SELECT COUNT(*) FROM MCPurchaseInvoices) AS TotalInvoices,
                    (SELECT COUNT(*) FROM MCPurchaseInvoiceCariPostings) AS TotalPostings,
                    (
                        SELECT COUNT(*)
                        FROM MCPurchaseInvoices i
                        WHERE NOT EXISTS (
                            SELECT 1
                            FROM MCPurchaseInvoiceCariPostings p
                            WHERE p.PurchaseInvoiceId = i.Id
                        )
                    ) AS PendingInvoices,
                    (
                        SELECT COUNT(*)
                        FROM (
                            SELECT PurchaseInvoiceId
                            FROM MCPurchaseInvoiceCariPostings
                            GROUP BY PurchaseInvoiceId
                            HAVING COUNT(*) > 1
                        )
                    ) AS DuplicatePostings,
                    (
                        SELECT COUNT(*)
                        FROM MCPurchaseInvoiceCariPostings p
                        LEFT JOIN MCCariMovements m
                            ON m.Id = p.CariMovementId
                        WHERE m.Id IS NULL
                    ) AS MissingMovements
                """;

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;

            await using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return StatusCode(500, "Mutabakat bilgileri okunamadi.");

                        // MC_CARI_AMOUNT_AUDIT_V1
            // Ayri baglanti: ilk veri okuyucusu acikken cakisma olusmaz.
            long amountMismatch = 0;

            await using (var amountConnection =
                new SqliteConnection(builder.ToString()))
            {
                await amountConnection.OpenAsync();

                await using var amountCmd =
                    amountConnection.CreateCommand();

                amountCmd.CommandText = """
                    SELECT COUNT(*)
                    FROM MCPurchaseInvoiceCariPostings p
                    JOIN MCPurchaseInvoices i
                      ON i.Id = p.PurchaseInvoiceId
                    JOIN MCCariMovements m
                      ON m.Id = p.CariMovementId
                    WHERE i.GrandTotal IS NULL
                       OR m.Debit IS NULL
                       OR m.Credit IS NULL
                       OR ABS(
                           ABS(CAST(i.GrandTotal AS REAL)) -
                           ABS(
                               CAST(m.Debit AS REAL) -
                               CAST(m.Credit AS REAL)
                           )
                       ) > 0.01
                    """;

                amountMismatch = Convert.ToInt64(
                    await amountCmd.ExecuteScalarAsync()
                );
            }
            // MC_CARI_CURRENCY_AUDIT_V1
            long currencyMismatch = 0;

            await using (var currencyConnection =
                new SqliteConnection(builder.ToString()))
            {
                await currencyConnection.OpenAsync();

                await using var currencyCmd = currencyConnection.CreateCommand();

                currencyCmd.CommandText = """
                    SELECT COUNT(*)
                    FROM MCPurchaseInvoiceCariPostings p
                    JOIN MCPurchaseInvoices i
                      ON i.Id = p.PurchaseInvoiceId
                    JOIN MCCariMovements m
                      ON m.Id = p.CariMovementId
                    WHERE NULLIF(TRIM(i.Currency), '') IS NULL
                       OR NULLIF(TRIM(m.Currency), '') IS NULL
                       OR UPPER(TRIM(i.Currency)) <>
                          UPPER(TRIM(m.Currency))
                    """;

                currencyMismatch = Convert.ToInt64(
                    await currencyCmd.ExecuteScalarAsync()
                );
            }
            // MC_CARI_ACCOUNT_AUDIT_V1
            long accountMismatch = 0;

            await using (var accountConnection =
                new SqliteConnection(builder.ToString()))
            {
                await accountConnection.OpenAsync();

                await using var accountCmd =
                    accountConnection.CreateCommand();

                accountCmd.CommandText = """
                    SELECT COUNT(*)
                    FROM MCPurchaseInvoiceCariPostings p
                    JOIN MCCariMovements m
                      ON m.Id = p.CariMovementId
                    WHERE p.CariAccountId IS NULL
                       OR m.CariAccountId IS NULL
                       OR p.CariAccountId <> m.CariAccountId
                    """;

                accountMismatch = Convert.ToInt64(
                    await accountCmd.ExecuteScalarAsync()
                );
            }
var totalInvoices = reader.GetInt64(0);
            var totalPostings = reader.GetInt64(1);
            var pending = reader.GetInt64(2);
            var duplicate = reader.GetInt64(3);
            var missing = reader.GetInt64(4);

            var html = new StringBuilder();

                        // MC_CARI_AUDIT_NATIVE_LAYOUT_V2
            html.Append("""
                <section class="mc-cari-audit">
                <style>
                .mc-cari-audit{padding:18px;color:#20344a}
                .mc-cari-audit h2{font-size:22px;margin:0 0 20px}
                .mc-cari-audit .grid{
                    display:grid;
                    grid-template-columns:repeat(auto-fit,minmax(170px,1fr));
                    gap:14px
                }
                .mc-cari-audit .card{
                    background:#fff;
                    padding:20px;
                    border:1px solid #e1e7ef;
                    border-radius:12px
                }
                .mc-cari-audit .number{
                    font-size:30px;
                    font-weight:700;
                    margin-top:12px
                }
                .mc-cari-audit .actions{
                    display:flex;
                    flex-wrap:wrap;
                    gap:10px;
                    margin-top:22px
                }
                .mc-cari-audit .actions a{
                    display:inline-block;
                    padding:11px 16px;
                    background:#24496c;
                    color:white;
                    text-decoration:none;
                    border-radius:8px
                }
                .mc-cari-audit .note{
                    color:#64748b;
                    margin-top:18px
                }
                </style>
                <h2>Cari Mutabakat Kontrolü</h2>
                <div class="grid">
                """);
void Card(string title, long value)
            {
                html.Append("<div class='card'><div>");
                html.Append(System.Net.WebUtility.HtmlEncode(title));
                html.Append("</div><div class='number'>");
                html.Append(value);
                html.Append("</div></div>");
            }

            Card("Toplam Fatura", totalInvoices);
            Card("Cari Aktarım", totalPostings);
            Card("Aktarılmamış Fatura", pending);
            Card("Mükerrer Aktarım", duplicate);
            Card("Eksik Cari Hareket", missing);
            Card("Tutar Farki", amountMismatch);
            Card("Para Birimi Farki", currencyMismatch);
            Card("Cari Hesap Farki", accountMismatch);

            // MC_CARI_AUDIT_SUMMARY_V1
                        // MC_CARI_AUDIT_CONSISTENCY_V1
            var postedInvoices = totalInvoices - pending;

            var countMismatch =
                totalInvoices < 0 ||
                pending < 0 ||
                postedInvoices < 0 ||
                postedInvoices + pending != totalInvoices;

            var auditState = totalInvoices == 0
                ? "Henüz fatura kaydı bulunmuyor. Gerçek mutabakat yapılamadı."
                : countMismatch
                    ? "Fatura sayılarında tutarsızlık var."
                    : duplicate > 0 || missing > 0
                        ? "Cari aktarım kayıtlarında kontrol gerektiren durum var."
                        : pending > 0
                            ? "Cari aktarımı bekleyen faturalar var."
                            : "Fatura aktarım sayıları tutarlı. Tutar mutabakatı ayrıca kontrol edilmelidir.";

            html.Append("<div class='mc-audit-status' style='margin-top:15px;background:#edf3f9;color:#24496c;border:1px solid #cbd9e8'>");
            html.Append(System.Net.WebUtility.HtmlEncode(auditState));
            html.Append("</div>");

            html.Append("<div class='mc-audit-summary'>");
            html.Append("<strong>Genel Fatura Durumu</strong>");
            html.Append("<div class='mc-audit-summary-grid'>");

            html.Append("<span>Toplam Fatura: <b>");
            html.Append(totalInvoices);
            html.Append("</b></span>");

            html.Append("<span>Aktarılmış Fatura: <b>");
            html.Append(postedInvoices);
            html.Append("</b></span>");

            html.Append("<span>Bekleyen Fatura: <b>");
            html.Append(pending);
            html.Append("</b></span>");

            html.Append("</div></div>");

            html.Append("""
                <style>
                .mc-audit-summary{
                    margin-top:18px;
                    padding:18px;
                    background:#f1f6fc;
                    border:1px solid #d2e0ee;
                    border-radius:10px;
                    color:#24496c;
                }
                .mc-audit-summary-grid{
                    display:flex;
                    flex-wrap:wrap;
                    gap:22px;
                    margin-top:12px;
                }
                .mc-audit-summary-grid span{
                    font-size:14px;
                }
                </style>
                """);

            // MC_CARI_AUDIT_STATUS_V1
                        html.Append("""
                <style>
                .mc-audit-status{
                    margin-top:18px;
                    padding:15px 18px;
                    border-radius:9px;
                    font-weight:700;
                }
                .mc-audit-success{
                    background:#e8f6ee;
                    color:#17653b;
                    border:1px solid #b7e2c7;
                }
                .mc-audit-warning{
                    background:#fff2df;
                    color:#965700;
                    border:1px solid #f2d09a;
                }
                </style>
                """);
                        // MC_CARI_PENDING_STATUS_V1
            var hasIssues = duplicate > 0 || missing > 0 || amountMismatch > 0 || currencyMismatch > 0 || accountMismatch > 0;

            var invoiceStatus = totalInvoices == 0
                ? "Henüz satın alma faturası bulunmuyor."
                : pending > 0
                    ? $"{pending} adet fatura cari hesaba aktarılmayı bekliyor."
                    : "Bütün faturalar için cari aktarım kaydı mevcut.";

            html.Append("<div class='mc-audit-status' style='background:#eef4fb;color:#24496c;border:1px solid #cadced'>");
            html.Append(System.Net.WebUtility.HtmlEncode(invoiceStatus));
            html.Append("</div>");

            html.Append(
                hasIssues
                ? "<div class='mc-audit-status mc-audit-warning'>Kontrol Gerekli: Cari aktarimlarda tutarsizlik tespit edildi.</div>"
                : "<div class='mc-audit-status mc-audit-success'>Aktarim kayitlarinda mukerrerlik veya eksik hareket tespit edilmedi.</div>"
            );

            html.Append("""
                </div>
                <div class="actions">
                    <a href="/MCPurchaseInvoiceStatus">Fatura Kontrol Merkezi</a>
                    <a href="/MCPurchaseCariExport">Excel Raporu</a>
                    <a href="/MCPurchaseCariAudit/InvoiceList">Fatura Aktarım Listesi</a>
                    <a href="/MCPurchaseCariAudit/DetailsCsv">Detaylı Mutabakat Excel</a>
                    <a href="/MCPurchaseCariAudit/DetailsCsv?onlyProblems=true">Sorunlu Kayıtları Excel'e Aktar</a>
                </div>
                <p class="note">Bilgiler veritabanından salt okunur alınmaktadır.</p>
                </section>
                """);

                        // MC_CARI_AUDIT_DEDICATED_VIEW_V1
            return View(
                "~/Views/MCPurchaseCariAudit/Index.cshtml",
                html.ToString()
            );
        }
    }
}