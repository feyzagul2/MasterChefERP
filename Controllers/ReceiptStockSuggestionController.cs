// MC_RECEIPT_STOCK_SUGGESTION_2026
using MasterChefTakip.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

[Authorize]
public class ReceiptStockSuggestionController : Controller
{
    private readonly ApplicationDbContext _db;

    public ReceiptStockSuggestionController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Line(int receiptLineId)
    {
        if (receiptLineId <= 0)
            return BadRequest();

        await using var connection = new SqliteConnection(
            _db.Database.GetConnectionString());

        await connection.OpenAsync();

        await using var command = connection.CreateCommand();

        command.CommandText = @"
            SELECT
                ml.MaterialCatalogId,
                COALESCE(c.MaterialCode, ''),
                COALESCE(c.MaterialName, ''),
                COALESCE(c.MaterialType, ''),
                sm.RawMaterialId,
                COALESCE(r.Code, ''),
                COALESCE(r.Name, ''),
                COALESCE(r.Unit, ''),
                COALESCE(l.Unit, ''),
                COALESCE(r.IsActive, 0)
            FROM PurchaseGoodsReceiptLines l
            LEFT JOIN MCMaterialReceiptLinks ml
                ON ml.PurchaseGoodsReceiptLineId = l.Id
            LEFT JOIN MCMaterialCatalog c
                ON c.Id = ml.MaterialCatalogId
            LEFT JOIN MCMaterialStockCardLinks sm
                ON sm.MaterialCatalogId = c.Id
            LEFT JOIN RawMaterials r
                ON r.Id = sm.RawMaterialId
            WHERE l.Id = @line";

        command.Parameters.AddWithValue("@line", receiptLineId);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return NotFound();

        if (reader.IsDBNull(0))
        {
            return Json(new
            {
                status = "unmapped",
                message = "Katalog eşleştirmesi yok. Mevcut aktarım yöntemi kullanılabilir."
            });
        }

        var code = reader.GetString(1);
        var name = reader.GetString(2);
        var type = reader.GetString(3);

        if (!string.Equals(type.Trim(), "HM",
            StringComparison.OrdinalIgnoreCase))
        {
            return Json(new
            {
                status = "blocked",
                message = code + " - " + name +
                    ": Bu kayıt hammadde türünde değil."
            });
        }

        if (reader.IsDBNull(4))
        {
            return Json(new
            {
                status = "blocked",
                message = code + " - " + name +
                    ": Stok kartı eşleştirilmemiş."
            });
        }

        var stockId = reader.GetInt32(4);
        var stockCode = reader.GetString(5);
        var stockName = reader.GetString(6);
        var stockUnit = reader.GetString(7);
        var receiptUnit = reader.GetString(8);
        var active = Convert.ToInt32(reader.GetValue(9)) == 1;

        if (!active)
        {
            return Json(new
            {
                status = "blocked",
                message = "Eşleşen stok kartı aktif değil."
            });
        }

        if (!string.Equals(
            stockUnit.Trim(),
            receiptUnit.Trim(),
            StringComparison.OrdinalIgnoreCase))
        {
            return Json(new
            {
                status = "blocked",
                message = "Birim uyuşmazlığı. Mal kabul: " +
                    receiptUnit + ", stok kartı: " + stockUnit
            });
        }

        return Json(new
        {
            status = "matched",
            stockId,
            message = code + " - " + name + " → " +
                stockCode + " - " + stockName
        });
    }
}
