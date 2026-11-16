// MC_MISSING_CARI_DIRECT_LINK_V1
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

[Authorize(Roles = "Admin,Yonetici")]
[Route("MCProcurementAudit")]
public class MCProcurementAuditController : Controller
{
    // MC_PROCUREMENT_AUDIT_CENTER_V1

    private readonly IWebHostEnvironment _env;

    public MCProcurementAuditController(IWebHostEnvironment env)
    {
        _env = env;
    }

    // MC_PROCUREMENT_SAFE_QUANTITY_V2
    private static bool TryAuditQuantity(string? value, out decimal quantity)
    {
        quantity = 0m;

        var text = (value ?? "").Trim()
            .Replace(" ", "")
            .Replace("\u00A0", "");

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
        else if (!System.Text.RegularExpressions.Regex.IsMatch(
            text, @"^-?\d+$"))
        {
            return false;
        }

        return decimal.TryParse(
            text,
            System.Globalization.NumberStyles.AllowLeadingSign |
            System.Globalization.NumberStyles.AllowDecimalPoint,
            System.Globalization.CultureInfo.InvariantCulture,
            out quantity);
    }
    private static string H(string value) =>
        WebUtility.HtmlEncode(value);

    private static async Task<long> Count(
        SqliteConnection connection,
        string sql)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    private static void Card(
        StringBuilder html,
        string title,
        long count,
        string description,
        string link,
        string button)
    {
        html.Append("<article style='background:white;border:1px solid #dbe4ee;");
        html.Append("border-radius:12px;padding:22px;min-width:250px;flex:1'>");

        html.Append("<h3 style='margin:0 0 12px;color:#24496c'>");
        html.Append(H(title));
        html.Append("</h3>");

        html.Append("<div style='font-size:32px;font-weight:700'>");
        html.Append(count);
        html.Append("</div>");

        html.Append("<p style='color:#64748b;min-height:45px'>");
        html.Append(H(description));
        html.Append("</p>");

        html.Append("<a style='display:inline-block;padding:10px 14px;");
        html.Append("background:#24496c;color:white;text-decoration:none;");
        html.Append("border-radius:7px' href='");
        html.Append(H(link));
        html.Append("'>");
        html.Append(H(button));
        html.Append("</a></article>");
    }

    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index([FromQuery] string? workflowFilter = null, [FromQuery] int page = 1, [FromQuery] string? invoiceSearch = null)
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

        var invoices = await Count(connection,
            "SELECT COUNT(*) FROM MCPurchaseInvoices");

        var invoiceLines = await Count(connection,
            "SELECT COUNT(*) FROM MCPurchaseInvoiceLines");

        var receiptLines = await Count(connection,
            "SELECT COUNT(*) FROM PurchaseGoodsReceiptLines");

        var transfers = await Count(connection,
            "SELECT COUNT(*) FROM RawMaterialReceiptTransfers");

        var postings = await Count(connection,
            "SELECT COUNT(*) FROM MCPurchaseInvoiceCariPostings");

        var invoiceLinesWithoutReceipt = await Count(connection, """
            SELECT COUNT(*)
            FROM MCPurchaseInvoiceLines l
            LEFT JOIN PurchaseGoodsReceiptLines r
                ON r.Id = l.PurchaseGoodsReceiptLineId
            WHERE l.PurchaseGoodsReceiptLineId IS NULL
               OR r.Id IS NULL
            """);

        var receiptLinesWithoutTransfer = await Count(connection, """
            SELECT COUNT(*)
            FROM PurchaseGoodsReceiptLines r
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM RawMaterialReceiptTransfers t
                WHERE t.PurchaseGoodsReceiptLineId = r.Id
            )
            """);

        var transferLinesWithoutMovement = await Count(connection, """
            SELECT COUNT(*)
            FROM RawMaterialReceiptTransfers t
            LEFT JOIN RawMaterialStockMovements m
                ON m.Id = t.RawMaterialStockMovementId
            WHERE m.Id IS NULL
            """);

        var invoicesWithoutPosting = await Count(connection, """
            SELECT COUNT(*)
            FROM MCPurchaseInvoices i
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM MCPurchaseInvoiceCariPostings p
                WHERE p.PurchaseInvoiceId = i.Id
            )
            """);

        var postingsWithoutMovement = await Count(connection, """
            SELECT COUNT(*)
            FROM MCPurchaseInvoiceCariPostings p
            LEFT JOIN MCCariMovements m
                ON m.Id = p.CariMovementId
            WHERE m.Id IS NULL
            """);

                // MC_PROCUREMENT_INVOICE_WORKFLOW_V1
                // MC_PROCUREMENT_WORKFLOW_FILTER_V1
        var selectedWorkflowFilter = (workflowFilter ?? "all").Trim().ToLowerInvariant();
        // MC_PROCUREMENT_INVOICE_SEARCH_V1
        var selectedInvoiceSearch = (invoiceSearch ?? "").Trim();

        if (selectedInvoiceSearch.Length > 100)
            return BadRequest("Fatura arama metni 100 karakteri gecemez.");
        // MC_PROCUREMENT_PAGINATION_V1
        const int pageSize = 50;

        if (page < 1)
            page = 1;

        var offset = ((long)page - 1L) * pageSize;


        if (selectedWorkflowFilter != "all" &&
            selectedWorkflowFilter != "waiting" &&
            selectedWorkflowFilter != "issues" && selectedWorkflowFilter != "linked")
        {
            return BadRequest("Gecersiz islem durumu filtresi.");
        }
        var workflowRows = new System.Collections.Generic.List<
            (long Id, string Number, string Currency, string Amount,
             long Lines, long MissingReceipts, long MissingTransfers,
             long MissingMovements, long CariPostings, long MissingCariMovements)>();

