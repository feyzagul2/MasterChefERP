// MC_RAW_WAREHOUSE_MANAGEMENT_2026
using MasterChefTakip.Data;
using MasterChefTakip.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

[Authorize(Roles = AppRoles.Admin)]
public class MCRawWarehousesController : Controller
{
    private readonly ApplicationDbContext _db;

    public MCRawWarehousesController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var model = new MCRawWarehousePage();

        await using var db = new SqliteConnection(
            _db.Database.GetConnectionString());

        await db.OpenAsync();

        await using var command = db.CreateCommand();

        command.CommandText = @"
            SELECT Id,
                   COALESCE(Code, ''),
                   COALESCE(Name, ''),
                   COALESCE(Location, ''),
                   IsActive
            FROM RawMaterialWarehouses
            ORDER BY Name, Id";

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            model.Warehouses.Add(new MCRawWarehouseRow
            {
                Id = reader.GetInt32(0),
                Code = reader.GetString(1),
                Name = reader.GetString(2),
                Location = reader.GetString(3),
                IsActive = Convert.ToInt32(reader.GetValue(4)) == 1
            });
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        string code,
        string name,
        string? location)
    {
        code = (code ?? "").Trim();
        name = (name ?? "").Trim();
        location = (location ?? "").Trim();

        if (code.Length == 0 || code.Length > 50 ||
            name.Length == 0 || name.Length > 200 ||
            location.Length > 250)
        {
            TempData["MCWarehouseError"] =
                "Depo kodu veya adi gecersiz.";

            return RedirectToAction(nameof(Index));
        }

        await using var db = new SqliteConnection(
            _db.Database.GetConnectionString());

        await db.OpenAsync();

        await using var transaction = await db.BeginTransactionAsync();

        await using var check = db.CreateCommand();
        check.Transaction = (SqliteTransaction)transaction;

        check.CommandText = @"
            SELECT COUNT(*)
            FROM RawMaterialWarehouses
            WHERE UPPER(TRIM(Code)) = UPPER(TRIM(@code))";

        check.Parameters.AddWithValue("@code", code);

        var existing = Convert.ToInt64(
            await check.ExecuteScalarAsync());

        if (existing > 0)
        {
            TempData["MCWarehouseError"] =
                "Bu depo kodu zaten kullaniliyor.";

            return RedirectToAction(nameof(Index));
        }

        await using var insert = db.CreateCommand();
        insert.Transaction = (SqliteTransaction)transaction;

        insert.CommandText = @"
            INSERT INTO RawMaterialWarehouses
            (
                Code,
                Name,
                Location,
                IsActive,
                CreatedAtUtc
            )
            VALUES
            (
                @code,
                @name,
                @location,
                1,
                @created
            )";

        insert.Parameters.AddWithValue("@code", code);
        insert.Parameters.AddWithValue("@name", name);
        insert.Parameters.AddWithValue("@location", location);
        insert.Parameters.AddWithValue(
            "@created", DateTime.UtcNow.ToString("O"));

        try
        {
            await insert.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }
        catch (SqliteException)
        {
            await transaction.RollbackAsync();

            TempData["MCWarehouseError"] =
                "Depo kaydedilemedi. Bilgileri kontrol edin.";

            return RedirectToAction(nameof(Index));
        }

        TempData["MCWarehouseSuccess"] =
            "Hammadde deposu basariyla olusturuldu.";

        return RedirectToAction(nameof(Index));
    }
}

public class MCRawWarehousePage
{
    public List<MCRawWarehouseRow> Warehouses { get; set; } = new();
}

public class MCRawWarehouseRow
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Location { get; set; } = "";
    public bool IsActive { get; set; }
}
