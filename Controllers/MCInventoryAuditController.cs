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

// MC_INVENTORY_AUDIT_V1
// Salt okunur kontrol. Stok veya veritabani kaydi degistirmez.
[Authorize(Roles = AppRoles.Admin)]
[Route("MCInventoryAudit")]
public sealed class MCInventoryAuditController : Controller
{
    private readonly ApplicationDbContext _db;

    public MCInventoryAuditController(ApplicationDbContext db)
    {
        _db = db;
    }

    
    // MC_AUDIT_REPORT_V1
    [HttpGet("Report")]
    public async Task<IActionResult> Report()
    {
        var result = await Index();

        if (result is not ObjectResult response)
            return result;

        var json = System.Text.Json.JsonSerializer
            .SerializeToElement(response.Value);

        static string Read(
            System.Text.Json.JsonElement json,
            string key)
        {
            if (json.ValueKind ==
                    System.Text.Json.JsonValueKind.Object &&
                json.TryGetProperty(key, out var value))
            {
                return value.ToString();
            }

            return "";
        }

        var model = new MCInventoryAuditReportPage
        {
            Status = Read(json, "status"),
            ActiveCards = Read(json, "activeCards"),
            ActiveWarehouses = Read(json, "activeWarehouses"),
            TotalLinks = Read(json, "totalLinks"),
            InvalidLinks = Read(json, "invalidLinks"),
            UnassignedCards = Read(
                json, "cardsWithoutActiveWarehouse"),
            InvalidUnits = Read(json, "invalidUnits"),
            MissingTables = Read(json, "missingTables")
        };

        return View("Report", model);
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        await using var db = new SqliteConnection(
            _db.Database.GetDbConnection().ConnectionString);

        await db.OpenAsync();

        async Task<bool> Exists(string name)
        {
            await using var cmd = db.CreateCommand();

            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type='table' AND name=@name";

            cmd.Parameters.AddWithValue("@name", name);

            return Convert.ToInt64(
                await cmd.ExecuteScalarAsync() ?? 0L) == 1;
        }

        async Task<long> Count(string sql)
        {
            await using var cmd = db.CreateCommand();
            cmd.CommandText = sql;

            return Convert.ToInt64(
                await cmd.ExecuteScalarAsync() ?? 0L);
        }

        var tables = new[]
        {
            "MCInventoryCards",
            "MCInventoryWarehouseLinks",
            "RawMaterialWarehouses"
        };

        var missing = new List<string>();

        foreach (var table in tables)
        {
            if (!await Exists(table))
                missing.Add(table);
        }

        if (missing.Count > 0)
        {
            return Ok(new
            {
                status = "Tablolar hen?z haz?r de?il",
                missingTables = missing,
                stockUpdated = false
            });
        }

        var activeCards = await Count(@"
            SELECT COUNT(*)
            FROM MCInventoryCards
            WHERE IsActive=1");

        var activeWarehouses = await Count(@"
            SELECT COUNT(*)
            FROM RawMaterialWarehouses
            WHERE IsActive=1");

        var totalLinks = await Count(@"
            SELECT COUNT(*)
            FROM MCInventoryWarehouseLinks");

        var invalidLinks = await Count(@"
            SELECT COUNT(*)
            FROM MCInventoryWarehouseLinks l
            LEFT JOIN MCInventoryCards c
                ON c.Id=l.CardId
            LEFT JOIN RawMaterialWarehouses w
                ON w.Id=l.WarehouseId
            WHERE c.Id IS NULL
               OR w.Id IS NULL
               OR c.IsActive<>1
               OR w.IsActive<>1");

        var cardsWithoutActiveWarehouse = await Count(@"
            SELECT COUNT(*)
            FROM MCInventoryCards c
            WHERE c.IsActive=1
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM MCInventoryWarehouseLinks l
                  INNER JOIN RawMaterialWarehouses w
                      ON w.Id=l.WarehouseId
                  WHERE l.CardId=c.Id
                    AND w.IsActive=1
              )");

        var invalidUnits = await Count(@"
            SELECT COUNT(*)
            FROM MCInventoryCards
            WHERE IsActive=1
              AND UPPER(TRIM(COALESCE(Unit,''))) NOT IN
              (
                  'KG','KILOGRAM','K?LOGRAM',
                  'GR','G','GRAM','TON',
                  'L','LT','LITRE','L?TRE','LITER',
                  'ML','MILILITRE','M?L?L?TRE',
                  'ADET','METRE','M'
              )");

        return Ok(new
        {
            status = "Kontrol tamamland?",
            activeCards,
            activeWarehouses,
            totalLinks,
            invalidLinks,
            cardsWithoutActiveWarehouse,
            invalidUnits,
            readyForFurtherReview =
                invalidLinks == 0 && invalidUnits == 0,
            stockUpdated = false,
            receiptCreated = false
        });
    }
}

// MC_AUDIT_REPORT_MODEL_V1
public sealed class MCInventoryAuditReportPage
{
    public string Status { get; set; } = "";
    public string ActiveCards { get; set; } = "";
    public string ActiveWarehouses { get; set; } = "";
    public string TotalLinks { get; set; } = "";
    public string InvalidLinks { get; set; } = "";
    public string UnassignedCards { get; set; } = "";
    public string InvalidUnits { get; set; } = "";
    public string MissingTables { get; set; } = "";
}
