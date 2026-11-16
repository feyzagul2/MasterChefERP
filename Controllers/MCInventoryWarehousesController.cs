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

// MC_WAREHOUSE_LINK_V1
[Authorize(Roles = AppRoles.Admin)]
[Route("MCInventoryWarehouses")]
public class MCInventoryWarehousesController : Controller
{
    private readonly ApplicationDbContext _db;

    public MCInventoryWarehousesController(ApplicationDbContext db)
    {
        _db = db;
    }

    private SqliteConnection Open()
    {
        return new SqliteConnection(
            _db.Database.GetConnectionString());
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var model = new MCWarehouseLinkPage();

        await using var db = Open();
        await db.OpenAsync();

        if (!await TableExists(db, "MCInventoryCards"))
        {
            model.Message =
                "Hen?z genel malzeme stok kart? tablosu olu?turulmam??.";
            return View(model);
        }

        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT Id, MaterialCode, MaterialName, Unit
                FROM MCInventoryCards
                WHERE IsActive = 1
                ORDER BY MaterialCode
                LIMIT 3000";

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                model.Cards.Add(new MCWarehouseCardRow
                {
                    Id = reader.GetInt64(0),
                    Code = reader.GetString(1),
                    Name = reader.GetString(2),
                    Unit = reader.GetString(3)
                });
            }
        }

        if (!await TableExists(db, "RawMaterialWarehouses"))
        {
            model.Message = "Depo tablosu bulunamad?.";
            return View(model);
        }

        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT Id, Name
                FROM RawMaterialWarehouses
                ORDER BY Name";

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                model.Warehouses.Add(new MCWarehouseOption
                {
                    Id = reader.GetInt64(0),
                    Name = reader.GetString(1)
                });
            }
        }

        if (await TableExists(db, "MCInventoryWarehouseLinks"))
        {
            await using var cmd = db.CreateCommand();

            cmd.CommandText = @"
                SELECT CardId, WarehouseId
                FROM MCInventoryWarehouseLinks
                ORDER BY CardId, WarehouseId";

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                model.Links.Add(new MCWarehouseLinkRow
                {
                    CardId = reader.GetInt64(0),
                    WarehouseId = reader.GetInt64(1)
                });
            }
        }

        return View(model);
    }

    [HttpPost("Assign")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(
        long cardId,
        long warehouseId,
        bool confirmed)
    {
        if (cardId <= 0 || warehouseId <= 0 || !confirmed)
            return BadRequest("Kart ve depo se?imini do?rulay?n.");

        await using var db = Open();
        await db.OpenAsync();

        await using var tx = (SqliteTransaction)
            await db.BeginTransactionAsync();

        try
        {
            async Task<long> Count(string sql, long id)
            {
                await using var cmd = db.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@id", id);

                return Convert.ToInt64(
                    await cmd.ExecuteScalarAsync() ?? 0L);
            }

            var cardCount = await Count(
                "SELECT COUNT(*) FROM MCInventoryCards " +
                "WHERE Id=@id AND IsActive=1",
                cardId);

            // MC_ACTIVE_WAREHOUSE_GUARD_V2
            var warehouseCount = await Count(
                "SELECT COUNT(*) FROM RawMaterialWarehouses " +
                "WHERE Id=@id AND IsActive=1",
                warehouseId);

            if (cardCount != 1 || warehouseCount != 1)
                return Conflict("Kart veya depo ge?erli de?il.");

            await using (var schema = db.CreateCommand())
            {
                schema.Transaction = tx;
                schema.CommandText = @"
                    CREATE TABLE IF NOT EXISTS
                    MCInventoryWarehouseLinks
                    (
                        CardId INTEGER NOT NULL,
                        WarehouseId INTEGER NOT NULL,
                        CreatedAtUtc TEXT NOT NULL,
                        PRIMARY KEY(CardId, WarehouseId),
                        FOREIGN KEY(CardId)
                            REFERENCES MCInventoryCards(Id),
                        FOREIGN KEY(WarehouseId)
                            REFERENCES RawMaterialWarehouses(Id)
                    )";

                await schema.ExecuteNonQueryAsync();
            }

            await using (var insert = db.CreateCommand())
            {
                insert.Transaction = tx;

                insert.CommandText = @"
                    INSERT OR IGNORE INTO
                    MCInventoryWarehouseLinks
                    (CardId, WarehouseId, CreatedAtUtc)
                    VALUES (@card, @warehouse, @date)";

                insert.Parameters.AddWithValue("@card", cardId);
                insert.Parameters.AddWithValue("@warehouse", warehouseId);
                insert.Parameters.AddWithValue(
                    "@date", DateTime.UtcNow.ToString("O"));

                await insert.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();

            TempData["WarehouseLinkMessage"] =
                "Depo ba?lant?s? kaydedildi. Stok miktar? de?i?medi.";

            return RedirectToAction(nameof(Index));
        }
        catch (SqliteException)
        {
            await tx.RollbackAsync();
            return Conflict("Depo ba?lant?s? kaydedilemedi.");
        }
    }

    private static async Task<bool> TableExists(
        SqliteConnection db,
        string table)
    {
        await using var cmd = db.CreateCommand();

        cmd.CommandText = @"
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type='table' AND name=@name";

        cmd.Parameters.AddWithValue("@name", table);

        return Convert.ToInt64(
            await cmd.ExecuteScalarAsync() ?? 0L) > 0;
    }
}

public class MCWarehouseLinkPage
{
    public string Message { get; set; } = "";
    public List<MCWarehouseCardRow> Cards { get; set; } = new();
    public List<MCWarehouseOption> Warehouses { get; set; } = new();
    public List<MCWarehouseLinkRow> Links { get; set; } = new();
}

public class MCWarehouseCardRow
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
}

public class MCWarehouseOption
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
}

public class MCWarehouseLinkRow
{
    public long CardId { get; set; }
    public long WarehouseId { get; set; }
}
