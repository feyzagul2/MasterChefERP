using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using MasterChefTakip.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace MasterChefTakip.Controllers;

// MC_RECEIPT_MATCHING_V1
// MC_RECEIPT_READINESS_V4
[Authorize(Roles = AppRoles.Admin)]
[Route("MCReceiptMatching")]
public sealed class MCReceiptMatchingController : Controller
{
    private readonly IConfiguration _configuration;

    public MCReceiptMatchingController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private static string? Normalize(string? value)
    {
        return (value ?? "").Trim().ToUpperInvariant() switch
        {
            "KG" or "KILOGRAM" or "K?LOGRAM" => "KG",
            "L" or "LT" or "LITRE" or "L?TRE" => "L",
            _ => null
        };
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var model = new MCReceiptMatchingPage();
        var connectionString =
            _configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
            return StatusCode(500, "Veritaban? ba?lant?s? eksik.");

        await using var db = new SqliteConnection(connectionString);
        await db.OpenAsync();

        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT
                    l.Id,
                    l.PurchaseGoodsReceiptId,
                    COALESCE(l.ProductDescription,''),
                    COALESCE(l.Unit,''),
                    l.AcceptedQuantity,
                    COALESCE(r.Status,''),
                    (
                        SELECT COUNT(*)
                        FROM MCMaterialReceiptLinks x
                        WHERE x.PurchaseGoodsReceiptLineId = l.Id
                    ) AS ReceiptLinks,
                    (
                        SELECT COUNT(*)
                        FROM MCMaterialReceiptLinks x
                        JOIN MCMaterialStockCardLinks y
                          ON y.MaterialCatalogId = x.MaterialCatalogId
                        JOIN RawMaterials m
                          ON m.Id = y.RawMaterialId
                        WHERE x.PurchaseGoodsReceiptLineId = l.Id
                          AND m.IsActive = 1
                          AND UPPER(TRIM(m.Unit)) IN
                              ('KG','KILOGRAM','K?LOGRAM',
                               'L','LT','LITRE','L?TRE')
                          AND (
                              CASE
                                  WHEN UPPER(TRIM(m.Unit)) IN
                                      ('KG','KILOGRAM','K?LOGRAM')
                                  THEN 'KG'
                                  ELSE 'L'
                              END
                          ) = (
                              CASE
                                  WHEN UPPER(TRIM(l.Unit)) IN
                                      ('KG','KILOGRAM','K?LOGRAM')
                                  THEN 'KG'
                                  WHEN UPPER(TRIM(l.Unit)) IN
                                      ('L','LT','LITRE','L?TRE')
                                  THEN 'L'
                                  ELSE ''
                              END
                          )
                    ) AS CompatibleLinks
                FROM PurchaseGoodsReceiptLines l
                LEFT JOIN PurchaseGoodsReceipts r
                  ON r.Id = l.PurchaseGoodsReceiptId
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM RawMaterialReceiptTransfers t
                    WHERE t.PurchaseGoodsReceiptLineId = l.Id
                )
                ORDER BY l.Id DESC
                LIMIT 200";

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var item = new MCReceiptMatchingLine
                {
                    Id = Convert.ToInt64(reader.GetValue(0)),
                    ReceiptId = Convert.ToInt64(reader.GetValue(1)),
                    Description = reader.GetString(2),
                    Unit = reader.GetString(3),
                    ReceiptStatus = reader.GetString(5)
                };

                var issues = new List<string>();

                if (item.ReceiptStatus != "Recorded")
                    issues.Add("Mal kabul Recorded durumunda de?il");

                if (reader.IsDBNull(4) ||
                    !decimal.TryParse(
                        Convert.ToString(
                            reader.GetValue(4),
                            CultureInfo.InvariantCulture),
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out var quantity))
                {
                    issues.Add("Kabul miktar? ge?ersiz");
                }
                else
                {
                    item.Quantity = quantity;

                    if (quantity <= 0m ||
                        decimal.Round(quantity, 4) != quantity)
                        issues.Add("Kabul miktar? uygun de?il");
                }

