using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using MasterChefTakip.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace MasterChefTakip.Controllers;

[Authorize(Roles = AppRoles.Admin)]
// MC_FINANCE_CACHE_SECURITY_V1
[ResponseCache(
    Duration = 0,
    Location = ResponseCacheLocation.None,
    NoStore = true)]
// MC_VIRMAN_SERVER_DATE_VALIDATION_V1
[MCVirmanDateRange]
[Route("MCBankCashAudit")]
public sealed class MCBankCashAuditController : Controller
{
    private readonly IConfiguration _configuration;

    public MCBankCashAuditController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? start, string? end, string? status, string? q)
    {
        // MC_VIRMAN_STATUS_FILTER_V2
        var filter = (status ?? "all").Trim().ToLowerInvariant();

        if (filter != "all" &&
            filter != "matched" &&
            filter != "issues")
            return BadRequest("Durum filtresi geçersiz.");

        // MC_VIRMAN_SEARCH_V1
        var search = (q ?? "").Trim();

        if (search.Length > 100)
            return BadRequest("Arama metni en fazla 100 karakter olabilir.");
        var today = DateTime.Today;

        start ??= new DateTime(today.Year, today.Month, 1)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        end ??= today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (!DateTime.TryParseExact(
                start, "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var first))
            return BadRequest("Başlangıç tarihi geçersiz.");

        if (!DateTime.TryParseExact(
                end, "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var last))
            return BadRequest("Bitiş tarihi geçersiz.");

        if (first > last)
            return BadRequest("Tarih aralığı geçersiz.");

        var page = new MCVirmanAuditPage
        {
            Start = start,
            End = end, FilterStatus = filter, Search = search
        };

        var cs = _configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(cs))
            return StatusCode(500, "Veritabanı bağlantısı bulunamadı.");

        var builder = new SqliteConnectionStringBuilder(cs)
        {
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        };

        await using var db = new SqliteConnection(builder.ToString());
        await db.OpenAsync();

        await using (var check = db.CreateCommand())
        {
            check.CommandText = @"
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type = 'table'
                  AND name IN (
                    'MCBankCashAccounts',
                    'MCBankCashMovements'
                  )";

            if (Convert.ToInt64(await check.ExecuteScalarAsync()) != 2)
                return View(page);
        }

        var movements = new List<MCVirmanAuditMovement>();

        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT
                    m.Id,
                    m.MovementDate,
                    m.AccountId,
                    a.Name,
                    m.Direction,
                    m.Amount,
                    m.RequestToken
                FROM MCBankCashMovements m
                INNER JOIN MCBankCashAccounts a
                    ON a.Id = m.AccountId
                WHERE m.RequestToken LIKE 'VIR-%'
                ORDER BY m.MovementDate DESC, m.Id DESC";

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var amountText = Convert.ToString(
                    reader.GetValue(5),
                    CultureInfo.InvariantCulture
                );

                if (!decimal.TryParse(
                        amountText,
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out var amount))
                {
                    return StatusCode(
                        500,
                        "Virman hareketinde geçersiz tutar bulundu."
                    );
                }

                movements.Add(new MCVirmanAuditMovement
                {
                    Id = reader.GetInt64(0),
                    Date = reader.GetString(1),
                    AccountId = reader.GetInt64(2),
                    Account = reader.GetString(3),
                    Direction = reader.GetString(4),
                    Amount = amount,
                    Token = reader.IsDBNull(6)
                        ? ""
                        : reader.GetString(6)
                });
            }
        }

        page.Movements.AddRange(movements);

        foreach (var group in movements.GroupBy(
            x => NormalizeToken(x.Token),
            StringComparer.OrdinalIgnoreCase))
        {
            var legs = group.ToList();

            if (!legs.Any(x =>
            {
                var date = x.Date.Length >= 10
                    ? x.Date.Substring(0, 10)
                    : x.Date;

                return string.CompareOrdinal(date, start) >= 0
                    && string.CompareOrdinal(date, end) <= 0;
            }))
                continue;

            var incoming = legs
                .Where(x => x.Direction == "In")
                .ToList();

            var outgoing = legs
                .Where(x => x.Direction == "Out")
                .ToList();

            var inTotal = incoming.Sum(x => x.Amount);
            var outTotal = outgoing.Sum(x => x.Amount);

            string issue;

            if (incoming.Count == 0 || outgoing.Count == 0)
                issue = "Eksik virman kaydı";
            else if (legs.Count != 2 ||
                     incoming.Count != 1 ||
                     outgoing.Count != 1)
                issue = "Tekrarlanan veya fazla hareket";
            else if (inTotal <= 0 || outTotal <= 0)
                issue = "Geçersiz virman tutarı";
            else if (inTotal != outTotal)
                issue = "Giriş ve çıkış tutarı farklı";
            else if (incoming[0].AccountId == outgoing[0].AccountId)
                issue = "Kaynak ve hedef hesap aynı";
            else if (legs.Any(x =>
                (x.Token.EndsWith(":IN",
                    StringComparison.OrdinalIgnoreCase)
                    && x.Direction != "In")
                ||
                (x.Token.EndsWith(":OUT",
                    StringComparison.OrdinalIgnoreCase)
                    && x.Direction != "Out")))
                issue = "Virman işlem yönü uyuşmuyor";
            else
                issue = "";

            page.Items.Add(new MCVirmanAuditItem
            {
                Token = group.Key,
                Date = legs.Max(x => x.Date) ?? "",
                Source = outgoing.Count > 0
                    ? string.Join(", ", outgoing.Select(x => x.Account).Distinct())
                    : "Bulunamadı",
                Target = incoming.Count > 0
                    ? string.Join(", ", incoming.Select(x => x.Account).Distinct())
                    : "Bulunamadı",
                InCount = incoming.Count,
                OutCount = outgoing.Count,
                TotalIn = inTotal,
                TotalOut = outTotal,
                Issue = issue
            });
        }

        page.Items = page.Items
            .OrderBy(x => x.IsMatched)
            .ThenByDescending(x => x.Date)
            .ToList();

        return View(page);
    }

    // MC_VIRMAN_AUDIT_CSV_V1
    [HttpGet("Export")]
    public async Task<IActionResult> Export(string? start, string? end, string? status, string? q)
    {
        var result = await Index(start, end, status, q);

        if (result is not ViewResult { Model: MCVirmanAuditPage page })
            return result;

        static string Cell(string? input)
        {
            var value = input ?? "";
            var check = value.TrimStart();

            if (check.Length > 0 &&
                (check[0] == '=' ||
                 check[0] == '+' ||
                 check[0] == '-' ||
                 check[0] == '@'))
            {
                value = "'" + value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        var tr = CultureInfo.GetCultureInfo("tr-TR");
        var csv = new System.Text.StringBuilder();

        csv.AppendLine("sep=;");
        csv.AppendLine("MASTER CHEF - VİRMAN MUTABAKAT RAPORU");
        csv.AppendLine("Başlangıç;" + Cell(page.Start));
        csv.AppendLine("Bitiş;" + Cell(page.End));

        csv.AppendLine(
            "Toplam virman;" +
            page.Items.Count.ToString(CultureInfo.InvariantCulture));

        csv.AppendLine(
            "Eşleşen virman;" +
            page.Matched.ToString(CultureInfo.InvariantCulture));

        csv.AppendLine(
            "Kontrol gerektiren;" +
            page.Problematic.ToString(CultureInfo.InvariantCulture));

        csv.AppendLine(
            "Eşleşen virman tutarı;" +
            page.MatchedAmount.ToString("0.00", tr));

        csv.AppendLine(
            "Durum filtresi;" +
            Cell(page.FilterStatus == "matched" ? "Eşleşen" :
                 page.FilterStatus == "issues" ? "Kontrol Gerektiren" :
                 "Tümü"));

        csv.AppendLine(
            "Listelenen virman;" +
            page.SearchedItems.Count.ToString(CultureInfo.InvariantCulture));

        // MC_VIRMAN_FILTERED_CSV_TOTALS_V1
        var mcSelectedVirmanRows = page.SearchedItems;

        var mcSelectedVirmanIn =
            mcSelectedVirmanRows.Sum(x => x.TotalIn);

        var mcSelectedVirmanOut =
            mcSelectedVirmanRows.Sum(x => x.TotalOut);

        var mcSelectedVirmanDifference =
            mcSelectedVirmanIn - mcSelectedVirmanOut;

        var mcSelectedVirmanIssues =
            mcSelectedVirmanRows.Count(x => !x.IsMatched);

        var mcSelectedVirmanAbsoluteDifference =
            mcSelectedVirmanRows
                .Where(x => !x.IsMatched)
                .Sum(x => Math.Abs(x.TotalIn - x.TotalOut));

        csv.AppendLine(
            "Arama;" + Cell(page.Search));

        csv.AppendLine(
            "Seçili Virman Sayısı;" +
            mcSelectedVirmanRows.Count.ToString(
                CultureInfo.InvariantCulture));

        csv.AppendLine(
            "Seçili Toplam Para Girişi;" +
            mcSelectedVirmanIn.ToString("0.00", tr));

        csv.AppendLine(
            "Seçili Toplam Para Çıkışı;" +
            mcSelectedVirmanOut.ToString("0.00", tr));

        csv.AppendLine(
            "Seçili Net Hareket Farkı;" +
            mcSelectedVirmanDifference.ToString("0.00", tr));

        csv.AppendLine(
            "Seçili Kontrol Gerektiren Virman;" +
            mcSelectedVirmanIssues.ToString(
                CultureInfo.InvariantCulture));

        csv.AppendLine(
            "Sorunlu Virmanların Mutlak Tutar Farkı;" +
            mcSelectedVirmanAbsoluteDifference.ToString("0.00", tr));


        csv.AppendLine();

        csv.AppendLine(
            "Tarih;Virman Kodu;Çıkış Hesabı;Giriş Hesabı;" +
            "Çıkış Tutarı;Giriş Tutarı;Tutar Farkı;Çıkış Hareketi;" +
            "Giriş Hareketi;Durum");

        foreach (var item in page.SearchedItems)
        {
            csv.AppendLine(string.Join(";", new[]
            {
                Cell(item.Date),
                Cell(item.Token),
                Cell(item.Source),
                Cell(item.Target),
                item.TotalOut.ToString("0.00", tr),
                item.TotalIn.ToString("0.00", tr),
                // MC_VIRMAN_ROW_DIFFERENCE_V1
                (item.TotalIn - item.TotalOut).ToString("0.00", tr),
                item.OutCount.ToString(CultureInfo.InvariantCulture),
                item.InCount.ToString(CultureInfo.InvariantCulture),
                Cell(item.IsMatched ? "Eşleşti" : item.Issue)
            }));
        }

        var encoding = new System.Text.UTF8Encoding(true);

        var bytes = encoding.GetPreamble()
            .Concat(encoding.GetBytes(csv.ToString()))
            .ToArray();

        var fileName =
            $"MASTER_CHEF_Virman_Mutabakat_{page.Start}_{page.End}.csv";

        return File(
            bytes,
            "text/csv; charset=utf-8",
            fileName);
    }

    // MC_VIRMAN_DETAILS_V1
    [HttpGet("Details")]
    public async Task<IActionResult> Details(
        string? token,
        string? start,
        string? end,
        string? status,
        string? q)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 200)
            return BadRequest("Virman kodu geçersiz.");

        var response = await Index(start, end, status, q);

        if (response is not ViewResult
            {
                Model: MCVirmanAuditPage page
            })
            return response;

        var normalized = NormalizeToken(token);

        var audit = page.Items.FirstOrDefault(x =>
            string.Equals(
                x.Token,
                normalized,
                StringComparison.OrdinalIgnoreCase));

        if (audit is null)
            return NotFound("Virman bulunamadı.");

        var movements = page.Movements
            .Where(x =>
                string.Equals(
                    NormalizeToken(x.Token),
                    normalized,
                    StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Id)
            .ToList();

        var model = new MCVirmanAuditDetailsPage
        {
            Item = audit,
            Movements = movements,
            Start = page.Start,
            End = page.End,
            Status = page.FilterStatus,
            Search = page.Search
        };

        return View("Details", model);
    }
    // MC_VIRMAN_DETAIL_EXPORT_PRINT_V1
    [HttpGet("DetailsExport")]
    public async Task<IActionResult> DetailsExport(
        string? token,
        string? start,
        string? end,
        string? status,
        string? q)
    {
        var response = await Details(token, start, end, status, q);

        if (response is not ViewResult
            {
                Model: MCVirmanAuditDetailsPage model
            })
            return response;

        static string Cell(string? input)
        {
            var value = input ?? "";
            var trimmed = value.TrimStart();

            if (trimmed.Length > 0 &&
                (trimmed[0] == '=' ||
                 trimmed[0] == '+' ||
                 trimmed[0] == '-' ||
                 trimmed[0] == '@'))
                value = "'" + value;

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        var tr = CultureInfo.GetCultureInfo("tr-TR");
        var csv = new System.Text.StringBuilder();

        csv.AppendLine("sep=;");
        csv.AppendLine("MASTER CHEF - VİRMAN HAREKET DETAYLARI");

        csv.AppendLine("Virman Kodu;" + Cell(model.Item.Token));
        csv.AppendLine("Başlangıç;" + Cell(model.Start));
        csv.AppendLine("Bitiş;" + Cell(model.End));

        csv.AppendLine(
            "Durum;" +
            Cell(model.Item.IsMatched ? "Eşleşti" : model.Item.Issue));

        csv.AppendLine(
            "Çıkış Hesabı;" + Cell(model.Item.Source));

        csv.AppendLine(
            "Giriş Hesabı;" + Cell(model.Item.Target));

        csv.AppendLine(
            "Toplam Çıkış;" +
            model.Item.TotalOut.ToString("0.00", tr));

        csv.AppendLine(
            "Toplam Giriş;" +
            model.Item.TotalIn.ToString("0.00", tr));

        csv.AppendLine(
            "Fark;" +
            (model.Item.TotalIn - model.Item.TotalOut)
                .ToString("0.00", tr));

        csv.AppendLine();

        csv.AppendLine(
            "Hareket No;Tarih;Hesap;İşlem;Tutar;Kayıt Kodu");

        foreach (var movement in model.Movements)
        {
            csv.AppendLine(string.Join(";", new[]
            {
                movement.Id.ToString(CultureInfo.InvariantCulture),
                Cell(movement.Date),
                Cell(movement.Account),
                Cell(movement.Direction == "In"
                    ? "Para Girişi"
                    : "Para Çıkışı"),
                movement.Amount.ToString("0.00", tr),
                Cell(movement.Token)
            }));
        }

        var encoding = new System.Text.UTF8Encoding(true);

        var bytes = encoding.GetPreamble()
            .Concat(encoding.GetBytes(csv.ToString()))
            .ToArray();

        var fileName =
            $"MASTER_CHEF_Virman_Detay_{DateTime.Now:yyyyMMdd_HHmmss}.csv";

        return File(
            bytes,
            "text/csv; charset=utf-8",
            fileName);
    }
    // MC_VIRMAN_ISSUE_EXPORT_V1
    [HttpGet("IssuesExport")]
    public async Task<IActionResult> IssuesExport(
        string? start,
        string? end,
        string? status,
        string? q)
    {
        var response = await Index(start, end, status, q);

        if (response is not ViewResult
            {
                Model: MCVirmanAuditPage page
            })
            return response;

        static string Cell(string? input)
        {
            var value = input ?? "";
            var trimmed = value.TrimStart();

            if (trimmed.Length > 0 &&
                (trimmed[0] == '=' ||
                 trimmed[0] == '+' ||
                 trimmed[0] == '-' ||
                 trimmed[0] == '@'))
            {
                value = "'" + value;
            }

            return "\"" +
                value.Replace("\"", "\"\"") +
                "\"";
        }

        var tr = CultureInfo.GetCultureInfo("tr-TR");

        var issues = page.SearchedItems
            .Where(x => !x.IsMatched)
            .OrderByDescending(x =>
                Math.Abs(x.TotalIn - x.TotalOut))
            .ThenByDescending(x => x.Date)
            .ToList();

        var groups = issues
            .GroupBy(x => x.Issue)
            .Select(g => new
            {
                Reason = g.Key,
                Count = g.Count(),
                TotalIn = g.Sum(x => x.TotalIn),
                TotalOut = g.Sum(x => x.TotalOut),
                Difference = g.Sum(x =>
                    Math.Abs(x.TotalIn - x.TotalOut))
            })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Reason)
            .ToList();

        var csv = new System.Text.StringBuilder();

        csv.AppendLine("sep=;");
        csv.AppendLine("MASTER CHEF - VİRMAN HATA ANALİZİ");
        csv.AppendLine();

        csv.AppendLine("Başlangıç;" + Cell(page.Start));
        csv.AppendLine("Bitiş;" + Cell(page.End));
        csv.AppendLine("Arama;" + Cell(page.Search));

        csv.AppendLine(
            "Durum Filtresi;" +
            Cell(page.FilterStatus));

        csv.AppendLine(
            "Kontrol Gerektiren Virman;" +
            issues.Count.ToString(CultureInfo.InvariantCulture));

        csv.AppendLine(
            "Sorunlu Virman Giriş Toplamı;" +
            issues.Sum(x => x.TotalIn).ToString("0.00", tr));

        csv.AppendLine(
            "Sorunlu Virman Çıkış Toplamı;" +
            issues.Sum(x => x.TotalOut).ToString("0.00", tr));

        csv.AppendLine(
            "Toplam Mutlak Tutar Farkı;" +
            issues.Sum(x =>
                Math.Abs(x.TotalIn - x.TotalOut))
                .ToString("0.00", tr));

        csv.AppendLine();
        csv.AppendLine("HATA NEDENLERİ ÖZETİ");

        csv.AppendLine(
            "Hata Nedeni;Virman Sayısı;Giriş Toplamı;" +
            "Çıkış Toplamı;Mutlak Tutar Farkı");

        foreach (var group in groups)
        {
            csv.AppendLine(string.Join(";", new[]
            {
                Cell(group.Reason),
                group.Count.ToString(CultureInfo.InvariantCulture),
                group.TotalIn.ToString("0.00", tr),
                group.TotalOut.ToString("0.00", tr),
                group.Difference.ToString("0.00", tr)
            }));
        }

        csv.AppendLine();
        csv.AppendLine("SORUNLU VİRMAN DETAYLARI");

        csv.AppendLine(
            "Tarih;Virman Kodu;Çıkış Hesabı;Giriş Hesabı;" +
            "Çıkış Tutarı;Giriş Tutarı;Tutar Farkı;" +
            "Çıkış Hareketi;Giriş Hareketi;Hata Nedeni");

        foreach (var item in issues)
        {
            csv.AppendLine(string.Join(";", new[]
            {
                Cell(item.Date),
                Cell(item.Token),
                Cell(item.Source),
                Cell(item.Target),
                item.TotalOut.ToString("0.00", tr),
                item.TotalIn.ToString("0.00", tr),
                (item.TotalIn - item.TotalOut).ToString("0.00", tr),
                item.OutCount.ToString(CultureInfo.InvariantCulture),
                item.InCount.ToString(CultureInfo.InvariantCulture),
                Cell(item.Issue)
            }));
        }

        csv.AppendLine();

        csv.AppendLine(
            Cell("Not: Tutar farkının sıfır olması " +
                 "virmanın hatasız olduğu anlamına gelmez."));

        var encoding = new System.Text.UTF8Encoding(true);

        var bytes = encoding.GetPreamble()
            .Concat(encoding.GetBytes(csv.ToString()))
            .ToArray();

        var fileName =
            $"MASTER_CHEF_Virman_Hata_Analizi_{page.Start}_{page.End}.csv";

        return File(
            bytes,
            "text/csv; charset=utf-8",
            fileName);
    }
    // MC_VIRMAN_MONTHLY_EXPORT_V1
    [HttpGet("MonthlyExport")]
    public async Task<IActionResult> MonthlyExport(
        string? start,
        string? end,
        string? status,
        string? q)
    {
        var response = await Index(start, end, status, q);

        if (response is not ViewResult
            {
                Model: MCVirmanAuditPage page
            })
            return response;

        static string Cell(string? input)
        {
            var value = input ?? "";
            var check = value.TrimStart();

            if (check.Length > 0 &&
                (check[0] == '=' ||
                 check[0] == '+' ||
                 check[0] == '-' ||
                 check[0] == '@'))
            {
                value = "'" + value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        var tr = CultureInfo.GetCultureInfo("tr-TR");

        var months = page.SearchedItems
            .GroupBy(x =>
                x.Date.Length >= 7
                    ? x.Date.Substring(0, 7)
                    : "Tarihsiz")
            .Select(g => new
            {
                Month = g.Key,
                Count = g.Count(),
                Matched = g.Count(x => x.IsMatched),
                Issues = g.Count(x => !x.IsMatched),
                TotalIn = g.Sum(x => x.TotalIn),
                TotalOut = g.Sum(x => x.TotalOut),
                Difference = g.Sum(x =>
                    Math.Abs(x.TotalIn - x.TotalOut))
            })
            .OrderByDescending(x => x.Month)
            .ToList();

        var csv = new System.Text.StringBuilder();

        csv.AppendLine("sep=;");
        csv.AppendLine("MASTER CHEF - AYLIK VİRMAN MUTABAKAT RAPORU");
        csv.AppendLine();

        csv.AppendLine("Başlangıç;" + Cell(page.Start));
        csv.AppendLine("Bitiş;" + Cell(page.End));
        csv.AppendLine("Arama;" + Cell(page.Search));

        csv.AppendLine(
            "Durum Filtresi;" +
            Cell(page.FilterStatus == "matched"
                ? "Eşleşen"
                : page.FilterStatus == "issues"
                    ? "Kontrol Gerektiren"
                    : "Tümü"));

        csv.AppendLine();

        csv.AppendLine(
            "Toplam Virman;" +
            months.Sum(x => x.Count)
                .ToString(CultureInfo.InvariantCulture));

        csv.AppendLine(
            "Eşleşen Virman;" +
            months.Sum(x => x.Matched)
                .ToString(CultureInfo.InvariantCulture));

        csv.AppendLine(
            "Sorunlu Virman;" +
            months.Sum(x => x.Issues)
                .ToString(CultureInfo.InvariantCulture));

        csv.AppendLine();

        csv.AppendLine(
            "Ay;Virman Sayısı;Eşleşen;Sorunlu;" +
            "Para Girişi;Para Çıkışı;Net Fark;" +
            "Mutlak Tutar Farkı");

        foreach (var month in months)
        {
            csv.AppendLine(string.Join(";", new[]
            {
                Cell(month.Month),
                month.Count.ToString(CultureInfo.InvariantCulture),
                month.Matched.ToString(CultureInfo.InvariantCulture),
                month.Issues.ToString(CultureInfo.InvariantCulture),
                month.TotalIn.ToString("0.00", tr),
                month.TotalOut.ToString("0.00", tr),
                (month.TotalIn - month.TotalOut).ToString("0.00", tr),
                month.Difference.ToString("0.00", tr)
            }));
        }

        csv.AppendLine();

        csv.AppendLine(string.Join(";", new[]
        {
            "TOPLAM",
            months.Sum(x => x.Count).ToString(CultureInfo.InvariantCulture),
            months.Sum(x => x.Matched).ToString(CultureInfo.InvariantCulture),
            months.Sum(x => x.Issues).ToString(CultureInfo.InvariantCulture),
            months.Sum(x => x.TotalIn).ToString("0.00", tr),
            months.Sum(x => x.TotalOut).ToString("0.00", tr),
            (months.Sum(x => x.TotalIn) -
             months.Sum(x => x.TotalOut)).ToString("0.00", tr),
            months.Sum(x => x.Difference).ToString("0.00", tr)
        }));

        csv.AppendLine();

        csv.AppendLine(
            Cell("Virmanlar şirket geliri veya gideri değildir."));

        csv.AppendLine(
            Cell("Tutar farkının sıfır olması bütün " +
                 "virmanların hatasız olduğu anlamına gelmez."));

        var encoding = new System.Text.UTF8Encoding(true);

        var bytes = encoding.GetPreamble()
            .Concat(encoding.GetBytes(csv.ToString()))
            .ToArray();

        var fileName =
            $"MASTER_CHEF_Aylik_Virman_{page.Start}_{page.End}.csv";

        return File(
            bytes,
            "text/csv; charset=utf-8",
            fileName);
    }
    // MC_VIRMAN_PAIR_EXPORT_V1
    [HttpGet("PairsExport")]
    public async Task<IActionResult> PairsExport(
        string? start,
        string? end,
        string? status,
        string? q)
    {
        var response = await Index(start, end, status, q);

        if (response is not ViewResult
            {
                Model: MCVirmanAuditPage page
            })
            return response;

        static string Cell(string? input)
        {
            var value = (input ?? "")
                .Replace("\r", " ")
                .Replace("\n", " ");

            var check = value.TrimStart();

            if (check.Length > 0 &&
                (check[0] == '=' ||
                 check[0] == '+' ||
                 check[0] == '-' ||
                 check[0] == '@'))
                value = "'" + value;

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        var tr = CultureInfo.GetCultureInfo("tr-TR");

        var pairs = page.SearchedItems
            .GroupBy(x => new
            {
                x.Source,
                x.Target
            })
            .Select(g => new
            {
                Source = g.Key.Source,
                Target = g.Key.Target,
                Count = g.Count(),
                Matched = g.Count(x => x.IsMatched),
                Issues = g.Count(x => !x.IsMatched),
                TotalOut = g.Sum(x => x.TotalOut),
                TotalIn = g.Sum(x => x.TotalIn),
                Difference = g.Sum(x =>
                    Math.Abs(x.TotalIn - x.TotalOut))
            })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Source)
            .ThenBy(x => x.Target)
            .ToList();

        var csv = new System.Text.StringBuilder();

        csv.AppendLine("sep=;");
        csv.AppendLine("MASTER CHEF - HESAPLAR ARASI VİRMAN ANALİZİ");
        csv.AppendLine();

        csv.AppendLine("Başlangıç;" + Cell(page.Start));
        csv.AppendLine("Bitiş;" + Cell(page.End));
        csv.AppendLine("Arama;" + Cell(page.Search));

        csv.AppendLine(
            "Durum Filtresi;" +
            Cell(page.FilterStatus == "matched"
                ? "Eşleşen"
                : page.FilterStatus == "issues"
                    ? "Kontrol Gerektiren"
                    : "Tümü"));

        csv.AppendLine();

        csv.AppendLine(
            "Hesap Çifti Sayısı;" +
            pairs.Count.ToString(CultureInfo.InvariantCulture));

        csv.AppendLine(
            "Toplam Virman;" +
            pairs.Sum(x => x.Count)
                .ToString(CultureInfo.InvariantCulture));

        csv.AppendLine(
            "Eşleşen Virman;" +
            pairs.Sum(x => x.Matched)
                .ToString(CultureInfo.InvariantCulture));

        csv.AppendLine(
            "Sorunlu Virman;" +
            pairs.Sum(x => x.Issues)
                .ToString(CultureInfo.InvariantCulture));

        csv.AppendLine();

        csv.AppendLine(
            "Çıkış Hesabı;Giriş Hesabı;Virman Sayısı;" +
            "Eşleşen;Sorunlu;Çıkış Toplamı;" +
            "Giriş Toplamı;Net Fark;Mutlak Tutar Farkı");

        foreach (var pair in pairs)
        {
            csv.AppendLine(string.Join(";", new[]
            {
                Cell(pair.Source),
                Cell(pair.Target),
                pair.Count.ToString(CultureInfo.InvariantCulture),
                pair.Matched.ToString(CultureInfo.InvariantCulture),
                pair.Issues.ToString(CultureInfo.InvariantCulture),
                pair.TotalOut.ToString("0.00", tr),
                pair.TotalIn.ToString("0.00", tr),
                (pair.TotalIn - pair.TotalOut).ToString("0.00", tr),
                pair.Difference.ToString("0.00", tr)
            }));
        }

        csv.AppendLine();

        csv.AppendLine(string.Join(";", new[]
        {
            "TOPLAM",
            "",
            pairs.Sum(x => x.Count).ToString(CultureInfo.InvariantCulture),
            pairs.Sum(x => x.Matched).ToString(CultureInfo.InvariantCulture),
            pairs.Sum(x => x.Issues).ToString(CultureInfo.InvariantCulture),
            pairs.Sum(x => x.TotalOut).ToString("0.00", tr),
            pairs.Sum(x => x.TotalIn).ToString("0.00", tr),
            (pairs.Sum(x => x.TotalIn) -
             pairs.Sum(x => x.TotalOut)).ToString("0.00", tr),
            pairs.Sum(x => x.Difference).ToString("0.00", tr)
        }));

        csv.AppendLine();
        csv.AppendLine(
            Cell("Virmanlar şirket geliri veya gideri değildir."));

        csv.AppendLine(
            Cell("Sıfır tutar farkı bütün virmanların hatasız olduğu anlamına gelmez."));

        var encoding = new System.Text.UTF8Encoding(true);

        var bytes = encoding.GetPreamble()
            .Concat(encoding.GetBytes(csv.ToString()))
            .ToArray();

        var fileName =
            $"MASTER_CHEF_Hesaplar_Arasi_Virman_{page.Start}_{page.End}.csv";

        return File(
            bytes,
            "text/csv; charset=utf-8",
            fileName);
    }
    // MC_VIRMAN_PAIR_DETAILS_V1
    [HttpGet("PairDetails")]
    public async Task<IActionResult> PairDetails(
        string? source,
        string? target,
        string? start,
        string? end,
        string? status,
        string? q)
    {
        if (string.IsNullOrWhiteSpace(source) ||
            string.IsNullOrWhiteSpace(target) ||
            source.Length > 250 ||
            target.Length > 250)
        {
            return BadRequest("Hesap seçimi geçersiz.");
        }

        var response = await Index(start, end, status, q);

        if (response is not ViewResult
            {
                Model: MCVirmanAuditPage page
            })
        {
            return response;
        }

        var rows = page.SearchedItems
            .Where(x =>
                string.Equals(
                    x.Source,
                    source,
                    StringComparison.OrdinalIgnoreCase)
                &&
                string.Equals(
                    x.Target,
                    target,
                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Date)
            .ThenBy(x => x.Token)
            .ToList();

        if (rows.Count == 0)
            return NotFound("Seçilen hesap çiftinde virman bulunamadı.");

        var model = new MCVirmanPairDetailsPage
        {
            Source = source,
            Target = target,
            Start = page.Start,
            End = page.End,
            Status = page.FilterStatus,
            Search = page.Search,
            Rows = rows
        };

        return View("PairDetails", model);
    }
    // MC_PAIR_DETAILS_EXCEL_V1
    [HttpGet("PairDetailsExport")]
    public async Task<IActionResult> PairDetailsExport(
        string? source,
        string? target,
        string? start,
        string? end,
        string? status,
        string? q)
    {
        var response = await PairDetails(
            source, target, start, end, status, q);

        if (response is not ViewResult
            {
                Model: MCVirmanPairDetailsPage model
            })
            return response;

        static string Cell(string? input)
        {
            var value = (input ?? "")
                .Replace("\r", " ")
                .Replace("\n", " ");

            var check = value.TrimStart();

            if (check.Length > 0 &&
                (check[0] == '=' ||
                 check[0] == '+' ||
                 check[0] == '-' ||
                 check[0] == '@'))
            {
                value = "'" + value;
            }

            return "\"" +
                value.Replace("\"", "\"\"") +
                "\"";
        }

        var tr = CultureInfo.GetCultureInfo("tr-TR");
        var inv = CultureInfo.InvariantCulture;
        var csv = new System.Text.StringBuilder();

        csv.AppendLine("sep=;");
        csv.AppendLine("MASTER CHEF - HESAP ÇİFTİ VİRMAN DETAY RAPORU");
        csv.AppendLine();

        csv.AppendLine("Kaynak Hesap;" + Cell(model.Source));
        csv.AppendLine("Hedef Hesap;" + Cell(model.Target));
        csv.AppendLine("Başlangıç;" + Cell(model.Start));
        csv.AppendLine("Bitiş;" + Cell(model.End));
        csv.AppendLine("Arama;" + Cell(model.Search));

        csv.AppendLine(
            "Durum Filtresi;" +
            Cell(model.Status == "matched"
                ? "Eşleşen"
                : model.Status == "issues"
                    ? "Kontrol Gerektiren"
                    : "Tümü"));

        csv.AppendLine();

        csv.AppendLine(
            "Toplam Virman;" +
            model.Rows.Count.ToString(inv));

        csv.AppendLine(
            "Eşleşen Virman;" +
            model.Matched.ToString(inv));

        csv.AppendLine(
            "Sorunlu Virman;" +
            model.Issues.ToString(inv));

        csv.AppendLine(
            "Toplam Para Çıkışı;" +
            model.TotalOut.ToString("0.00", tr));

        csv.AppendLine(
            "Toplam Para Girişi;" +
            model.TotalIn.ToString("0.00", tr));

        csv.AppendLine(
            "Net Tutar Farkı;" +
            model.Difference.ToString("0.00", tr));

        csv.AppendLine(
            "Mutlak Tutar Farkı;" +
            model.AbsoluteDifference.ToString("0.00", tr));

        csv.AppendLine();
        csv.AppendLine("VİRMAN HAREKETLERİ");

        csv.AppendLine(
            "Tarih;Virman Kodu;Kaynak Hesap;Hedef Hesap;" +
            "Para Çıkışı;Para Girişi;Net Tutar Farkı;" +
            "Mutlak Tutar Farkı;Çıkış Sayısı;Giriş Sayısı;Durum");

        foreach (var row in model.Rows)
        {
            var difference = row.TotalIn - row.TotalOut;

            csv.AppendLine(string.Join(";", new[]
            {
                Cell(row.Date),
                Cell(row.Token),
                Cell(row.Source),
                Cell(row.Target),
                row.TotalOut.ToString("0.00", tr),
                row.TotalIn.ToString("0.00", tr),
                difference.ToString("0.00", tr),
                Math.Abs(difference).ToString("0.00", tr),
                row.OutCount.ToString(inv),
                row.InCount.ToString(inv),
                Cell(row.IsMatched ? "Eşleşti" : row.Issue)
            }));
        }

        csv.AppendLine();

        csv.AppendLine(string.Join(";", new[]
        {
            "TOPLAM",
            "",
            "",
            "",
            model.TotalOut.ToString("0.00", tr),
            model.TotalIn.ToString("0.00", tr),
            model.Difference.ToString("0.00", tr),
            model.AbsoluteDifference.ToString("0.00", tr),
            "",
            "",
            ""
        }));

        csv.AppendLine();

        csv.AppendLine(
            Cell("Virmanlar şirket geliri veya gideri değildir."));

        csv.AppendLine(
            Cell("Sıfır tutar farkı tüm virmanların " +
                 "doğru eşleştiğini göstermez."));

        var encoding = new System.Text.UTF8Encoding(true);

        var bytes = encoding.GetPreamble()
            .Concat(encoding.GetBytes(csv.ToString()))
            .ToArray();

        var fileName =
            $"MASTER_CHEF_Hesap_Cifti_Virman_{DateTime.Now:yyyyMMdd_HHmmss}.csv";

        return File(
            bytes,
            "text/csv; charset=utf-8",
            fileName);
    }
    // MC_PRIORITY_REVIEW_EXPORT_V1
    [HttpGet("PriorityExport")]
    public async Task<IActionResult> PriorityExport(
        string? start,
        string? end,
        string? status,
        string? q,
        int priority = 0)
    {
        var response = await Index(start, end, status, q);

        if (response is not ViewResult
            {
                Model: MCVirmanAuditPage page
            })
            return response;

        static string Cell(string? input)
        {
            var value = (input ?? "")
                .Replace("\r", " ")
                .Replace("\n", " ");

            var trimmed = value.TrimStart();

            if (trimmed.Length > 0 &&
                (trimmed[0] == '=' ||
                 trimmed[0] == '+' ||
                 trimmed[0] == '-' ||
                 trimmed[0] == '@'))
            {
                value = "'" + value;
            }

            return "\"" +
                value.Replace("\"", "\"\"") +
                "\"";
        }

        var tr = CultureInfo.GetCultureInfo("tr-TR");
        var inv = CultureInfo.InvariantCulture;

        var items = page.SearchedItems
            .Where(x => !x.IsMatched)
            .Select(x => new
            {
                Item = x,

                Priority =
                    x.InCount != 1 ||
                    x.OutCount != 1 ||
                    x.Issue.Contains(
                        "Kaynak ve hedef",
                        StringComparison.OrdinalIgnoreCase) ||
                    x.Issue.Contains(
                        "yönü",
                        StringComparison.OrdinalIgnoreCase)
                        ? 1
                        : x.TotalIn != x.TotalOut
                            ? 2
                            : 3,

                Difference = Math.Abs(
                    x.TotalIn - x.TotalOut)
            })
            .OrderBy(x => x.Priority)
            .ThenByDescending(x => x.Difference)
            .ThenByDescending(x => x.Item.Date)
            .ToList();

        if (priority is >= 1 and <= 3)
        {
            items = items
                .Where(x => x.Priority == priority)
                .ToList();
        }

        var priorityOne = items.Count(x => x.Priority == 1);
        var priorityTwo = items.Count(x => x.Priority == 2);
        var priorityThree = items.Count(x => x.Priority == 3);

        var csv = new System.Text.StringBuilder();

        csv.AppendLine("sep=;");
        csv.AppendLine("MASTER CHEF - ÖNCELİKLİ VİRMAN KONTROL RAPORU");
        csv.AppendLine();

        csv.AppendLine("Başlangıç;" + Cell(page.Start));
        csv.AppendLine("Bitiş;" + Cell(page.End));
        csv.AppendLine("Arama;" + Cell(page.Search));

        csv.AppendLine(
            "Durum Filtresi;" +
            Cell(page.FilterStatus == "matched"
                ? "Eşleşen"
                : page.FilterStatus == "issues"
                    ? "Kontrol Gerektiren"
                    : "Tümü"));

        csv.AppendLine();

        csv.AppendLine(
            "Toplam Sorunlu Virman;" +
            items.Count.ToString(inv));

        csv.AppendLine(
            "Öncelik 1 - Kayıt Sorunu;" +
            priorityOne.ToString(inv));

        csv.AppendLine(
            "Öncelik 2 - Tutar Farkı;" +
            priorityTwo.ToString(inv));

        csv.AppendLine(
            "Öncelik 3 - Diğer Kontroller;" +
            priorityThree.ToString(inv));

        csv.AppendLine(
            "Sorunlu Virman Giriş Toplamı;" +
            items.Sum(x => x.Item.TotalIn).ToString("0.00", tr));

        csv.AppendLine(
            "Sorunlu Virman Çıkış Toplamı;" +
            items.Sum(x => x.Item.TotalOut).ToString("0.00", tr));

        csv.AppendLine(
            "Toplam Mutlak Tutar Farkı;" +
            items.Sum(x => x.Difference).ToString("0.00", tr));

        csv.AppendLine();

        csv.AppendLine("ÖNCELİKLİ KONTROL LİSTESİ");

        csv.AppendLine(
            "Öncelik;Tarih;Virman Kodu;" +
            "Çıkış Hesabı;Giriş Hesabı;" +
            "Çıkış Tutarı;Giriş Tutarı;" +
            "Net Tutar Farkı;Mutlak Tutar Farkı;" +
            "Çıkış Hareketi;Giriş Hareketi;Sorun");

        foreach (var entry in items)
        {
            var item = entry.Item;

            csv.AppendLine(string.Join(";", new[]
            {
                entry.Priority.ToString(inv),
                Cell(item.Date),
                Cell(item.Token),
                Cell(item.Source),
                Cell(item.Target),
                item.TotalOut.ToString("0.00", tr),
                item.TotalIn.ToString("0.00", tr),
                (item.TotalIn - item.TotalOut).ToString("0.00", tr),
                entry.Difference.ToString("0.00", tr),
                item.OutCount.ToString(inv),
                item.InCount.ToString(inv),
                Cell(item.Issue)
            }));
        }

        csv.AppendLine();

        csv.AppendLine(
            Cell("Bu liste finansal risk tutarı değil, " +
                 "otomatik kontrol önceliği gösterir."));

        csv.AppendLine(
            Cell("Tutar farkı sıfır olan virmanlarda da " +
                 "kayıt sorunu bulunabilir."));

        var encoding = new System.Text.UTF8Encoding(true);

        var bytes = encoding.GetPreamble()
            .Concat(encoding.GetBytes(csv.ToString()))
            .ToArray();

        var fileName =
            $"MASTER_CHEF_Oncelikli_Virman_{page.Start}_{page.End}.csv";

        return File(
            bytes,
            "text/csv; charset=utf-8",
            fileName);
    }
    // MC_VIRMAN_PRIORITY_PAGING_V1
    [HttpGet("PriorityList")]
    public async Task<IActionResult> PriorityList(
        string? start,
        string? end,
        string? status,
        string? q,
        int p = 1,
        int priority = 0)
    {
        var response = await Index(start, end, status, q);

        if (response is not ViewResult
            {
                Model: MCVirmanAuditPage audit
            })
        {
            return response;
        }

        var all = audit.SearchedItems
            .Where(x => !x.IsMatched)
            .Select(x => new MCVirmanPriorityRow
            {
                Item = x,

                Priority =
                    x.InCount != 1 ||
                    x.OutCount != 1 ||
                    x.Issue.Contains(
                        "Kaynak ve hedef",
                        StringComparison.OrdinalIgnoreCase) ||
                    x.Issue.Contains(
                        "yönü",
                        StringComparison.OrdinalIgnoreCase)
                        ? 1
                        : x.TotalIn != x.TotalOut
                            ? 2
                            : 3,

                Difference = Math.Abs(
                    x.TotalIn - x.TotalOut)
            })
            .OrderBy(x => x.Priority)
            .ThenByDescending(x => x.Difference)
            .ThenByDescending(x => x.Item.Date)
            .ThenBy(x => x.Item.Token)
            .ToList();

        // MC_PRIORITY_FILTER_V1
        priority = priority is >= 1 and <= 3 ? priority : 0;

        var filtered = priority == 0
            ? all
            : all.Where(x => x.Priority == priority).ToList();

        // MC_VIRMAN_AGING_V1
        // Yaslandirma, sorunun ilk ortaya ciktigi tarihe degil,
        // kaydin son hareket tarihine gore hesaplanir.
        var agingToday = DateTime.Today;

        foreach (var entry in filtered)
        {
            if (DateTime.TryParse(
                    entry.Item.Date,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var movementDate) &&
                movementDate.Date <= agingToday)
            {
                entry.DaysSinceLastMovement =
                    (agingToday - movementDate.Date).Days;
            }
        }
        const int pageSize = 50;

        var totalPages = Math.Max(
            1,
            (filtered.Count + pageSize - 1) / pageSize);

        var currentPage = Math.Clamp(p, 1, totalPages);

        var model = new MCVirmanPriorityListPage
        {
            Start = audit.Start,
            End = audit.End,
            Status = audit.FilterStatus,
            Search = audit.Search,

            SelectedPriority = priority,
            TotalCount = filtered.Count,
            CurrentPage = currentPage,
            TotalPages = totalPages,
            PageSize = pageSize,

            PriorityOne = all.Count(x => x.Priority == 1),
            PriorityTwo = all.Count(x => x.Priority == 2),
            PriorityThree = all.Count(x => x.Priority == 3),

            TotalDifference = filtered.Sum(x => x.Difference),

            Age0To2 = filtered.Count(x =>
                x.DaysSinceLastMovement is >= 0 and <= 2),

            Age3To7 = filtered.Count(x =>
                x.DaysSinceLastMovement is >= 3 and <= 7),

            Age8To30 = filtered.Count(x =>
                x.DaysSinceLastMovement is >= 8 and <= 30),

            Age31Plus = filtered.Count(x =>
                x.DaysSinceLastMovement is >= 31),

            AgeUnknown = filtered.Count(x =>
                !x.DaysSinceLastMovement.HasValue),

            AgeKnownCount = filtered.Count(x =>
                x.DaysSinceLastMovement.HasValue),

            AgeOldestDays = filtered
                .Where(x => x.DaysSinceLastMovement.HasValue)
                .Select(x => x.DaysSinceLastMovement!.Value)
                .DefaultIfEmpty(0)
                .Max(),

            FilteredRows = filtered,
            Rows = filtered
                .Skip((currentPage - 1) * pageSize)
                .Take(pageSize)
                .ToList()
        };

        return View("PriorityList", model);
    }
    // MC_VIRMAN_AGING_EXCEL_V1
    [HttpGet("AgingExport")]
    public async Task<IActionResult> AgingExport(
        string? start,
        string? end,
        string? status,
        string? q,
        int priority = 0)
    {
        var response = await PriorityList(
            start, end, status, q, 1, priority);

        if (response is not ViewResult
            {
                Model: MCVirmanPriorityListPage model
            })
        {
            return response;
        }

        static string Cell(string? input)
        {
            var value = (input ?? "")
                .Replace("\r", " ")
                .Replace("\n", " ");

            var check = value.TrimStart();

            if (check.Length > 0 &&
                (check[0] == '=' ||
                 check[0] == '+' ||
                 check[0] == '-' ||
                 check[0] == '@'))
            {
                value = "'" + value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        static string AgeGroup(int? days)
        {
            if (!days.HasValue)
                return "Tarihi Kontrol Et";

            if (days.Value <= 2)
                return "0-2 Gün";

            if (days.Value <= 7)
                return "3-7 Gün";

            if (days.Value <= 30)
                return "8-30 Gün";

            return "31 Gün ve Üzeri";
        }

        var tr = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        // Sayfalama siniri uygulanmaz.
        var rows = model.FilteredRows;

        var csv = new System.Text.StringBuilder();

        csv.AppendLine("sep=;");
        csv.AppendLine("MASTER CHEF - SORUNLU VİRMAN YAŞLANDIRMA RAPORU");
        csv.AppendLine();

        csv.AppendLine("Başlangıç;" + Cell(model.Start));
        csv.AppendLine("Bitiş;" + Cell(model.End));
        csv.AppendLine("Arama;" + Cell(model.Search));

        csv.AppendLine(
            "Öncelik Filtresi;" +
            Cell(model.SelectedPriority == 0
                ? "Tümü"
                : "Öncelik " + model.SelectedPriority));

        csv.AppendLine("Durum Filtresi;" + Cell(model.Status));

        csv.AppendLine();

        csv.AppendLine("Toplam Sorunlu Virman;" + rows.Count);
        csv.AppendLine("0-2 Gün;" + model.Age0To2);
        csv.AppendLine("3-7 Gün;" + model.Age3To7);
        csv.AppendLine("8-30 Gün;" + model.Age8To30);
        csv.AppendLine("31 Gün ve Üzeri;" + model.Age31Plus);
        csv.AppendLine("Tarihi Kontrol Edilecek;" + model.AgeUnknown);

        csv.AppendLine(
            "Toplam Mutlak Tutar Farkı;" +
            model.TotalDifference.ToString("0.00", tr));

        csv.AppendLine();

        csv.AppendLine(
            "Öncelik;Tarih;Virman Kodu;" +
            "Çıkış Hesabı;Giriş Hesabı;" +
            "Son Hareketten Geçen Gün;Yaşlandırma Grubu;" +
            "Çıkış Tutarı;Giriş Tutarı;" +
            "Net Tutar Farkı;Mutlak Tutar Farkı;" +
            "Çıkış Hareket Sayısı;Giriş Hareket Sayısı;Sorun");

        foreach (var row in rows)
        {
            var item = row.Item;
            var difference = item.TotalIn - item.TotalOut;

            csv.AppendLine(string.Join(";", new[]
            {
                row.Priority.ToString(inv),
                Cell(item.Date),
                Cell(item.Token),
                Cell(item.Source),
                Cell(item.Target),

                row.DaysSinceLastMovement.HasValue
                    ? row.DaysSinceLastMovement.Value.ToString(inv)
                    : "",

                Cell(AgeGroup(row.DaysSinceLastMovement)),

                item.TotalOut.ToString("0.00", tr),
                item.TotalIn.ToString("0.00", tr),
                difference.ToString("0.00", tr),
                row.Difference.ToString("0.00", tr),

                item.OutCount.ToString(inv),
                item.InCount.ToString(inv),
                Cell(item.Issue)
            }));
        }

        csv.AppendLine();

        csv.AppendLine(
            Cell("Yaşlandırma son hareket tarihine göre hesaplanmıştır."));

        csv.AppendLine(
            Cell("Bu rapor finansal risk derecelendirmesi değildir."));

        var encoding = new System.Text.UTF8Encoding(true);

        var bytes = encoding.GetPreamble()
            .Concat(encoding.GetBytes(csv.ToString()))
            .ToArray();

        return File(
            bytes,
            "text/csv; charset=utf-8",
            $"MASTER_CHEF_Virman_Yaslandirma_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
    }
    private static string NormalizeToken(string token)
    {
        var value = token.Trim();

        if (value.EndsWith(":IN", StringComparison.OrdinalIgnoreCase))
            value = value.Substring(0, value.Length - 3);
        else if (value.EndsWith(":OUT", StringComparison.OrdinalIgnoreCase))
            value = value.Substring(0, value.Length - 4);

        return value.ToUpperInvariant();
    }
}

public sealed class MCVirmanAuditPage
{
    public string Start { get; set; } = "";
    public string End { get; set; } = "";

    public List<MCVirmanAuditItem> Items { get; set; } = new();

    public List<MCVirmanAuditMovement> Movements { get; set; } = new();

    public string FilterStatus { get; set; } = "all";

    public string Search { get; set; } = "";

    public List<MCVirmanAuditItem> SearchedItems =>
        VisibleItems.Where(x =>
            string.IsNullOrWhiteSpace(Search) ||
            x.Token.Contains(Search, StringComparison.OrdinalIgnoreCase) ||
            x.Source.Contains(Search, StringComparison.OrdinalIgnoreCase) ||
            x.Target.Contains(Search, StringComparison.OrdinalIgnoreCase) ||
            x.Issue.Contains(Search, StringComparison.OrdinalIgnoreCase)
        ).ToList();

    public List<MCVirmanAuditItem> VisibleItems =>
        FilterStatus switch
        {
            "matched" => Items.Where(x => x.IsMatched).ToList(),
            "issues" => Items.Where(x => !x.IsMatched).ToList(),
            _ => Items
        };

    public int Matched => Items.Count(x => x.IsMatched);
    public int Problematic => Items.Count(x => !x.IsMatched);

    public decimal MatchedAmount =>
        Items.Where(x => x.IsMatched).Sum(x => x.TotalOut);
}

public sealed class MCVirmanAuditItem
{
    public string Token { get; set; } = "";
    public string Date { get; set; } = "";
    public string Source { get; set; } = "";
    public string Target { get; set; } = "";

    public int InCount { get; set; }
    public int OutCount { get; set; }

    public decimal TotalIn { get; set; }
    public decimal TotalOut { get; set; }

    public string Issue { get; set; } = "";

    public bool IsMatched => string.IsNullOrEmpty(Issue);
}

public sealed class MCVirmanAuditMovement
{
    public long Id { get; set; }
    public string Date { get; set; } = "";
    public long AccountId { get; set; }
    public string Account { get; set; } = "";
    public string Direction { get; set; } = "";
    public decimal Amount { get; set; }
    public string Token { get; set; } = "";
}

// MC_VIRMAN_DETAILS_V1
public sealed class MCVirmanAuditDetailsPage
{
    public MCVirmanAuditItem Item { get; set; } = new();

    public List<MCVirmanAuditMovement> Movements { get; set; } = new();

    public string Start { get; set; } = "";
    public string End { get; set; } = "";
    public string Status { get; set; } = "all";
    public string Search { get; set; } = "";
}

// MC_VIRMAN_PAIR_DETAILS_V1
public sealed class MCVirmanPairDetailsPage
{
    public string Source { get; set; } = "";
    public string Target { get; set; } = "";

    public string Start { get; set; } = "";
    public string End { get; set; } = "";

    public string Status { get; set; } = "all";
    public string Search { get; set; } = "";

    public List<MCVirmanAuditItem> Rows { get; set; } = new();

    public int Matched =>
        Rows.Count(x => x.IsMatched);

    public int Issues =>
        Rows.Count(x => !x.IsMatched);

    public decimal TotalIn =>
        Rows.Sum(x => x.TotalIn);

    public decimal TotalOut =>
        Rows.Sum(x => x.TotalOut);

    public decimal Difference =>
        TotalIn - TotalOut;

    public decimal AbsoluteDifference =>
        Rows.Sum(x => Math.Abs(x.TotalIn - x.TotalOut));
}

// MC_VIRMAN_PRIORITY_PAGING_V1
public sealed class MCVirmanPriorityRow
{
    public int? DaysSinceLastMovement { get; set; }
    public MCVirmanAuditItem Item { get; set; } = new();

    public int Priority { get; set; }

    public decimal Difference { get; set; }
}

public sealed class MCVirmanPriorityListPage
{
    public List<MCVirmanPriorityRow> FilteredRows { get; set; } = new();
    public int Age0To2 { get; set; }
    public int Age3To7 { get; set; }
    public int Age8To30 { get; set; }
    public int Age31Plus { get; set; }
    public int AgeUnknown { get; set; }
    public int AgeKnownCount { get; set; }
    public int AgeOldestDays { get; set; }
    public int SelectedPriority { get; set; }
    public string Start { get; set; } = "";
    public string End { get; set; } = "";

    public string Status { get; set; } = "all";
    public string Search { get; set; } = "";

    public int TotalCount { get; set; }

    public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int PageSize { get; set; } = 50;

    public int PriorityOne { get; set; }
    public int PriorityTwo { get; set; }
    public int PriorityThree { get; set; }

    public decimal TotalDifference { get; set; }

    public List<MCVirmanPriorityRow> Rows { get; set; } = new();

    public int FirstRow =>
        TotalCount == 0
            ? 0
            : (CurrentPage - 1) * PageSize + 1;

    public int LastRow =>
        Math.Min(CurrentPage * PageSize, TotalCount);
}