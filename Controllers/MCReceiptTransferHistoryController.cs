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

// MC_RECEIPT_HISTORY_FIXED_V2
[Authorize(Roles = AppRoles.Admin)]
[Route("MCReceiptTransferHistory")]
public sealed class MCReceiptTransferHistoryController : Controller
{
    private readonly IConfiguration _configuration;

    public MCReceiptTransferHistoryController(
        IConfiguration configuration)
    {
        _configuration = configuration;
    }


    // MC_TRANSFER_INTEGRITY_V1
    [HttpGet("Integrity")]
    public async Task<IActionResult> Integrity()
    {
        var connectionString =
            _configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
            return StatusCode(500, "Veritaban? ba?lant?s? bulunamad?.");

        var model = new MCTransferIntegrityPage();

        await using var db = new SqliteConnection(connectionString);
        await db.OpenAsync();

        await using var cmd = db.CreateCommand();

        // Yaln?zca okuma: aktar?m ve stok hareketi kar??la?t?r?l?r.
        cmd.CommandText = @"
            SELECT
                t.Id,
                t.RawMaterialStockMovementId,
                t.RawMaterialId,
                t.WarehouseId,
                t.Quantity,
                COALESCE(t.Unit, ''),
                m.Id,
                m.RawMaterialId,
                m.WarehouseId,
                m.Quantity,
                COALESCE(m.Unit, '')
            FROM RawMaterialReceiptTransfers t
            LEFT JOIN RawMaterialStockMovements m
                ON m.Id = t.RawMaterialStockMovementId
            ORDER BY t.Id DESC";

        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            model.TotalTransfers++;

            var transferId = reader.GetInt64(0);
            var movementId = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);

            string? problem = null;

            if (reader.IsDBNull(6))
            {
                problem = "?li?kili stok hareketi bulunamad?.";
                model.MissingMovements++;
            }
            else
            {
                var transferMaterial = reader.GetInt64(2);
                var transferWarehouse = reader.GetInt64(3);
                var transferQuantity = Convert.ToDecimal(
                    reader.GetValue(4),
                    System.Globalization.CultureInfo.InvariantCulture);

                var transferUnit = reader.GetString(5).Trim();

                var movementMaterial = reader.GetInt64(7);
                var movementWarehouse = reader.GetInt64(8);
                var movementQuantity = Convert.ToDecimal(
                    reader.GetValue(9),
                    System.Globalization.CultureInfo.InvariantCulture);

                var movementUnit = reader.GetString(10).Trim();

                var issues = new List<string>();

                if (transferMaterial != movementMaterial)
                    issues.Add("Hammadde farkl?");

                if (transferWarehouse != movementWarehouse)
                    issues.Add("Depo farkl?");

                if (transferQuantity != movementQuantity)
                    issues.Add("Miktar farkl?");

                if (!string.Equals(
                    transferUnit,
                    movementUnit,
                    StringComparison.OrdinalIgnoreCase))
                    issues.Add("?l?? birimi farkl?");

                if (issues.Count > 0)
                {
                    problem = string.Join(", ", issues);
                    model.MismatchedMovements++;
                }
            }

            if (problem == null)
            {
                model.ConsistentTransfers++;
            }
            else if (model.Problems.Count < 500)
            {
                model.Problems.Add(new MCTransferIntegrityRow
                {
                    TransferId = transferId,
                    MovementId = movementId,
                    Problem = problem
                });
            }
        }

        return View(model);
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var connectionString =
            _configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
            return StatusCode(500, "Veritaban? ba?lant?s? bulunamad?.");

        var model = new MCReceiptHistoryPage();

        await using var db = new SqliteConnection(connectionString);
        await db.OpenAsync();

        await using var cmd = db.CreateCommand();

        cmd.CommandText = @"
            SELECT
                t.Id,
                t.PurchaseGoodsReceiptLineId,
                t.RawMaterialId,
                COALESCE(m.Name, ''),
                t.WarehouseId,
                COALESCE(w.Name, ''),
                t.Quantity,
                COALESCE(t.Unit, ''),
                COALESCE(t.CreatedAtUtc, ''),
                t.RawMaterialStockMovementId
            FROM RawMaterialReceiptTransfers t
            LEFT JOIN RawMaterials m
                ON m.Id = t.RawMaterialId
            LEFT JOIN RawMaterialWarehouses w
                ON w.Id = t.WarehouseId
            ORDER BY t.Id DESC
            LIMIT 500";

        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            model.Rows.Add(new MCReceiptHistoryRow
            {
                Id = Convert.ToInt64(reader.GetValue(0)),
                ReceiptLineId = Convert.ToInt64(reader.GetValue(1)),
                MaterialId = Convert.ToInt64(reader.GetValue(2)),
                MaterialName = reader.GetString(3),
                WarehouseId = Convert.ToInt64(reader.GetValue(4)),
                WarehouseName = reader.GetString(5),
                Quantity = Convert.ToDecimal(
                    reader.GetValue(6),
                    CultureInfo.InvariantCulture),
                Unit = reader.GetString(7),
                CreatedAt = reader.GetString(8),
                StockMovementId = Convert.ToInt64(reader.GetValue(9))
            });
        }

        return View(model);
    }
}

public sealed class MCReceiptHistoryPage
{
    public List<MCReceiptHistoryRow> Rows { get; set; } = new();
}

public sealed class MCReceiptHistoryRow
{
    public long Id { get; set; }
    public long ReceiptLineId { get; set; }
    public long MaterialId { get; set; }
    public string MaterialName { get; set; } = "";
    public long WarehouseId { get; set; }
    public string WarehouseName { get; set; } = "";
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public long StockMovementId { get; set; }
}

// MC_TRANSFER_INTEGRITY_MODEL_V1
public sealed class MCTransferIntegrityPage
{
    public long TotalTransfers { get; set; }
    public long ConsistentTransfers { get; set; }
    public long MissingMovements { get; set; }
    public long MismatchedMovements { get; set; }

    public List<MCTransferIntegrityRow> Problems { get; set; } = new();
}

public sealed class MCTransferIntegrityRow
{
    public long TransferId { get; set; }
    public long MovementId { get; set; }
    public string Problem { get; set; } = "";
}
