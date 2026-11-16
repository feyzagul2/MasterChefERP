using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MasterChefTakip.Data;
using MasterChefTakip.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

// MC_RECEIPT_PRECHECK_V1
// Salt okunur mal kabul uygunluk kontrolu.
// Stok hareketi veya mal kabul kaydi olusturmaz.
[Authorize(Roles = AppRoles.Admin)]
[Route("MCInventoryReceiptCheck")]
public sealed class MCInventoryReceiptCheckController : Controller
{
    private readonly ApplicationDbContext _db;

    public MCInventoryReceiptCheckController(ApplicationDbContext db)
    {
        _db = db;
    }

    
    // MC_RECEIPT_REVIEW_V1
    [HttpGet("Review")]
    public async Task<IActionResult> Review(
        long cardId,
        long warehouseId,
        string? unit,
        decimal? quantity = null)
    {
        var result = await Index(
            cardId, warehouseId, unit, quantity);

        if (result is not ObjectResult response)
            return result;

        var json = System.Text.Json.JsonSerializer
            .SerializeToElement(response.Value);

        static string Value(
            System.Text.Json.JsonElement json,
            string key)
        {
            if (json.ValueKind == System.Text.Json.JsonValueKind.Object &&
                json.TryGetProperty(key, out var property))
            {
                return property.ToString();
            }

            return "";
        }

        var model = new MCReceiptReviewPage
        {
            CardId = cardId,
            WarehouseId = warehouseId,
            Unit = unit ?? "",
            Quantity = quantity,
            Eligible =
                response.StatusCode == 200 &&
                string.Equals(
                    Value(json, "eligible"),
                    "true",
                    StringComparison.OrdinalIgnoreCase),
            Message = Value(json, "message")
        };

        Response.StatusCode = response.StatusCode ?? 200;

        return View("Review", model);
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        long cardId,
        long warehouseId,
        string? unit, decimal? quantity = null)
    {
        
        // MC_QUANTITY_CHECK_FIXED_V2
        // Eksik, sifir, negatif ve hassasiyeti fazla miktarlar reddedilir.
        if (!quantity.HasValue ||
            quantity.Value <= 0 ||
            decimal.Round(quantity.Value, 4) != quantity.Value)
        {
            return BadRequest(new
            {
                eligible = false,
                message = "Gecerli bir miktar giriniz. " +
                          "Miktar sifirdan buyuk ve en fazla " +
                          "4 ondalik basamakli olmalidir.",
                stockUpdated = false,
                receiptCreated = false
            });
        }

        if (cardId <= 0 || warehouseId <= 0)
        {
            return BadRequest(new
            {
                eligible = false,
                message = "Gecerli stok karti ve depo secilmelidir."
            });
        }

        await using var db = new SqliteConnection(
            _db.Database.GetDbConnection().ConnectionString);

        await db.OpenAsync();

        async Task<bool> TableExists(string name)
        {
            await using var cmd = db.CreateCommand();
            cmd.CommandText =
                "SELECT COUNT(*) FROM sqlite_master " +
                "WHERE type='table' AND name=@name";
            cmd.Parameters.AddWithValue("@name", name);

            return Convert.ToInt64(
                await cmd.ExecuteScalarAsync() ?? 0L) == 1;
        }

        async Task<bool> ColumnExists(string table, string column)
        {
            await using var cmd = db.CreateCommand();

            // Tablo adlari yalnizca sabit, kod icinde tanimli.
            cmd.CommandText = table switch
            {
                "MCInventoryCards" =>
                    "PRAGMA table_info(MCInventoryCards)",
                "RawMaterialWarehouses" =>
                    "PRAGMA table_info(RawMaterialWarehouses)",
                _ => "SELECT NULL WHERE 1=0"
            };

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                if (string.Equals(
                    reader.GetString(1),
                    column,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        var requiredTables = new[]
        {
            "MCInventoryCards",
            "RawMaterialWarehouses",
            "MCInventoryWarehouseLinks"
        };

        foreach (var table in requiredTables)
        {
            if (!await TableExists(table))
            {
                return Conflict(new
                {
                    eligible = false,
                    message = "Gerekli tablo henuz hazir degil.",
                    missingTable = table
                });
            }
        }

        if (!await ColumnExists("MCInventoryCards", "Unit"))
        {
            return Conflict(new
            {
                eligible = false,
                message = "Stok kartinin olcu birimi alani bulunamadi."
            });
        }

        string? cardUnit = null;

        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT Unit
                FROM MCInventoryCards
                WHERE Id=@card AND IsActive=1";

            cmd.Parameters.AddWithValue("@card", cardId);

            var value = await cmd.ExecuteScalarAsync();

            if (value == null || value == DBNull.Value)
            {
                return Conflict(new
                {
                    eligible = false,
                    message = "Aktif stok karti bulunamadi."
                });
            }

            cardUnit = Convert.ToString(value);
        }

        var hasActiveColumn = await ColumnExists(
            "RawMaterialWarehouses", "IsActive");

        if (!hasActiveColumn)
        {
            return Conflict(new
            {
                eligible = false,
                message = "Depo aktiflik bilgisi dogrulanamadi."
            });
        }

        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM RawMaterialWarehouses
                WHERE Id=@warehouse AND IsActive=1";

            cmd.Parameters.AddWithValue("@warehouse", warehouseId);

            var count = Convert.ToInt64(
                await cmd.ExecuteScalarAsync() ?? 0L);

            if (count != 1)
            {
                return Conflict(new
                {
                    eligible = false,
                    message = "Aktif depo bulunamadi."
                });
            }
        }

        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM MCInventoryWarehouseLinks
                WHERE CardId=@card AND WarehouseId=@warehouse";

            cmd.Parameters.AddWithValue("@card", cardId);
            cmd.Parameters.AddWithValue("@warehouse", warehouseId);

            var count = Convert.ToInt64(
                await cmd.ExecuteScalarAsync() ?? 0L);

            if (count != 1)
            {
                return Conflict(new
                {
                    eligible = false,
                    message = "Stok karti bu depoya eslestirilmemis."
                });
            }
        }

        
        // MC_RECEIPT_UNIT_SYNC_V1
        static string Normalize(string? text)
        {
            var value = (text ?? "").Trim().ToUpperInvariant();

            return value switch
            {
                "KG" or "KILOGRAM" or "K?LOGRAM" => "KG",
                "GR" or "G" or "GRAM" => "GR",
                "TON" => "TON",

                "L" or "LT" or "LITRE" or "L?TRE"
                    or "LITER" => "L",

                "ML" or "MILILITRE" or "M?L?L?TRE"
                    or "MILLILITER" => "ML",

                "ADET" => "ADET",
                "METRE" or "M" => "METRE",

                _ => ""
            };
        }

        var requestedUnit = Normalize(unit);
        var storedUnit = Normalize(cardUnit);

        if (requestedUnit.Length == 0 ||
            storedUnit.Length == 0 ||
            requestedUnit != storedUnit)
        {
            return Conflict(new
            {
                eligible = false,
                message = "Olcu birimleri uyusmuyor.",
                cardUnit,
                requestedUnit = unit,
                note = "Bu asamada otomatik birim donusumu yapilmiyor."
            });
        }

        return Ok(new
        {
            eligible = true,
            cardId,
            warehouseId,
            unit = storedUnit,
            message = "Kart, depo ve olcu birimi kontrolu basarili.",
            stockUpdated = false,
            receiptCreated = false
        });
    }
}