        await using (var workflowCmd = connection.CreateCommand())
        {
            workflowCmd.CommandText = $"""
                SELECT
                    i.Id,
                    COALESCE(i.InvoiceNumber, ''),
                    COALESCE(i.Currency, ''),
                    CAST(i.GrandTotal AS TEXT),

                    (SELECT COUNT(*)
                     FROM MCPurchaseInvoiceLines l
                     WHERE l.PurchaseInvoiceId = i.Id),

                    (SELECT COUNT(*)
                     FROM MCPurchaseInvoiceLines l
                     LEFT JOIN PurchaseGoodsReceiptLines r
                       ON r.Id = l.PurchaseGoodsReceiptLineId
                     WHERE l.PurchaseInvoiceId = i.Id
                       AND r.Id IS NULL),

                    (SELECT COUNT(*)
                     FROM MCPurchaseInvoiceLines l
                     JOIN PurchaseGoodsReceiptLines r
                       ON r.Id = l.PurchaseGoodsReceiptLineId
                     WHERE l.PurchaseInvoiceId = i.Id
                       AND NOT EXISTS (
                           SELECT 1 FROM RawMaterialReceiptTransfers t
                           WHERE t.PurchaseGoodsReceiptLineId = r.Id
                       )),

                    (SELECT COUNT(*)
                     FROM MCPurchaseInvoiceLines l
                     JOIN RawMaterialReceiptTransfers t
                       ON t.PurchaseGoodsReceiptLineId = l.PurchaseGoodsReceiptLineId
                     LEFT JOIN RawMaterialStockMovements m
                       ON m.Id = t.RawMaterialStockMovementId
                     WHERE l.PurchaseInvoiceId = i.Id
                       AND m.Id IS NULL),

                    (SELECT COUNT(*)
                     FROM MCPurchaseInvoiceCariPostings p
                     WHERE p.PurchaseInvoiceId = i.Id),

                    (SELECT COUNT(*)
                     FROM MCPurchaseInvoiceCariPostings p
                     LEFT JOIN MCCariMovements m
                       ON m.Id = p.CariMovementId
                     WHERE p.PurchaseInvoiceId = i.Id
                       AND m.Id IS NULL)

                FROM MCPurchaseInvoices i
                ORDER BY i.Id DESC
                -- MC_GLOBAL_FILTER_PAGING_V5
                """;

            await using var workflowReader =
                await workflowCmd.ExecuteReaderAsync();

            while (await workflowReader.ReadAsync())
            {
                workflowRows.Add((
                    Convert.ToInt64(workflowReader.GetValue(0)),
                    Convert.ToString(workflowReader.GetValue(1)) ?? "",
                    Convert.ToString(workflowReader.GetValue(2)) ?? "",
                    Convert.ToString(workflowReader.GetValue(3)) ?? "",
                    Convert.ToInt64(workflowReader.GetValue(4)),
                    Convert.ToInt64(workflowReader.GetValue(5)),
                    Convert.ToInt64(workflowReader.GetValue(6)),
                    Convert.ToInt64(workflowReader.GetValue(7)),
                    Convert.ToInt64(workflowReader.GetValue(8)),
                    Convert.ToInt64(workflowReader.GetValue(9))
                ));
            }
        }
                // MC_PROCUREMENT_QUANTITY_AUDIT_V1
        var quantityIssues = new System.Collections.Generic.HashSet<long>();

        await using (var quantityCmd = connection.CreateCommand())
        {
            quantityCmd.CommandText = """
                SELECT
                    l.PurchaseInvoiceId,
                    l.Quantity,
                    l.Unit,
                    r.AcceptedQuantity,
                    r.Unit,
                    l.PurchaseGoodsReceiptLineId,
                    (
                        SELECT COUNT(*)
                        FROM MCPurchaseInvoiceLines x
                        WHERE x.PurchaseGoodsReceiptLineId =
                              l.PurchaseGoodsReceiptLineId
                          AND l.PurchaseGoodsReceiptLineId IS NOT NULL
                    )
                FROM MCPurchaseInvoiceLines l
                LEFT JOIN PurchaseGoodsReceiptLines r
                    ON r.Id = l.PurchaseGoodsReceiptLineId
                """;

            await using var quantityReader =
                await quantityCmd.ExecuteReaderAsync();

            while (await quantityReader.ReadAsync())
            {
                var invoiceId = Convert.ToInt64(
                    quantityReader.GetValue(0));

                if (quantityReader.IsDBNull(3))
                    continue;

                var invoiceText = Convert.ToString(
                    quantityReader.GetValue(1)) ?? "";

                var receivedText = Convert.ToString(
                    quantityReader.GetValue(3)) ?? "";

                var invoiceUnit = quantityReader.IsDBNull(2)
                    ? "" : Convert.ToString(quantityReader.GetValue(2)) ?? "";

                var receivedUnit = quantityReader.IsDBNull(4)
                    ? "" : Convert.ToString(quantityReader.GetValue(4)) ?? "";

                                var invoiceValid = TryAuditQuantity(
                    invoiceText, out var invoiceQty);

                var receivedValid = TryAuditQuantity(
                    receivedText, out var receivedQty);

                var duplicate = Convert.ToInt64(
                    quantityReader.GetValue(6)) > 1;

                if (!invoiceValid ||
                    !receivedValid ||
                    invoiceQty <= 0 ||
                    receivedQty < 0 ||
                    !string.Equals(
                        invoiceUnit.Trim(),
                        receivedUnit.Trim(),
                        StringComparison.OrdinalIgnoreCase) ||
                    invoiceQty != receivedQty ||
                    duplicate)
                {
                    quantityIssues.Add(invoiceId);
                }
            }
        }
                // MC_DUPLICATE_RECEIPT_AUDIT_FIXED_V2
        var duplicateReceiptInvoices = new System.Collections.Generic.HashSet<long>();

