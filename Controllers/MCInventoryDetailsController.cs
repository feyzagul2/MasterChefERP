using System;
using System.Globalization;
using System.Threading.Tasks;
using MasterChefTakip.Data;
using MasterChefTakip.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

[Authorize(Roles = AppRoles.Admin)]
[Route("MCInventoryDetails")]
public class MCInventoryDetailsController : Controller
{
    private readonly ApplicationDbContext _db;

    public MCInventoryDetailsController(ApplicationDbContext db)
    {
        _db = db;
    }

    private SqliteConnection Open()
    {
        return new SqliteConnection(_db.Database.GetConnectionString());
    }

    private static async Task<bool> Exists(
        SqliteConnection db, string table)
    {
        await using var cmd = db.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(*) FROM sqlite_master " +
            "WHERE type='table' AND name=@name";
        cmd.Parameters.AddWithValue("@name", table);
        return Convert.ToInt64(
            await cmd.ExecuteScalarAsync() ?? 0L) > 0;
    }

    private static async Task<MCInventoryDetailsInput?> Load(
        SqliteConnection db, int catalogId)
    {
        await using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, COALESCE(MaterialCode,''),
                   COALESCE(MaterialName,''),
                   COALESCE(MaterialType,'')
            FROM MCMaterialCatalog
            WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", catalogId);

        MCInventoryDetailsInput model;

        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync())
                return null;

