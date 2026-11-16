using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

[Authorize(Roles = "Admin,Yonetici")]
[Route("MCReceiptStockAudit")]
public class MCReceiptStockAuditController : Controller
{
    private readonly IWebHostEnvironment _env;

    public MCReceiptStockAuditController(IWebHostEnvironment env)
    {
        _env = env;
    }

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

        // MC_RECEIPT_AUDIT_EXCEL_V1
    private static string CsvCell(object? value)
    {
        var text = Convert.ToString(
            value,
            CultureInfo.InvariantCulture
        ) ?? "";

        if (text.StartsWith("=") ||
            text.StartsWith("+") ||
            text.StartsWith("-") ||
            text.StartsWith("@"))
        {
            text = "'" + text;
        }

        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
    private static string H(object? value)
    {
        return WebUtility.HtmlEncode(
            Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
    }

    private sealed class ReceiptRow
    {
        public long Id { get; set; }
        public string Description { get; set; } = "";
        public string Unit { get; set; } = "";
        public string AcceptedText { get; set; } = "";
        public bool AcceptedValid { get; set; }
        public decimal Accepted { get; set; }
        public decimal Transferred { get; set; }
        public int TransferCount { get; set; }
        public bool InvalidTransfer { get; set; }
        public bool MissingMovement { get; set; }
        public bool MovementMismatch { get; set; }
        public bool MovementQuantityMismatch { get; set; }
        public bool UnitMismatch { get; set; }
        public bool DuplicateMovement { get; set; }
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

        var rows = new Dictionary<long, ReceiptRow>();
        var movementOwners = new Dictionary<long, long>();

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT Id,
                       COALESCE(ProductDescription, ''),
                       COALESCE(Unit, ''),
                       COALESCE(AcceptedQuantity, '')
                FROM PurchaseGoodsReceiptLines
                ORDER BY Id DESC
                """;

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var row = new ReceiptRow
                {
                    Id = reader.GetInt64(0),
                    Description = reader.GetString(1),
                    Unit = reader.GetString(2),
                    AcceptedText = reader.GetString(3)
                };

                row.AcceptedValid = TryQuantity(
                    row.AcceptedText, out var accepted);

                row.Accepted = accepted;
                rows.Add(row.Id, row);
            }
        }

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT
                    t.PurchaseGoodsReceiptLineId,
                    t.Quantity,
                    COALESCE(t.Unit, ''),
                    t.RawMaterialId,
                    t.WarehouseId,
                    t.RawMaterialStockMovementId,
                    m.Id,
                    m.RawMaterialId,
                    m.WarehouseId,
                    m.Quantity,
                    COALESCE(m.Unit, '')
                FROM RawMaterialReceiptTransfers t
                LEFT JOIN RawMaterialStockMovements m
                    ON m.Id = t.RawMaterialStockMovementId
                ORDER BY t.PurchaseGoodsReceiptLineId, t.Id
                """;

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                if (reader.IsDBNull(0))
                    continue;

                var receiptId = reader.GetInt64(0);

                if (!rows.TryGetValue(receiptId, out var row))
                    continue;

                row.TransferCount++;
                // MC_DUPLICATE_RECEIPT_MOVEMENT_V1
                if (!reader.IsDBNull(5))
                {
                    var movementId = Convert.ToInt64(reader.GetValue(5));

                    if (movementOwners.TryGetValue(movementId, out var previousReceipt))
                    {
                        row.DuplicateMovement = true;

                        if (rows.TryGetValue(previousReceipt, out var previousRow))
                            previousRow.DuplicateMovement = true;
                    }
                    else
                    {
                        movementOwners.Add(movementId, receiptId);
                    }
                }


                if (!TryQuantity(reader.GetValue(1), out var quantity))
                {
                    row.InvalidTransfer = true;
                    continue;
                }

                row.Transferred += quantity;

                var transferUnit = reader.IsDBNull(2)
                    ? "" : reader.GetString(2);

                if (!string.Equals(row.Unit.Trim(),
                        transferUnit.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                    row.UnitMismatch = true;

                if (reader.IsDBNull(6))
                {
                    row.MissingMovement = true;
                    continue;
                }

                if (reader.IsDBNull(3) || reader.IsDBNull(4) ||
                    reader.IsDBNull(7) || reader.IsDBNull(8) ||
                    Convert.ToInt64(reader.GetValue(3)) !=
                        Convert.ToInt64(reader.GetValue(7)) ||
                    Convert.ToInt64(reader.GetValue(4)) !=
                        Convert.ToInt64(reader.GetValue(8)))
                {
                    row.MovementMismatch = true;
                }

                // MC_MOVEMENT_QUANTITY_MISMATCH_V1
                if (reader.IsDBNull(9) ||
                    !TryQuantity(reader.GetValue(9), out var movementQuantity) ||
                    movementQuantity != quantity)
                {
                    row.MovementQuantityMismatch = true;
                    row.MovementMismatch = true;
                }

                var movementUnit = reader.IsDBNull(10)
                    ? "" : reader.GetString(10);

                if (!string.Equals(transferUnit.Trim(),
                        movementUnit.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                    row.UnitMismatch = true;
            }
        }

                var html = new StringBuilder();

        var csv = new StringBuilder();
        csv.AppendLine(
            "Mal Kabul Satiri;Hammadde;Kabul Edilen;Stoga Aktarilan;Fark;Birim;Durum;Sorunlar"
        );
        var shownCount = 0;
        var normalCount = 0;
        var problemCount = 0;

        html.Append("<section style='padding:20px'>");
                // MC_RECEIPT_PROBLEM_FILTER_V2
        html.Append("<h2>Mal Kabul - Hammadde Stok Kontrolu</h2>");
        html.Append("<p style='display:flex;gap:8px;flex-wrap:wrap'>");
        html.Append("<a class='mc-filter-back' href='/MCProcurementAudit'>Kontrol Merkezine Dön</a>");
        html.Append("<a class='mc-filter-link' href='/MCReceiptStockAudit?onlyProblems=false'>Tüm Kayıtlar</a>");
        html.Append("<a class='mc-filter-link' href='/MCReceiptStockAudit?onlyProblems=true'>Yalnızca Sorunlular</a>");
        html.Append("</p>");
        html.Append("<p>Kabul edilen miktarlar ile stok aktarimlari karsilastirilir. Veriler salt okunur.</p>");
        html.Append("<div style='overflow-x:auto'>");
        html.Append("<table style='width:100%;border-collapse:collapse;background:white'>");
        html.Append("<thead><tr style='background:#24496c;color:white'>");

        foreach (var title in new[]
        {
            "Mal Kabul Satiri",
            "Hammadde",
            "Kabul Edilen",
            "Stoga Aktarilan",
            "Fark",
            "Birim", "Durum", "Tespit Edilen Sorunlar"
        })
        {
            html.Append("<th style='padding:12px;text-align:left'>");
            html.Append(H(title));
            html.Append("</th>");
        }

        html.Append("</tr></thead><tbody>");

        foreach (var row in rows.Values)
        {
            var difference = row.Accepted - row.Transferred;

            var invalid = row.DuplicateMovement || !row.AcceptedValid ||
                          row.InvalidTransfer ||
                          row.MissingMovement ||
                          row.MovementMismatch ||
                          row.UnitMismatch;

                        // MC_NONPOSITIVE_QUANTITY_AUDIT_V1
                        // MC_RECEIPT_TRANSFER_STATUS_V1
            // MC_UNIT_MISMATCH_SAFE_TOTAL_V1
            var status = row.MovementQuantityMismatch
                ? "STOK HAREKETI MIKTAR FARKI"
                : row.DuplicateMovement
                ? "MUKERRER STOK HAREKETI"
                : row.TransferCount == 0 && row.AcceptedValid && row.Accepted > 0
                ? "AKTARIM BEKLIYOR"
                : row.AcceptedValid && row.Accepted <= 0
                ? "KABUL MIKTARI KONTROL"
                : row.Transferred < 0
                    ? "NEGATIF STOK AKTARIMI"
                    : !row.AcceptedValid || row.InvalidTransfer
                ? "MIKTAR OKUNAMADI"
                : row.MissingMovement
                    ? "STOK HAREKETI EKSIK"
                    : row.MovementMismatch
                        ? "STOK HAREKETI UYUSMUYOR"
                        : row.UnitMismatch
                            ? "BIRIM FARKI"
                            : difference > 0
                                ? "EKSIK AKTARIM"
                                : difference < 0
                                    ? "FAZLA AKTARIM"
                                    : "ESLESEN";

                        // MC_STOCK_SINGLE_ISSUE_EVALUATION_V1
            // MC_STOCK_ISSUE_SUMMARY_V2
            var issues = new List<string>();

            if (!row.AcceptedValid || row.InvalidTransfer)
                issues.Add("Miktar okunamadi");

            if (row.DuplicateMovement)
                issues.Add("Mukerrer stok hareketi");

            if (row.MissingMovement)
                issues.Add("Stok hareketi eksik");

            if (row.MovementQuantityMismatch)
                issues.Add("Stok hareketi miktar farki");

            if (row.MovementMismatch && !row.MovementQuantityMismatch)
                issues.Add("Stok hareketi bilgileri uyusmuyor");

            if (row.UnitMismatch)
                issues.Add("Birim farki");

            if (row.AcceptedValid && row.Accepted <= 0)
                issues.Add("Kabul miktari kontrol edilmeli");

            if (row.TransferCount == 0 && row.AcceptedValid && row.Accepted > 0)
                issues.Add("Aktarim bekliyor");

            if (row.AcceptedValid &&
                !row.InvalidTransfer &&
                !row.DuplicateMovement &&
                !row.UnitMismatch &&
                !row.MovementMismatch)
            {
                if (difference > 0)
                    issues.Add("Eksik aktarim");

                if (difference < 0)
                    issues.Add("Fazla aktarim");
            }

            var issueSummary = issues.Count == 0
                ? "Sorun bulunmadi"
                : string.Join("; ", issues);

            // MC_STOCK_ISSUE_CONSISTENCY_V1
            if (issues.Count > 0 && status == "ESLESEN")
                status = "KONTROL GEREKIYOR";

            
                        // MC_STOCK_UNIFIED_EVALUATION_V2
            var problem = issues.Count > 0;

            if (problem)
                problemCount++;
            else
                normalCount++;

            if (onlyProblems && !problem)
                continue;

            shownCount++;

            var background = problem ? "#fff1f2" : "#f0fdf4";

            html.Append("<tr style='background:");
            html.Append(background);
            html.Append(";border-bottom:1px solid #e2e8f0'>");

            // MC_DUPLICATE_MOVEMENT_SAFE_TOTAL_V1
                                    // MC_STOCK_EXPORT_RELIABILITY_V1
            var reliableDifference =
                row.AcceptedValid &&
                !row.InvalidTransfer &&
                !row.DuplicateMovement &&
                !row.UnitMismatch &&
                !row.MissingMovement &&
                !row.MovementMismatch;

            var differenceText = reliableDifference
                ? difference.ToString("0.###", CultureInfo.InvariantCulture)
                : "KONTROL GEREKIYOR";
            var values = new[]
            {
                row.Id.ToString(CultureInfo.InvariantCulture),
                row.Description,
                row.AcceptedText,
                row.Transferred.ToString("0.###", CultureInfo.InvariantCulture),
                differenceText,
                row.Unit, status, issueSummary
            };

                        csv.AppendLine(string.Join(";",
                System.Linq.Enumerable.Select(values, value => CsvCell(value))
            ));
            foreach (var value in values)
            {
                html.Append("<td style='padding:11px'>");
                html.Append(H(value));
                html.Append("</td>");
            }

            html.Append("</tr>");
        }

        if (shownCount == 0)
        {
            html.Append(
                "<tr><td colspan='8' style='padding:20px'>" +
                "Secilen filtrede gosterilecek mal kabul kaydi bulunmuyor.</td></tr>");
        }

        html.Append("</tbody></table></div>");

        html.Append("<div style='margin-top:20px;padding:15px;background:#f1f5f9'>");
        html.Append("<strong>Toplam Mal Kabul Satiri: ");
        html.Append(rows.Count);
        html.Append("</strong><br/>Eslesen: ");
        html.Append(normalCount);
        html.Append("<br/>Goruntulenen: ");
        html.Append(shownCount);
        html.Append("<br/>Kontrol Gerektiren: ");
        html.Append(problemCount);
        html.Append("</div>");

        html.Append("<p style='margin-top:12px'>");
        html.Append("Not: Bu ekran fatura miktarlarini henuz karsilastirmaz. ");
        html.Append("Yalnizca mal kabul ve hammadde stok aktarimini kontrol eder.");
        html.Append("</p></section>");

        ViewData["Title"] = "Mal Kabul Stok Kontrolu";

                if (Request.Query.ContainsKey("exportExcel"))
        {
            var bytes = new UTF8Encoding(true).GetPreamble()
                .Concat(Encoding.UTF8.GetBytes(csv.ToString()))
                .ToArray();

            return File(
                bytes,
                "text/csv; charset=utf-8",
                "Mal_Kabul_Stok_Kontrol_" +
                DateTime.Now.ToString("yyyyMMdd") + ".csv"
            );
        }
        return View(
            "~/Views/MCReceiptStockAudit/Index.cshtml",
            html.ToString()
        );
    }
}