        await using (var duplicateCmd = connection.CreateCommand())
        {
            duplicateCmd.CommandText = """
                SELECT DISTINCT l.PurchaseInvoiceId
                FROM MCPurchaseInvoiceLines l
                WHERE l.PurchaseGoodsReceiptLineId IS NOT NULL
                  AND l.PurchaseGoodsReceiptLineId IN
                  (
                      SELECT x.PurchaseGoodsReceiptLineId
                      FROM MCPurchaseInvoiceLines x
                      WHERE x.PurchaseGoodsReceiptLineId IS NOT NULL
                      GROUP BY x.PurchaseGoodsReceiptLineId
                      HAVING COUNT(*) > 1
                  )
                """;

            await using var duplicateReader = await duplicateCmd.ExecuteReaderAsync();

            while (await duplicateReader.ReadAsync())
            {
                if (!duplicateReader.IsDBNull(0))
                    duplicateReceiptInvoices.Add(
                        Convert.ToInt64(duplicateReader.GetValue(0)));
            }
        }
                // MC_DUPLICATE_RECEIPT_DETAILS_V3
        var duplicateReceiptDetails =
            new System.Collections.Generic.Dictionary<long, System.Collections.Generic.SortedSet<long>>();

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT l.PurchaseInvoiceId, l.PurchaseGoodsReceiptLineId
                FROM MCPurchaseInvoiceLines l
                WHERE l.PurchaseGoodsReceiptLineId IS NOT NULL
                  AND l.PurchaseGoodsReceiptLineId IN
                  (
                      SELECT PurchaseGoodsReceiptLineId
                      FROM MCPurchaseInvoiceLines
                      WHERE PurchaseGoodsReceiptLineId IS NOT NULL
                      GROUP BY PurchaseGoodsReceiptLineId
                      HAVING COUNT(*) > 1
                  )
                """;

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                if (reader.IsDBNull(0) || reader.IsDBNull(1))
                    continue;

                var invoiceId = Convert.ToInt64(reader.GetValue(0));
                var receiptId = Convert.ToInt64(reader.GetValue(1));

                if (!duplicateReceiptDetails.TryGetValue(invoiceId, out var ids))
                {
                    ids = new System.Collections.Generic.SortedSet<long>();
                    duplicateReceiptDetails[invoiceId] = ids;
                }

                ids.Add(receiptId);
            }
        }
                // MC_INVOICE_RECEIPT_QUANTITY_DETAILS_V1
        var quantityDetails =
            new System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<string>>();

        await using (var detailCmd = connection.CreateCommand())
        {
            detailCmd.CommandText = """
                SELECT
                    l.PurchaseInvoiceId,
                    l.Id,
                    l.Quantity,
                    l.Unit,
                    r.AcceptedQuantity,
                    r.Unit
                FROM MCPurchaseInvoiceLines l
                JOIN PurchaseGoodsReceiptLines r
                  ON r.Id = l.PurchaseGoodsReceiptLineId
                """;

            await using var detailReader =
                await detailCmd.ExecuteReaderAsync();

            while (await detailReader.ReadAsync())
            {
                var invoiceId = Convert.ToInt64(detailReader.GetValue(0));
                var lineId = Convert.ToInt64(detailReader.GetValue(1));

                var invoiceText = Convert.ToString(detailReader.GetValue(2)) ?? "";
                var invoiceUnit = Convert.ToString(detailReader.GetValue(3)) ?? "";

                var receiptText = Convert.ToString(detailReader.GetValue(4)) ?? "";
                var receiptUnit = Convert.ToString(detailReader.GetValue(5)) ?? "";

                var validInvoice = TryAuditQuantity(invoiceText, out var invoiceQty);
                var validReceipt = TryAuditQuantity(receiptText, out var receiptQty);

                if (!validInvoice || !validReceipt)
                    continue;

                var unitMismatch = !string.Equals(
                    invoiceUnit.Trim(),
                    receiptUnit.Trim(),
                    StringComparison.OrdinalIgnoreCase);

                if (!unitMismatch && invoiceQty == receiptQty)
                    continue;

                if (!quantityDetails.TryGetValue(invoiceId, out var messages))
                {
                    messages = new System.Collections.Generic.List<string>();
                    quantityDetails[invoiceId] = messages;
                }

                var message =
                    "Fatura satiri " + lineId +
                    ": Fatura " + invoiceQty.ToString("0.###",
                        System.Globalization.CultureInfo.InvariantCulture) +
                    " " + invoiceUnit +
                    ", Mal kabul " + receiptQty.ToString("0.###",
                        System.Globalization.CultureInfo.InvariantCulture) +
                    " " + receiptUnit;

                if (!unitMismatch)
                {
                    var difference = invoiceQty - receiptQty;

                    message += ", Fark " + difference.ToString("0.###",
                        System.Globalization.CultureInfo.InvariantCulture) +
                        " " + invoiceUnit;
                }
                else
                {
                    message += " (Birim uyusmazligi)";
                }

                messages.Add(message);
            }
        }
                // MC_RECEIPT_TRANSFER_DETAILS_V1
        var receiptTransferDetails =
            new System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<string>>();

        await using (var transferDetailCmd = connection.CreateCommand())
        {
            transferDetailCmd.CommandText = """
                SELECT DISTINCT
                    l.PurchaseInvoiceId,
                    r.Id,
                    r.AcceptedQuantity,
                    r.Unit,
                    t.Id,
                    t.Quantity,
                    t.Unit
                FROM MCPurchaseInvoiceLines l
                JOIN PurchaseGoodsReceiptLines r
                  ON r.Id = l.PurchaseGoodsReceiptLineId
                LEFT JOIN RawMaterialReceiptTransfers t
                  ON t.PurchaseGoodsReceiptLineId = r.Id
                ORDER BY l.PurchaseInvoiceId, r.Id, t.Id
                """;

            var totals = new System.Collections.Generic.Dictionary<
                (long InvoiceId, long ReceiptId),
                (string Accepted, string Unit, decimal Transferred, bool Invalid, bool UnitMismatch)>();

            await using var transferDetailReader =
                await transferDetailCmd.ExecuteReaderAsync();

            while (await transferDetailReader.ReadAsync())
            {
                var invoiceId = Convert.ToInt64(transferDetailReader.GetValue(0));
                var receiptId = Convert.ToInt64(transferDetailReader.GetValue(1));
                var acceptedText = Convert.ToString(transferDetailReader.GetValue(2)) ?? "";
                var receiptUnit = Convert.ToString(transferDetailReader.GetValue(3)) ?? "";

                var key = (invoiceId, receiptId);

                if (!totals.TryGetValue(key, out var current))
                    current = (acceptedText, receiptUnit, 0m, false, false);

                if (!transferDetailReader.IsDBNull(4))
                {
                    var transferText =
                        Convert.ToString(transferDetailReader.GetValue(5)) ?? "";

                    var transferUnit =
                        Convert.ToString(transferDetailReader.GetValue(6)) ?? "";

                    if (TryAuditQuantity(transferText, out var transferQty) &&
                        transferQty >= 0)
                    {
                        current.Transferred += transferQty;
                    }
                    else
                    {
                        current.Invalid = true;
                    }

                    if (!string.Equals(
                        receiptUnit.Trim(),
                        transferUnit.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                    {
                        current.UnitMismatch = true;
                    }
                }

                totals[key] = current;
            }

            foreach (var entry in totals)
            {
                var invoiceId = entry.Key.InvoiceId;
                var receiptId = entry.Key.ReceiptId;
                var value = entry.Value;

                var acceptedValid =
                    TryAuditQuantity(value.Accepted, out var acceptedQty);

                if (acceptedValid &&
                    acceptedQty >= 0 &&
                    !value.Invalid &&
                    !value.UnitMismatch &&
                    acceptedQty == value.Transferred)
                    continue;

                if (!receiptTransferDetails.TryGetValue(invoiceId, out var messages))
                {
                    messages = new System.Collections.Generic.List<string>();
                    receiptTransferDetails[invoiceId] = messages;
                }

                var message = "Mal kabul satiri " + receiptId + ": ";

                if (!acceptedValid || acceptedQty < 0 || value.Invalid)
                {
                    message += "Miktar kontrol edilmeli";
                }
                else if (value.UnitMismatch)
                {
                    message += "Stok aktariminda birim uyusmazligi";
                }
                else
                {
                    var difference = acceptedQty - value.Transferred;
                    message += "Kabul " +
                        acceptedQty.ToString("0.###",
                            System.Globalization.CultureInfo.InvariantCulture) +
                        ", stok aktarimi " +
                        value.Transferred.ToString("0.###",
                            System.Globalization.CultureInfo.InvariantCulture) +
                        ", fark " +
                        difference.ToString("0.###",
                            System.Globalization.CultureInfo.InvariantCulture) +
                        " " + value.Unit;
                }

                messages.Add(message);
            }
        }
                // MC_DUPLICATE_STOCK_MOVEMENT_V1
        var duplicateStockMovements =
            new System.Collections.Generic.Dictionary<long, System.Collections.Generic.SortedSet<long>>();

        await using (var movementCmd = connection.CreateCommand())
        {
            movementCmd.CommandText = """
                SELECT DISTINCT
                    l.PurchaseInvoiceId,
                    t.RawMaterialStockMovementId
                FROM MCPurchaseInvoiceLines l
                JOIN RawMaterialReceiptTransfers t
                  ON t.PurchaseGoodsReceiptLineId = l.PurchaseGoodsReceiptLineId
                WHERE t.RawMaterialStockMovementId IS NOT NULL
                  AND t.RawMaterialStockMovementId IN
                  (
                      SELECT x.RawMaterialStockMovementId
                      FROM RawMaterialReceiptTransfers x
                      WHERE x.RawMaterialStockMovementId IS NOT NULL
                      GROUP BY x.RawMaterialStockMovementId
                      HAVING COUNT(*) > 1
                  )
                """;

            await using var movementReader =
                await movementCmd.ExecuteReaderAsync();

            while (await movementReader.ReadAsync())
            {
                if (movementReader.IsDBNull(0) ||
                    movementReader.IsDBNull(1))
                    continue;

                var invoiceId =
                    Convert.ToInt64(movementReader.GetValue(0));

                var movementId =
                    Convert.ToInt64(movementReader.GetValue(1));

                if (!duplicateStockMovements.TryGetValue(
                    invoiceId, out var ids))
                {
                    ids = new System.Collections.Generic.SortedSet<long>();
                    duplicateStockMovements[invoiceId] = ids;
                }

                ids.Add(movementId);
            }
        }
                // MC_MOVEMENT_QUANTITY_INVOICE_LINK_V1
        // Mevcut stok aktarim sorun listesine hareket miktari farklarini ekler.
        // Veritabaninda yalnizca SELECT ve PRAGMA kullanilir.

        string? stockMovementQuantityColumn = null;

        await using (var schemaCmd = connection.CreateCommand())
        {
            schemaCmd.CommandText =
                "PRAGMA table_info('RawMaterialStockMovements')";

            await using var schemaReader =
                await schemaCmd.ExecuteReaderAsync();

            var movementColumns =
                new System.Collections.Generic.HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            while (await schemaReader.ReadAsync())
            {
                movementColumns.Add(
                    Convert.ToString(schemaReader.GetValue(1)) ?? "");
            }

            foreach (var candidate in new[]
            {
                "Quantity",
                "MovementQuantity",
                "QuantityText"
            })
            {
                if (movementColumns.Contains(candidate))
                {
                    stockMovementQuantityColumn = candidate;
                    break;
                }
            }
        }

        if (stockMovementQuantityColumn is null)
        {
            throw new InvalidOperationException(
                "Stok hareketi miktar sutunu belirlenemedi. " +
                "Fatura kontrolu guvenli sekilde tamamlanamadi.");
        }

        await using (var movementAuditCmd = connection.CreateCommand())
        {
            movementAuditCmd.CommandText =
                "SELECT DISTINCT " +
                "l.PurchaseInvoiceId, " +
                "t.Id, " +
                "t.RawMaterialStockMovementId, " +
                "CAST(t.Quantity AS TEXT), " +
                "CAST(m.\"" + stockMovementQuantityColumn + "\" AS TEXT), " +
                "m.Id " +
                "FROM MCPurchaseInvoiceLines l " +
                "JOIN RawMaterialReceiptTransfers t " +
                "ON t.PurchaseGoodsReceiptLineId = " +
                "l.PurchaseGoodsReceiptLineId " +
                "LEFT JOIN RawMaterialStockMovements m " +
                "ON m.Id = t.RawMaterialStockMovementId " +
                "WHERE t.RawMaterialStockMovementId IS NOT NULL " +
                "ORDER BY l.PurchaseInvoiceId, t.Id";

            await using var movementAuditReader =
                await movementAuditCmd.ExecuteReaderAsync();

            while (await movementAuditReader.ReadAsync())
            {
                if (movementAuditReader.IsDBNull(0) ||
                    movementAuditReader.IsDBNull(1))
                    continue;

                var invoiceId = Convert.ToInt64(
                    movementAuditReader.GetValue(0));

                var transferId = Convert.ToInt64(
                    movementAuditReader.GetValue(1));

                var movementId =
                    movementAuditReader.IsDBNull(2)
                    ? 0L
                    : Convert.ToInt64(
                        movementAuditReader.GetValue(2));

                // Eksik stok hareketi mevcut ayri kontrolde izleniyor.
                if (movementAuditReader.IsDBNull(5))
                    continue;

                var transferText =
                    movementAuditReader.IsDBNull(3)
                    ? ""
                    : Convert.ToString(
                        movementAuditReader.GetValue(3)) ?? "";

                var movementText =
                    movementAuditReader.IsDBNull(4)
                    ? ""
                    : Convert.ToString(
                        movementAuditReader.GetValue(4)) ?? "";

                var transferValid =
                    TryAuditQuantity(
                        transferText, out var transferQty);

                var movementValid =
                    TryAuditQuantity(
                        movementText, out var movementQty);

                if (transferValid &&
                    movementValid &&
                    transferQty >= 0 &&
                    movementQty >= 0 &&
                    transferQty == movementQty)
                {
                    continue;
                }

                if (!receiptTransferDetails.TryGetValue(
                    invoiceId, out var movementMessages))
                {
                    movementMessages =
                        new System.Collections.Generic.List<string>();

                    receiptTransferDetails[invoiceId] =
                        movementMessages;
                }

                string movementMessage;

                if (!transferValid ||
                    !movementValid ||
                    transferQty < 0 ||
                    movementQty < 0)
                {
                    movementMessage =
                        "Stok hareketi miktari kontrol edilmeli" +
                        " - Aktarim ID: " + transferId +
                        ", Hareket ID: " + movementId;
                }
                else
                {
                    var movementDifference =
                        transferQty - movementQty;

                    movementMessage =
                        "Stok hareketi miktar farki" +
                        " - Aktarim ID: " + transferId +
                        ", Hareket ID: " + movementId +
                        ", Aktarim: " +
                        transferQty.ToString(
                            "0.###",
                            System.Globalization.CultureInfo.InvariantCulture) +
                        ", Hareket: " +
                        movementQty.ToString(
                            "0.###",
                            System.Globalization.CultureInfo.InvariantCulture) +
                        ", Fark: " +
                        movementDifference.ToString(
                            "0.###",
                            System.Globalization.CultureInfo.InvariantCulture);
                }

                if (!movementMessages.Contains(movementMessage))
                    movementMessages.Add(movementMessage);
            }
        }
                // MC_CARI_AMOUNT_RECONCILIATION_V1
        var cariAmountIssues =
            new System.Collections.Generic.Dictionary<long, string>();

        await using (var cariAmountCmd = connection.CreateCommand())
        {
            cariAmountCmd.CommandText = """
                SELECT
                    i.Id,
                    CAST(i.GrandTotal AS TEXT),
                    COALESCE(CAST(i.Currency AS TEXT), ''),
                    CAST(m.Debit AS TEXT),
                    CAST(m.Credit AS TEXT),
                    COALESCE(CAST(m.Currency AS TEXT), '')
                FROM MCPurchaseInvoices i
                JOIN MCPurchaseInvoiceCariPostings p
                    ON p.PurchaseInvoiceId = i.Id
                JOIN MCCariMovements m
                    ON m.Id = p.CariMovementId
                WHERE
                    (
                        SELECT COUNT(*)
                        FROM MCPurchaseInvoiceCariPostings x
                        WHERE x.PurchaseInvoiceId = i.Id
                    ) = 1
                ORDER BY i.Id
                """;

            await using var cariAmountReader =
                await cariAmountCmd.ExecuteReaderAsync();

            while (await cariAmountReader.ReadAsync())
            {
                var invoiceId =
                    Convert.ToInt64(cariAmountReader.GetValue(0));

                var invoiceText = cariAmountReader.IsDBNull(1)
                    ? ""
                    : Convert.ToString(cariAmountReader.GetValue(1)) ?? "";

                var invoiceCurrency = cariAmountReader.IsDBNull(2)
                    ? ""
                    : Convert.ToString(cariAmountReader.GetValue(2)) ?? "";

                var debitText = cariAmountReader.IsDBNull(3)
                    ? ""
                    : Convert.ToString(cariAmountReader.GetValue(3)) ?? "";

                var creditText = cariAmountReader.IsDBNull(4)
                    ? ""
                    : Convert.ToString(cariAmountReader.GetValue(4)) ?? "";

                var movementCurrency = cariAmountReader.IsDBNull(5)
                    ? ""
                    : Convert.ToString(cariAmountReader.GetValue(5)) ?? "";

                var invoiceValid =
                    TryAuditQuantity(invoiceText, out var invoiceAmount);

                var debitValid =
                    TryAuditQuantity(debitText, out var debitAmount);

                var creditValid =
                    TryAuditQuantity(creditText, out var creditAmount);

                // NULL borc/alacak alanlari sifir kabul edilir.
                if (string.IsNullOrWhiteSpace(debitText))
                {
                    debitAmount = 0m;
                    debitValid = true;
                }

                if (string.IsNullOrWhiteSpace(creditText))
                {
                    creditAmount = 0m;
                    creditValid = true;
                }

                if (!invoiceValid ||
                    !debitValid ||
                    !creditValid ||
                    invoiceAmount <= 0m ||
                    debitAmount < 0m ||
                    creditAmount < 0m)
                {
                    cariAmountIssues[invoiceId] =
                        "Cari mutabakat: Tutar okunamadi veya gecersiz.";

                    continue;
                }

                if (string.IsNullOrWhiteSpace(invoiceCurrency) ||
                    string.IsNullOrWhiteSpace(movementCurrency) ||
                    !string.Equals(
                        invoiceCurrency.Trim(),
                        movementCurrency.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    cariAmountIssues[invoiceId] =
                        "Cari mutabakat: Para birimi uyusmazligi veya eksik. " +
                        "Fatura: " + invoiceCurrency +
                        ", Cari: " + movementCurrency;

                    continue;
                }

                if ((debitAmount > 0m && creditAmount > 0m) ||
                    (debitAmount == 0m && creditAmount == 0m))
                {
                    cariAmountIssues[invoiceId] =
                        "Cari mutabakat: Borc ve alacak hareketi kontrol edilmeli.";

                    continue;
                }

                // Yalnizca tutar buyuklugu karsilastirilir.
                // Muhasebe yonunun dogrulugu bu kontrolun disindadir.
                var cariNetAmount =
                    Math.Abs(debitAmount - creditAmount);

                if (invoiceAmount != cariNetAmount)
                {
                    var amountDifference =
                        invoiceAmount - cariNetAmount;

                    var culture =
                        System.Globalization.CultureInfo.InvariantCulture;

                    cariAmountIssues[invoiceId] =
                        "Cari tutar farki: Fatura " +
                        invoiceAmount.ToString("0.##", culture) +
                        ", Cari " +
                        cariNetAmount.ToString("0.##", culture) +
                        ", Fark " +
                        amountDifference.ToString("0.##", culture) +
                        " " + invoiceCurrency;
                }
            }
        }
                // MC_DUPLICATE_CARI_MOVEMENT_V1
        // Ayni cari hareketin birden fazla aktarimda
        // kullanilmasini fatura bazinda kontrol eder.

        var duplicateCariDetails =
            new System.Collections.Generic.Dictionary<
                long,
                System.Collections.Generic.SortedSet<long>>();

        await using (var duplicateCariCmd = connection.CreateCommand())
        {
            duplicateCariCmd.CommandText = """
                SELECT DISTINCT
                    p.PurchaseInvoiceId,
                    p.CariMovementId
                FROM MCPurchaseInvoiceCariPostings p
                WHERE p.CariMovementId IS NOT NULL
                  AND p.CariMovementId IN
                  (
                      SELECT x.CariMovementId
                      FROM MCPurchaseInvoiceCariPostings x
                      WHERE x.CariMovementId IS NOT NULL
                      GROUP BY x.CariMovementId
                      HAVING COUNT(*) > 1
                  )
                ORDER BY
                    p.PurchaseInvoiceId,
                    p.CariMovementId
                """;

            await using var duplicateCariReader =
                await duplicateCariCmd.ExecuteReaderAsync();

            while (await duplicateCariReader.ReadAsync())
            {
                if (duplicateCariReader.IsDBNull(0) ||
                    duplicateCariReader.IsDBNull(1))
                {
                    continue;
                }

                var invoiceId = Convert.ToInt64(
                    duplicateCariReader.GetValue(0));

                var movementId = Convert.ToInt64(
                    duplicateCariReader.GetValue(1));

                if (!duplicateCariDetails.TryGetValue(
                    invoiceId, out var movementIds))
                {
                    movementIds =
                        new System.Collections.Generic.SortedSet<long>();

                    duplicateCariDetails[invoiceId] = movementIds;
                }

                movementIds.Add(movementId);
            }
        }

        // Mevcut cari tutar hatalarini silmeden
        // mukerrer hareket uyarilarini da ekle.

        foreach (var duplicateEntry in duplicateCariDetails)
        {
            var invoiceId = duplicateEntry.Key;

            var duplicateMessage =
                "Mukerrer cari hareket ID: " +
                string.Join(", ", duplicateEntry.Value);

            if (cariAmountIssues.TryGetValue(
                invoiceId, out var existingCariIssue))
            {
                cariAmountIssues[invoiceId] =
                    existingCariIssue + "; " + duplicateMessage;
            }
            else
            {
                cariAmountIssues[invoiceId] = duplicateMessage;
            }
        }
                // MC_CARI_DIRECTION_FINAL_V3
        await using (var cmdDirection = connection.CreateCommand())
        {
            cmdDirection.CommandText = """
                SELECT p.PurchaseInvoiceId,
                       p.CariMovementId,
                       CAST(m.Debit AS TEXT),
                       CAST(m.Credit AS TEXT)
                FROM MCPurchaseInvoiceCariPostings p
                JOIN MCCariMovements m ON m.Id = p.CariMovementId
                WHERE (
                    SELECT COUNT(*)
                    FROM MCPurchaseInvoiceCariPostings x
                    WHERE x.PurchaseInvoiceId = p.PurchaseInvoiceId
                ) = 1
                """;

            await using var readerDirection =
                await cmdDirection.ExecuteReaderAsync();

            while (await readerDirection.ReadAsync())
            {
                var invoiceId = Convert.ToInt64(readerDirection.GetValue(0));
                var movementId = Convert.ToInt64(readerDirection.GetValue(1));

                var debitText = readerDirection.IsDBNull(2)
                    ? "" : Convert.ToString(readerDirection.GetValue(2)) ?? "";

                var creditText = readerDirection.IsDBNull(3)
                    ? "" : Convert.ToString(readerDirection.GetValue(3)) ?? "";

                decimal debit = 0m;
                decimal credit = 0m;

                if (!string.IsNullOrWhiteSpace(debitText) &&
                    !TryAuditQuantity(debitText, out debit))
                    continue;

                if (!string.IsNullOrWhiteSpace(creditText) &&
                    !TryAuditQuantity(creditText, out credit))
                    continue;

                if (debit <= 0m || credit != 0m)
                    continue;

                var warning = "Cari borc/alacak yonu kontrol edilmeli" +
                    " - Hareket ID: " + movementId;

                if (cariAmountIssues.TryGetValue(invoiceId, out var previous))
                    cariAmountIssues[invoiceId] = previous + "; " + warning;
                else
                    cariAmountIssues[invoiceId] = warning;
            }
        }
        var html = new StringBuilder();

        html.Append("<section style='padding:20px'>");

        html.Append("<h2>Satın Alma Kontrol Merkezi</h2>");

        html.Append("<p style='color:#64748b'>");
        html.Append("Fatura, cari hesap, mal kabul ve hammadde stok ");
        html.Append("hareketlerinin tek noktadan kontrolü.");
        html.Append("</p>");

        html.Append("<div style='display:flex;flex-wrap:wrap;gap:16px;margin:22px 0'>");

        Card(html,
            "Satın Alma Faturaları",
            invoices,
            "Sistemde kayıtlı toplam satın alma faturası.",
            "/MCPurchaseCariAudit/InvoiceList",
            "Faturaları İncele");

        Card(html,
            "Fatura - Mal Kabul",
            invoiceLinesWithoutReceipt,
            "Mal kabul satırıyla bağlantısı bulunmayan fatura satırları.",
            "/MCPurchaseInvoiceReceiptAudit?onlyProblems=true",
            "Miktar Kontrolü");

        Card(html,
            "Mal Kabul - Stok",
            receiptLinesWithoutTransfer,
            "Henüz stok aktarım bağlantısı bulunmayan mal kabul satırları.",
            "/MCReceiptStockAudit?onlyProblems=true",
            "Stok Kontrolü");

        Card(html,
            "Fatura - Cari Hesap",
            invoicesWithoutPosting,
            "Cari aktarım bağlantısı bulunmayan satın alma faturaları.",
            "/MCProcurementMissingCari",
            "Cari Mutabakat");

        html.Append("</div>");

                // MC_PROCUREMENT_PRIORITY_PANEL_V1
        html.Append("<h3 style='margin-top:28px'>Öncelikli İşlemler</h3>");
        html.Append("<p style='color:#64748b'>");
        html.Append("Aşağıdaki sayılar otomatik düzeltme değil, inceleme önceliğidir.");
        html.Append("</p>");

        html.Append("<div style='overflow-x:auto'>");
        html.Append("<table style='width:100%;border-collapse:collapse;background:white'>");
        html.Append("<thead><tr style='background:#24496c;color:white'>");
        html.Append("<th style='padding:12px;text-align:left'>Öncelik</th>");
        html.Append("<th style='padding:12px;text-align:left'>Kontrol Konusu</th>");
        html.Append("<th style='padding:12px;text-align:right'>Kayıt</th>");
        html.Append("<th style='padding:12px;text-align:left'>İşlem</th>");
        html.Append("</tr></thead><tbody>");

        void PriorityRow(
            string priority,
            string title,
            long count,
            string link,
            string background)
        {
            html.Append("<tr style='border-bottom:1px solid #e2e8f0;background:");
            html.Append(background);
            html.Append("'>");

            html.Append("<td style='padding:12px;font-weight:600'>");
            html.Append(H(priority));
            html.Append("</td>");

            html.Append("<td style='padding:12px'>");
            html.Append(H(title));
            html.Append("</td>");

            html.Append("<td style='padding:12px;text-align:right;font-weight:700'>");
            html.Append(count);
            html.Append("</td>");

            html.Append("<td style='padding:12px'><a href='");
            html.Append(H(link));
            html.Append("'>Görüntüle</a></td></tr>");
        }

        PriorityRow(
            "KRİTİK",
            "Cari hareketi bulunmayan fatura aktarımı",
            postingsWithoutMovement,
            "/MCPurchaseCariAudit",
            "#fff1f2");

        PriorityRow(
            "KRİTİK",
            "Stok hareketi bulunmayan mal kabul aktarımı",
            transferLinesWithoutMovement,
            "/MCReceiptStockAudit?onlyProblems=true",
            "#fff1f2");

        PriorityRow(
            "YÜKSEK",
            "Cari hesaba aktarılmamış satın alma faturası",
            invoicesWithoutPosting,
            "/MCProcurementMissingCari",
            "#fff7ed");

        PriorityRow(
            "YÜKSEK",
            "Stok aktarım bağlantısı bulunmayan mal kabul satırı",
            receiptLinesWithoutTransfer,
            "/MCReceiptStockAudit?onlyProblems=true",
            "#fff7ed");

        PriorityRow(
            "KONTROL",
            "Mal kabul bağlantısı bulunmayan fatura satırı",
            invoiceLinesWithoutReceipt,
            "/MCPurchaseInvoiceReceiptAudit?onlyProblems=true",
            "#fefce8");

        html.Append("</tbody></table></div>");

        var priorityTotal =
            postingsWithoutMovement +
            transferLinesWithoutMovement +
            invoicesWithoutPosting +
            receiptLinesWithoutTransfer +
            invoiceLinesWithoutReceipt;

        html.Append("<p style='margin-top:12px;font-weight:600'>");
        html.Append("Toplam inceleme konusu: ");
        html.Append(priorityTotal);
        html.Append("</p>");

        html.Append("<p style='color:#64748b;font-size:13px'>");
        html.Append("Not: Bu toplam farklı tablolardaki kontrol sonuçlarının ");
        html.Append("toplamıdır; benzersiz işlem sayısı değildir.");
        html.Append("</p>");
                // MC_PROCUREMENT_STOCK_EXPORT_LINK_V1
        html.Append("<div style='margin:18px 0;padding:16px;");
        html.Append("background:#f8fafc;border:1px solid #dbe4ee;");
        html.Append("border-radius:10px'>");

        html.Append("<h3 style='margin:0 0 12px'>Depo Kontrol Raporu</h3>");
        html.Append("<p>Mal kabul ve stok aktarim sorunlarini Excel'de inceleyin.</p>");

        html.Append("<a href='/MCReceiptStockAudit?onlyProblems=true&amp;exportExcel=1' ");
        html.Append("style='display:inline-block;padding:11px 16px;");
        html.Append("background:#24496c;color:white;text-decoration:none;");
        html.Append("border-radius:7px;font-weight:600'>");
        html.Append("Sorunlu Stok Kayitlarini Excel'e Aktar");
        html.Append("</a></div>");
                // MC_PROCUREMENT_WORKFLOW_STATUS_V1
        html.Append("<section style='margin:20px 0;padding:18px;");
        html.Append("background:white;border:1px solid #dbe4ee;");
        html.Append("border-radius:10px'>");

        html.Append("<h3 style='margin-top:0'>Satın Alma İşlem Durumları</h3>");

        html.Append("<p><strong style='color:#b45309'>BEKLİYOR:</strong> ");
        html.Append("İşlemin bir sonraki aşaması henüz kaydedilmemiş olabilir.");
        html.Append("</p>");

        html.Append("<p><strong style='color:#b91c1c'>KONTROL GEREKİYOR:</strong> ");
        html.Append("Miktar, birim, ürün veya kayıt bağlantısı uyuşmazlığı ");
        html.Append("ilgili detay ekranında tespit edilmiştir.");
        html.Append("</p>");

        html.Append("<p><strong style='color:#15803d'>TAMAMLANDI:</strong> ");
        html.Append("Gerekli aşamaların tamamı doğrulandıktan sonra ");
        html.Append("kullanılabilecek durumdur.");
        html.Append("</p>");

        html.Append("<p style='color:#64748b;font-size:13px'>");
        html.Append("Bu panel henüz faturaları otomatik olarak ");
        html.Append("tamamlandı şeklinde sınıflandırmaz. ");
        html.Append("Eksik bağlantılar tek başına kesin hata değildir.");
        html.Append("</p></section>");
                // MC_PROCUREMENT_SEARCH_FILTER_LINKS_V2
        var safeSearch = System.Net.WebUtility.UrlEncode(selectedInvoiceSearch);
        html.Append("<h3 style='margin-top:25px'>Fatura Bazlı İşlem Takibi</h3>");
        html.Append("<form method='get' action='/MCProcurementAudit' ");
        html.Append("style='display:flex;gap:10px;margin:12px 0;flex-wrap:wrap'>");

        html.Append("<input type='hidden' name='workflowFilter' value='");
        html.Append(H(selectedWorkflowFilter));
        html.Append("'>");

        html.Append("<input type='text' name='invoiceSearch' placeholder='Fatura numarasi ara' ");
        html.Append("value='");
        html.Append(H(selectedInvoiceSearch));
        html.Append("' style='padding:10px;border:1px solid #cbd5e1;border-radius:6px;min-width:240px'>");

        html.Append("<button type='submit' style='padding:10px 16px;");
        html.Append("background:#24496c;color:white;border:0;border-radius:6px'>");
        html.Append("Fatura Ara</button></form>");

                        // MC_WORKFLOW_COUNTERS_FIXED_V2
        var totalMatchingInvoices = 0;
        var waitingInvoices = 0;
        var issueInvoices = 0;
        // MC_WORKFLOW_COMPLETION_GUARD_V1
        // MC_TRANSFER_ISSUE_INTEGRATION_V2
        var filteredTotal = 0;

        foreach (var record in workflowRows)
        {
            if (selectedInvoiceSearch.Length > 0 &&
                !record.Number.Contains(
                    selectedInvoiceSearch,
                    StringComparison.OrdinalIgnoreCase))
                continue;
            totalMatchingInvoices++;

            var recordIssue =
                (record.Lines == 0 || record.MissingMovements > 0 ||
                record.MissingCariMovements > 0 ||
                record.CariPostings > 1 ||
                (quantityIssues.Contains(record.Id) ||
                (receiptTransferDetails.ContainsKey(record.Id) || duplicateStockMovements.ContainsKey(record.Id)) || duplicateReceiptInvoices.Contains(record.Id))) || cariAmountIssues.ContainsKey(record.Id);

            var recordWaiting =
                !recordIssue &&
                (record.MissingReceipts > 0 ||
                 record.MissingTransfers > 0 ||
                 record.CariPostings == 0);
            if (recordIssue)
                issueInvoices++;
            else if (recordWaiting)
                waitingInvoices++;


            if (selectedWorkflowFilter == "issues" && !recordIssue)
                continue;

            if (selectedWorkflowFilter == "waiting" && !recordWaiting)
                continue;
            // MC_WORKFLOW_LINKED_FILTER_V1
            if (selectedWorkflowFilter == "linked" &&
                (recordIssue || recordWaiting))
                continue;

            filteredTotal++;
        }
                html.Append("<div style='display:flex;gap:12px;flex-wrap:wrap;margin:14px 0'>");

        html.Append("<div style='padding:14px;background:#eff6ff;border-radius:8px'>");
        html.Append("<strong>Arama Sonucu:</strong> ");
        html.Append(totalMatchingInvoices);
        html.Append("</div>");

        html.Append("<div style='padding:14px;background:#fef3c7;border-radius:8px'>");
        html.Append("<strong>Bekleyen:</strong> ");
        html.Append(waitingInvoices);
        html.Append("</div>");

        html.Append("<div style='padding:14px;background:#fee2e2;border-radius:8px'>");
        html.Append("<strong>Kontrol Gerektiren:</strong> ");
        html.Append(issueInvoices);
        html.Append("</div>");

        html.Append("</div>");
        var safeFilter = System.Net.WebUtility.UrlEncode(selectedWorkflowFilter);
        

        html.Append("<div style='display:flex;gap:12px;margin:12px 0'>");

        if (page > 1)
        {
            html.Append("<a href='/MCProcurementAudit?workflowFilter=");
            html.Append(safeFilter);
            html.Append("&amp;invoiceSearch="); html.Append(safeSearch); html.Append("&amp;page=");
            html.Append(page - 1);
            html.Append("'>Önceki Sayfa</a>");
        }

        html.Append("<span>Sayfa ");
        html.Append(page);
        html.Append("</span>");

        if ((long)filteredTotal > offset + pageSize)
        {
            html.Append("<a href='/MCProcurementAudit?workflowFilter=");
            html.Append(safeFilter);
            html.Append("&amp;invoiceSearch="); html.Append(safeSearch); html.Append("&amp;page=");
            html.Append(page + 1);
            html.Append("'>Sonraki Sayfa</a>");
        }

        html.Append("</div>");

        html.Append("<div style='display:flex;gap:10px;flex-wrap:wrap;margin:12px 0'>");
        html.Append("<a href='/MCProcurementAudit?workflowFilter=all&amp;invoiceSearch="); html.Append(safeSearch); html.Append("'>Tüm Faturalar</a>");
        html.Append("<a href='/MCProcurementAudit?workflowFilter=waiting&amp;invoiceSearch="); html.Append(safeSearch); html.Append("'>Bekleyen İşlemler</a>");
        html.Append("<a href='/MCProcurementAudit?workflowFilter=issues&amp;invoiceSearch="); html.Append(safeSearch); html.Append("'>Kontrol Gerektiren</a>");
        html.Append("<a href='/MCProcurementAudit?workflowFilter=linked&amp;invoiceSearch=");
        html.Append(safeSearch);
        html.Append("'>Baglantilari Tamamlananlar</a>");
        html.Append("</div>");

        html.Append("<div style='overflow-x:auto'>");
        html.Append("<table style='width:100%;border-collapse:collapse;background:white'>");
        html.Append("<thead><tr style='background:#24496c;color:white'>");

        foreach (var heading in new[]
        {
            "Fatura No", "Tutar", "Para Birimi",
            "Fatura Satırı", "Eksik Mal Kabul",
            "Eksik Stok Aktarımı", "Eksik Stok Hareketi",
            "Cari Aktarım", "Durum", "Sorun Nedeni", "Detay"
        })
        {
            html.Append("<th style='padding:10px;text-align:left'>");
            html.Append(H(heading));
            html.Append("</th>");
        }

        html.Append("</tr></thead><tbody>");

        var filteredPosition = 0;
        foreach (var item in workflowRows)
        {
            if (selectedInvoiceSearch.Length > 0 &&
                !item.Number.Contains(
                    selectedInvoiceSearch,
                    StringComparison.OrdinalIgnoreCase))
                continue;
            var issues =
                item.Lines == 0 || item.MissingMovements > 0 ||
                item.MissingCariMovements > 0 ||
                item.CariPostings > 1;

            var waiting =
                item.MissingReceipts > 0 ||
                item.MissingTransfers > 0 ||
                item.CariPostings == 0;

                        // MC_PROCUREMENT_ISSUE_REASON_V1
            var reasons = new System.Collections.Generic.List<string>();

            if (item.Lines == 0)
                reasons.Add("Fatura satiri bulunmuyor");

            if (item.MissingReceipts > 0)
                reasons.Add("Mal kabul baglantisi bekleniyor");

                        if (receiptTransferDetails.TryGetValue(item.Id, out var transferMessages))
                reasons.AddRange(transferMessages);
            if (item.MissingTransfers > 0)
                reasons.Add("Stok aktarimi bekleniyor");

            if (item.MissingMovements > 0)
                reasons.Add("Stok hareketi eksik");

            if (item.CariPostings == 0)
                reasons.Add("Cari aktarim bekleniyor");

            if (item.CariPostings > 1)
                reasons.Add("Mukerrer cari aktarim");

            if (item.MissingCariMovements > 0)
                reasons.Add("Cari hareket eksik");

                        if (duplicateReceiptDetails.TryGetValue(item.Id, out var duplicateIds))
                reasons.Add("Mukerrer mal kabul satiri ID: " + string.Join(", ", duplicateIds));
                        if (quantityIssues.Contains(item.Id))
            {
                if (quantityDetails.TryGetValue(item.Id, out var quantityMessages)
                    && quantityMessages.Count > 0)
                {
                    reasons.AddRange(quantityMessages);
                }
                else
                {
                    reasons.Add("Fatura - mal kabul miktar veya birim kontrolu");
                }
            }

            if (reasons.Count == 0)
                reasons.Add("Baglantilar mevcut; detay kontrolu gerekli");

                        if (duplicateStockMovements.TryGetValue(item.Id, out var duplicateMovementIds))
            {
                reasons.Add(
                    "Mukerrer stok hareketi ID: " +
                    string.Join(", ", duplicateMovementIds));
            }
                        if (cariAmountIssues.TryGetValue(item.Id, out var cariIssue))
                reasons.Add(cariIssue);
            var reasonText = string.Join("; ", reasons);
            var state = cariAmountIssues.ContainsKey(item.Id) ? "KONTROL GEREKIYOR" : (receiptTransferDetails.ContainsKey(item.Id) || duplicateStockMovements.ContainsKey(item.Id)) ? "KONTROL GEREKIYOR" : (issues || quantityIssues.Contains(item.Id) || duplicateReceiptInvoices.Contains(item.Id))
                ? "KONTROL GEREKIYOR"
                : waiting
                    ? "BEKLIYOR"
                    : "BAGLANTILAR TAMAM - DETAY KONTROLU GEREKLI";
            var isIssue = (issues || quantityIssues.Contains(item.Id) || duplicateReceiptInvoices.Contains(item.Id) || (receiptTransferDetails.ContainsKey(item.Id) || duplicateStockMovements.ContainsKey(item.Id))) || cariAmountIssues.ContainsKey(item.Id);
            var isWaiting = !isIssue && waiting;

            if (selectedWorkflowFilter == "issues" && !isIssue)
                continue;

            if (selectedWorkflowFilter == "waiting" && !isWaiting)
                continue;
            if (selectedWorkflowFilter == "linked" &&
                (isIssue || isWaiting))
                continue;
            filteredPosition++;

            if ((long)filteredPosition <= offset)
                continue;

            if ((long)filteredPosition > offset + pageSize)
                continue;

            html.Append("<tr style='border-bottom:1px solid #e2e8f0'>");

            foreach (var cell in new[]
            {
                item.Number,
                item.Amount,
                item.Currency,
                item.Lines.ToString(),
                item.MissingReceipts.ToString(),
                item.MissingTransfers.ToString(),
                item.MissingMovements.ToString(),
                item.CariPostings.ToString(), state, reasonText
            })
            {
                html.Append("<td style='padding:10px'>");
                html.Append(H(cell));
                html.Append("</td>");
            }

            html.Append("<td style='padding:10px'><a href='/MCPurchaseCariAudit/InvoiceDetail/");
            html.Append(item.Id);
            html.Append("'>İncele</a></td></tr>");
        }

        if (filteredTotal == 0 || offset >= filteredTotal)
            html.Append("<tr><td colspan='11' style='padding:15px'>Secilen filtreye uygun fatura bu sayfada bulunmuyor.</td></tr>");

        html.Append("</tbody></table></div>");
        html.Append("<p style='color:#64748b;font-size:13px'>");
        html.Append("Faturalar 50 kayitlik sayfalar halinde listelenir. Bağlantı kontrolleri ");
        html.Append("miktar, ürün ve tutar mutabakatının yerine geçmez.");
        html.Append("</p>");
        html.Append("<h3>İşlem Özeti</h3>");

        html.Append("<table style='width:100%;max-width:850px;");
        html.Append("border-collapse:collapse;background:white'>");

        void Row(string label, long value)
        {
            html.Append("<tr style='border-bottom:1px solid #e2e8f0'>");
            html.Append("<td style='padding:12px'>");
            html.Append(H(label));
            html.Append("</td><td style='padding:12px;text-align:right'>");
            html.Append(value);
            html.Append("</td></tr>");
        }

        Row("Toplam satın alma faturası", invoices);
        Row("Toplam fatura satırı", invoiceLines);
        Row("Toplam mal kabul satırı", receiptLines);
        Row("Toplam stok aktarım kaydı", transfers);
        Row("Toplam cari aktarım kaydı", postings);

        Row("Mal kabul bağlantısı eksik fatura satırı",
            invoiceLinesWithoutReceipt);

        Row("Stok aktarım bağlantısı bulunmayan mal kabul satırı",
            receiptLinesWithoutTransfer);

        Row("Stok hareketi bulunmayan aktarım",
            transferLinesWithoutMovement);

        Row("Cari aktarımı bulunmayan fatura",
            invoicesWithoutPosting);

        Row("Cari hareketi bulunmayan aktarım",
            postingsWithoutMovement);

        html.Append("</table>");

        html.Append("<div style='margin-top:20px;padding:16px;");
        html.Append("background:#eff6ff;border-radius:10px'>");

        html.Append("<strong>Önemli:</strong> Bu sayfadaki sayılar ");
        html.Append("eksik bağlantıları gösterir. Tek başına miktar ");
        html.Append("veya tutar uyuşmazlığını kanıtlamaz. ");
        html.Append("Ayrıntılı farklar ilgili kontrol ekranlarında incelenir.");
        html.Append("</div>");

        html.Append("<p style='margin-top:15px;color:#64748b'>");
        html.Append("Bilgiler gerçek veritabanından salt okunur alınır. ");
        html.Append("Hiçbir stok veya finans kaydı değiştirilmez.");
        html.Append("</p>");

        html.Append("</section>");

        ViewData["Title"] = "Satın Alma Kontrol Merkezi";

        return View(
            "~/Views/MCPurchaseCariAudit/Index.cshtml",
            html.ToString()
        );
    }
}