// MC_RECEIPT_REVIEW_MODEL_V1

// MC_RECEIPT_CHECKLIST_FIXED_V2
public static class MCReceiptChecklist
{
    public static string QuantityStatus(decimal? value)
    {
        if (!value.HasValue)
            return "Miktar girilmedi";

        if (value.Value <= 0)
            return "Miktar s?f?rdan b?y?k olmal?";

        if (decimal.Round(value.Value, 4) != value.Value)
            return "En fazla 4 ondal?k basamak kullan?labilir";

        return "Miktar bi?imi uygun";
    }

    public static string UnitStatus(string? unit)
    {
        var value = (unit ?? "").Trim().ToUpperInvariant();

        return value switch
        {
            "KG" or "KILOGRAM" or "K?LOGRAM"
                or "GR" or "G" or "GRAM" or "TON"
                or "L" or "LT" or "LITRE" or "L?TRE"
                or "LITER" or "ML" or "MILILITRE"
                or "M?L?L?TRE" or "MILLILITER"
                or "ADET" or "METRE" or "M"
                => "Tan?nan ?l?? birimi",

            _ => "?l?? birimi kontrol edilmeli"
        };
    }
}

public sealed class MCReceiptReviewPage
{
    public long CardId { get; set; }
    public long WarehouseId { get; set; }
    public string Unit { get; set; } = "";
    public decimal? Quantity { get; set; }
    public bool Eligible { get; set; }
    public string Message { get; set; } = "";
}