            model = new MCInventoryDetailsInput
            {
                CatalogId = reader.GetInt32(0),
                Code = reader.GetString(1),
                Name = reader.GetString(2),
                Type = reader.GetString(3)
            };
        }

        if (await Exists(db, "MCInventoryCards"))
        {
            await using var check = db.CreateCommand();
            check.CommandText =
                "SELECT COUNT(*) FROM MCInventoryCards " +
                "WHERE CatalogId=@id";
            check.Parameters.AddWithValue("@id", catalogId);

            model.HasCard = Convert.ToInt64(
                await check.ExecuteScalarAsync() ?? 0L) > 0;
        }

        if (!model.HasCard)
        {
            await using var check = db.CreateCommand();
            check.CommandText =
                "SELECT COUNT(*) FROM MCMaterialStockCardLinks " +
                "WHERE MaterialCatalogId=@id";
            check.Parameters.AddWithValue("@id", catalogId);

            model.HasCard = Convert.ToInt64(
                await check.ExecuteScalarAsync() ?? 0L) > 0;
        }

        if (!await Exists(db, "MCInventoryCardDetails"))
            return model;

        await using var detail = db.CreateCommand();
        detail.CommandText = @"
            SELECT Barcode, MinimumStock, CriticalStock,
                   Supplier, PurchasePrice, Currency,
                   VatRate, ShelfLocation, Notes
            FROM MCInventoryCardDetails
            WHERE CatalogId=@id";
        detail.Parameters.AddWithValue("@id", catalogId);

        await using var data = await detail.ExecuteReaderAsync();

        if (await data.ReadAsync())
        {
            model.Barcode = data.GetString(0);
            model.MinimumStock = data.GetString(1);
            model.CriticalStock = data.GetString(2);
            model.Supplier = data.GetString(3);
            model.PurchasePrice = data.GetString(4);
            model.Currency = data.GetString(5);
            model.VatRate = data.GetString(6);
            model.ShelfLocation = data.GetString(7);
            model.Notes = data.GetString(8);
        }

        return model;
    }

    [HttpGet("Edit")]
    public async Task<IActionResult> Edit(int catalogId)
    {
        if (catalogId <= 0)
            return BadRequest();

        await using var db = Open();
        await db.OpenAsync();

        var model = await Load(db, catalogId);
        if (model == null)
            return NotFound();

        return View(model);
    }

    private static bool Number(
        string? raw, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        var text = raw.Trim().Replace(',', '.');

        if (!decimal.TryParse(
                text, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var value))
            return false;

        if (value < 0 || value > 999999999999m)
            return false;

        normalized = value.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    [HttpPost("Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        MCInventoryDetailsInput input)
    {
        if (input.CatalogId <= 0)
            return BadRequest();

        await using var db = Open();
        await db.OpenAsync();

        var existing = await Load(db, input.CatalogId);

        if (existing == null)
            return NotFound();

        if (!existing.HasCard)
            return Conflict("?nce stok kart? olu?turulmal?d?r.");

        input.Code = existing.Code;
        input.Name = existing.Name;
        input.Type = existing.Type;
        input.HasCard = true;

        input.Barcode = (input.Barcode ?? "").Trim();
        input.Supplier = (input.Supplier ?? "").Trim();
        input.Currency = (input.Currency ?? "").Trim().ToUpperInvariant();
        input.ShelfLocation = (input.ShelfLocation ?? "").Trim();
        input.Notes = (input.Notes ?? "").Trim();

        if (input.Barcode.Length > 100 ||
            input.Supplier.Length > 250 ||
            input.Currency.Length > 10 ||
            input.ShelfLocation.Length > 150 ||
            input.Notes.Length > 2000)
        {
            input.Error = "Alan uzunlu?u s?n?r? a??ld?.";
            return View(input);
        }

        if (!Number(input.MinimumStock, out var minimum) ||
            !Number(input.CriticalStock, out var critical) ||
            !Number(input.PurchasePrice, out var price) ||
            !Number(input.VatRate, out var vat))
        {
            input.Error = "Say?sal alanlar? kontrol ediniz.";
            return View(input);
        }

        if (vat.Length > 0 &&
            decimal.Parse(vat, CultureInfo.InvariantCulture) > 100)
        {
            input.Error = "KDV oran? 0 ile 100 aras?nda olmal?d?r.";
            return View(input);
        }

        await using var tx = (SqliteTransaction)
            await db.BeginTransactionAsync();

        await using (var schema = db.CreateCommand())
        {
            schema.Transaction = tx;
            schema.CommandText = @"
                CREATE TABLE IF NOT EXISTS MCInventoryCardDetails
                (
                    CatalogId INTEGER NOT NULL PRIMARY KEY,
                    Barcode TEXT NOT NULL DEFAULT '',
                    MinimumStock TEXT NOT NULL DEFAULT '',
                    CriticalStock TEXT NOT NULL DEFAULT '',
                    Supplier TEXT NOT NULL DEFAULT '',
                    PurchasePrice TEXT NOT NULL DEFAULT '',
                    Currency TEXT NOT NULL DEFAULT '',
                    VatRate TEXT NOT NULL DEFAULT '',
                    ShelfLocation TEXT NOT NULL DEFAULT '',
                    Notes TEXT NOT NULL DEFAULT '',
                    UpdatedAtUtc TEXT NOT NULL,
                    FOREIGN KEY(CatalogId)
                        REFERENCES MCMaterialCatalog(Id)
                )";
            await schema.ExecuteNonQueryAsync();
        }

        await using (var save = db.CreateCommand())
        {
            save.Transaction = tx;
            save.CommandText = @"
                INSERT INTO MCInventoryCardDetails
                (CatalogId, Barcode, MinimumStock, CriticalStock,
                 Supplier, PurchasePrice, Currency, VatRate,
                 ShelfLocation, Notes, UpdatedAtUtc)
                VALUES
                (@id, @barcode, @min, @critical,
                 @supplier, @price, @currency, @vat,
                 @shelf, @notes, @date)
                ON CONFLICT(CatalogId) DO UPDATE SET
                    Barcode=excluded.Barcode,
                    MinimumStock=excluded.MinimumStock,
                    CriticalStock=excluded.CriticalStock,
                    Supplier=excluded.Supplier,
                    PurchasePrice=excluded.PurchasePrice,
                    Currency=excluded.Currency,
                    VatRate=excluded.VatRate,
                    ShelfLocation=excluded.ShelfLocation,
                    Notes=excluded.Notes,
                    UpdatedAtUtc=excluded.UpdatedAtUtc";

            save.Parameters.AddWithValue("@id", input.CatalogId);
            save.Parameters.AddWithValue("@barcode", input.Barcode);
            save.Parameters.AddWithValue("@min", minimum);
            save.Parameters.AddWithValue("@critical", critical);
            save.Parameters.AddWithValue("@supplier", input.Supplier);
            save.Parameters.AddWithValue("@price", price);
            save.Parameters.AddWithValue("@currency", input.Currency);
            save.Parameters.AddWithValue("@vat", vat);
            save.Parameters.AddWithValue("@shelf", input.ShelfLocation);
            save.Parameters.AddWithValue("@notes", input.Notes);
            save.Parameters.AddWithValue(
                "@date", DateTime.UtcNow.ToString("O"));

            await save.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        TempData["MCDetailSaved"] = "Bilgiler kaydedildi.";

        return RedirectToAction(
            nameof(Edit), new { catalogId = input.CatalogId });
    }
}

public class MCInventoryDetailsInput
{
    public int CatalogId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool HasCard { get; set; }
    public string Barcode { get; set; } = "";
    public string MinimumStock { get; set; } = "";
    public string CriticalStock { get; set; } = "";
    public string Supplier { get; set; } = "";
    public string PurchasePrice { get; set; } = "";
    public string Currency { get; set; } = "";
    public string VatRate { get; set; } = "";
    public string ShelfLocation { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Error { get; set; } = "";
}