                if (Normalize(item.Unit) == null)
                    issues.Add("?l?? birimi desteklenmiyor");

                if (Convert.ToInt64(reader.GetValue(6)) == 0)
                    issues.Add("Katalog ba?lant?s? yok");
                else if (Convert.ToInt64(reader.GetValue(7)) == 0)
                    issues.Add("Aktif ve uyumlu hammadde ba?lant?s? yok");

                item.Reasons = issues.Count == 0
                    ? "?n kontroller uygun; depo ve aktar?m onay? gerekli"
                    : string.Join("; ", issues);

                item.PrecheckReady = issues.Count == 0;
                model.Lines.Add(item);
            }
        }

        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT Id, COALESCE(Code,''),
                       COALESCE(Name,''), COALESCE(Unit,'')
                FROM RawMaterials
                WHERE IsActive = 1
                ORDER BY Name
                LIMIT 500";

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                model.Materials.Add(new MCReceiptMatchingMaterial
                {
                    Id = Convert.ToInt64(reader.GetValue(0)),
                    Code = reader.GetString(1),
                    Name = reader.GetString(2),
                    Unit = reader.GetString(3)
                });
            }
        }

        return View(model);
    }

    // MC_RECEIPT_STOCK_SUGGESTION_V1
    [HttpGet("StockSuggestion")]
    public async Task<IActionResult> StockSuggestion(
        [FromQuery] long lineId)
    {
        if (lineId <= 0)
            return BadRequest("Gecersiz irsaliye satiri.");

        var connectionString =
            HttpContext.RequestServices
                .GetRequiredService<IConfiguration>()
                .GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
            return StatusCode(500, "Veritabani baglantisi bulunamadi.");

        await using var db =
            new Microsoft.Data.Sqlite.SqliteConnection(connectionString);

        await db.OpenAsync();

        await using var command = db.CreateCommand();

        command.CommandText = """
            SELECT
                r.Id,
                r.Code,
                r.Name,
                r.Unit
            FROM MCMaterialReceiptLinks rl
            INNER JOIN MCMaterialStockCardLinks sl
                ON sl.MaterialCatalogId = rl.MaterialCatalogId
            INNER JOIN RawMaterials r
                ON r.Id = sl.RawMaterialId
            INNER JOIN PurchaseGoodsReceiptLines l
                ON l.Id = rl.PurchaseGoodsReceiptLineId
            WHERE rl.PurchaseGoodsReceiptLineId = @lineId
              AND r.IsActive = 1
              AND UPPER(TRIM(r.Unit)) = UPPER(TRIM(l.Unit))
            """;

        command.Parameters.AddWithValue("@lineId", lineId);

        var results = new List<object>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            results.Add(new
            {
                id = reader.GetInt64(0),
                code = reader.IsDBNull(1) ? "" : reader.GetString(1),
                name = reader.IsDBNull(2) ? "" : reader.GetString(2),
                unit = reader.IsDBNull(3) ? "" : reader.GetString(3)
            });

            if (results.Count > 1)
                break;
        }

        if (results.Count != 1)
            return Ok(new
            {
                matched = false,
                reason = results.Count == 0
                    ? "Eslesen stok karti bulunamadi."
                    : "Birden fazla stok karti eslesiyor."
            });

        return Ok(new
        {
            matched = true,
            material = results[0]
        });
    }
}

public sealed class MCReceiptMatchingPage
{
    public List<MCReceiptMatchingLine> Lines { get; set; } = new();
    public List<MCReceiptMatchingMaterial> Materials { get; set; } = new();
}

public sealed class MCReceiptMatchingLine
{
    public long Id { get; set; }
    public long ReceiptId { get; set; }
    public string Description { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal Quantity { get; set; }
    public string ReceiptStatus { get; set; } = "";
    public bool PrecheckReady { get; set; }
    public string Reasons { get; set; } = "";
}

public sealed class MCReceiptMatchingMaterial
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
}
