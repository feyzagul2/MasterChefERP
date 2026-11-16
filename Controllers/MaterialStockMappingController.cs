// MC_MATERIAL_STOCK_MAPPING_2026
using MasterChefTakip.Data;
using MasterChefTakip.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

[Authorize(Roles = AppRoles.Admin)]
public class MaterialStockMappingController : Controller
{
    private readonly ApplicationDbContext _db;

    public MaterialStockMappingController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? q, int page = 1)
    {
        q = (q ?? "").Trim();
        if (q.Length > 100) q = q[..100];

        page = Math.Clamp(page, 1, 100000);

        var model = new MaterialStockMappingPage
        {
            Search = q,
            Page = page
        };

        await using var db = new SqliteConnection(
            _db.Database.GetConnectionString());

        await db.OpenAsync();

        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM RawMaterials";

            model.StockCardCount = Convert.ToInt32(
                await cmd.ExecuteScalarAsync());
        }

        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM MCMaterialCatalog
                WHERE UPPER(TRIM(MaterialType)) = 'HM'";

            model.RawCatalogCount = Convert.ToInt32(
                await cmd.ExecuteScalarAsync());
        }

        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM MCMaterialStockCardLinks";

            model.MatchedCount = Convert.ToInt32(
                await cmd.ExecuteScalarAsync());
        }

        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT Id, Code, Name, Unit
                FROM RawMaterials
                ORDER BY Name
                LIMIT 300";

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                model.StockCards.Add(new MaterialStockCardOption
                {
                    Id = reader.GetInt32(0),
                    Code = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Name = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Unit = reader.IsDBNull(3) ? "" : reader.GetString(3)
                });
            }
        }

        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT
                    c.Id,
                    COALESCE(c.MaterialCode, ''),
                    COALESCE(c.MaterialName, ''),
                    COALESCE(r.Code, ''),
                    COALESCE(r.Name, ''),
                    COALESCE(r.Unit, ''),
                    l.RawMaterialId
                FROM MCMaterialCatalog c
                LEFT JOIN MCMaterialStockCardLinks l
                    ON l.MaterialCatalogId = c.Id
                LEFT JOIN RawMaterials r
                    ON r.Id = l.RawMaterialId
                WHERE UPPER(TRIM(c.MaterialType)) = 'HM'
                  AND (
                    @q = ''
                    OR c.MaterialCode LIKE @search
                    OR c.MaterialName LIKE @search
                  )
                ORDER BY c.MaterialCode, c.Id
                LIMIT 50 OFFSET @offset";

            cmd.Parameters.AddWithValue("@q", q);
            cmd.Parameters.AddWithValue("@search", "%" + q + "%");
            cmd.Parameters.AddWithValue("@offset", (page - 1) * 50);

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                model.Materials.Add(new MaterialStockMappingRow
                {
                    CatalogId = reader.GetInt32(0),
                    Code = reader.GetString(1),
                    Name = reader.GetString(2),
                    StockCode = reader.GetString(3),
                    StockName = reader.GetString(4),
                    StockUnit = reader.GetString(5),
                    IsMatched = !reader.IsDBNull(6)
                });
            }
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(
        int catalogId,
        int rawMaterialId)
    {
        if (catalogId <= 0 || rawMaterialId <= 0)
            return BadRequest("Gecersiz eslestirme.");

        await using var db = new SqliteConnection(
            _db.Database.GetConnectionString());

        await db.OpenAsync();

        await using var transaction = await db.BeginTransactionAsync();

        async Task<long> Count(string sql, int id)
        {
            await using var cmd = db.CreateCommand();

            cmd.Transaction = (SqliteTransaction)transaction;
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@id", id);

            return Convert.ToInt64(
                await cmd.ExecuteScalarAsync());
        }

        var catalogExists = await Count(@"
            SELECT COUNT(*)
            FROM MCMaterialCatalog
            WHERE Id = @id
              AND UPPER(TRIM(MaterialType)) = 'HM'",
            catalogId);

        if (catalogExists != 1)
            return BadRequest("Hammadde katalog kaydi bulunamadi.");

        var stockExists = await Count(@"
            SELECT COUNT(*)
            FROM RawMaterials
            WHERE Id = @id",
            rawMaterialId);

        if (stockExists != 1)
            return BadRequest("Stok karti bulunamadi.");

        var catalogUsed = await Count(@"
            SELECT COUNT(*)
            FROM MCMaterialStockCardLinks
            WHERE MaterialCatalogId = @id",
            catalogId);

        if (catalogUsed != 0)
            return Conflict("Bu malzeme zaten eslestirilmis.");

        var stockUsed = await Count(@"
            SELECT COUNT(*)
            FROM MCMaterialStockCardLinks
            WHERE RawMaterialId = @id",
            rawMaterialId);

        if (stockUsed != 0)
            return Conflict(
                "Bu stok karti baska bir malzemeye bagli.");

        await using var insert = db.CreateCommand();

        insert.Transaction = (SqliteTransaction)transaction;

        insert.CommandText = @"
            INSERT INTO MCMaterialStockCardLinks
            (
                MaterialCatalogId,
                RawMaterialId,
                MatchedBy,
                MatchedAtUtc
            )
            VALUES
            (
                @catalog,
                @stock,
                @user,
                @time
            )";

        insert.Parameters.AddWithValue("@catalog", catalogId);
        insert.Parameters.AddWithValue("@stock", rawMaterialId);
        insert.Parameters.AddWithValue(
            "@user", User.Identity?.Name ?? "");
        insert.Parameters.AddWithValue(
            "@time", DateTime.UtcNow.ToString("O"));

        try
        {
            await insert.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }
        catch (SqliteException)
        {
            await transaction.RollbackAsync();

            return Conflict(
                "Eslestirme yapilamadi. Kayitlar degismis olabilir.");
        }

        TempData["MCStockMappingSuccess"] =
            "Malzeme stok kartiyla eslestirildi.";

        return RedirectToAction(nameof(Index));
    }
}

public class MaterialStockMappingPage
{
    public string Search { get; set; } = "";
    public int Page { get; set; }
    public int RawCatalogCount { get; set; }
    public int StockCardCount { get; set; }
    public int MatchedCount { get; set; }

    public List<MaterialStockMappingRow> Materials { get; set; } = new();
    public List<MaterialStockCardOption> StockCards { get; set; } = new();
}

public class MaterialStockMappingRow
{
    public int CatalogId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string StockCode { get; set; } = "";
    public string StockName { get; set; } = "";
    public string StockUnit { get; set; } = "";
    public bool IsMatched { get; set; }
}

public class MaterialStockCardOption
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
}
