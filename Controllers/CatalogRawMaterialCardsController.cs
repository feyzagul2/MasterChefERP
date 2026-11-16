// MC_CATALOG_RAW_CARD_CREATE_2026
using MasterChefTakip.Data;
using MasterChefTakip.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

[Authorize(Roles = AppRoles.Admin)]
public class CatalogRawMaterialCardsController : Controller
{
    private readonly ApplicationDbContext _db;

    public CatalogRawMaterialCardsController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Create(int catalogId)
    {
        await using var db = new SqliteConnection(
            _db.Database.GetConnectionString());

        await db.OpenAsync();

        await using var command = db.CreateCommand();

        command.CommandText = @"
            SELECT
                COALESCE(MaterialCode, ''),
                COALESCE(MaterialName, ''),
                COALESCE(MaterialType, '')
            FROM MCMaterialCatalog
            WHERE Id = @id";

        command.Parameters.AddWithValue("@id", catalogId);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return NotFound();

        var model = new CatalogRawCardInput
        {
            CatalogId = catalogId,
            Code = reader.GetString(0),
            Name = reader.GetString(1),
            MaterialType = reader.GetString(2)
        };

        if (!string.Equals(model.MaterialType.Trim(), "HM",
            StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(
                "Yalnizca HM turundeki malzemelerden " +
                "hammadde stok karti olusturulabilir.");
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CatalogRawCardInput input)
    {
        if (input.CatalogId <= 0)
            return BadRequest();

        if (input.Unit != "Kg" && input.Unit != "L")
        {
            ModelState.AddModelError(
                nameof(input.Unit),
                "Birim olarak Kg veya L secilmelidir.");

            return View(input);
        }

        await using var db = new SqliteConnection(
            _db.Database.GetConnectionString());

        await db.OpenAsync();

        await using var transaction = await db.BeginTransactionAsync();

        async Task<object?> Scalar(
            string sql,
            params (string Key, object Value)[] parameters)
        {
            await using var command = db.CreateCommand();

            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = sql;

            foreach (var item in parameters)
                command.Parameters.AddWithValue(item.Key, item.Value);

            return await command.ExecuteScalarAsync();
        }

        var type = Convert.ToString(await Scalar(@"
            SELECT MaterialType
            FROM MCMaterialCatalog
            WHERE Id = @id",
            ("@id", input.CatalogId)));

        if (!string.Equals(type?.Trim(), "HM",
            StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Bu kayit hammadde turunde degil.");
        }

        var code = Convert.ToString(await Scalar(@"
            SELECT MaterialCode
            FROM MCMaterialCatalog
            WHERE Id = @id",
            ("@id", input.CatalogId)))?.Trim() ?? "";

        var name = Convert.ToString(await Scalar(@"
            SELECT MaterialName
            FROM MCMaterialCatalog
            WHERE Id = @id",
            ("@id", input.CatalogId)))?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(code) ||
            string.IsNullOrWhiteSpace(name) ||
            code.Length > 50 ||
            name.Length > 200)
        {
            return BadRequest(
                "Malzeme kodu veya adi stok karti sinirlarina uymuyor.");
        }

        var mapped = Convert.ToInt64(await Scalar(@"
            SELECT COUNT(*)
            FROM MCMaterialStockCardLinks
            WHERE MaterialCatalogId = @id",
            ("@id", input.CatalogId)) ?? 0L);

        if (mapped > 0)
            return Conflict("Bu malzemenin stok karti zaten mevcut.");

        var duplicate = Convert.ToInt64(await Scalar(@"
            SELECT COUNT(*)
            FROM RawMaterials
            WHERE UPPER(TRIM(Code)) = UPPER(TRIM(@code))",
            ("@code", code)) ?? 0L);

        if (duplicate > 0)
        {
            return Conflict(
                "Bu kodla stok karti zaten var. " +
                "Mevcut karti eslestirme ekranindan secin.");
        }

        try
        {
            await using (var insert = db.CreateCommand())
            {
                insert.Transaction = (SqliteTransaction)transaction;

                insert.CommandText = @"
                    INSERT INTO RawMaterials
                    (
                        Code,
                        Name,
                        Unit,
                        MinimumStock,
                        Category,
                        Notes,
                        IsActive,
                        CreatedAtUtc
                    )
                    VALUES
                    (
                        @code,
                        @name,
                        @unit,
                        '0',
                        'HM',
                        '',
                        1,
                        @date
                    )";

                insert.Parameters.AddWithValue("@code", code);
                insert.Parameters.AddWithValue("@name", name);
                insert.Parameters.AddWithValue("@unit", input.Unit);
                insert.Parameters.AddWithValue(
                    "@date", DateTime.UtcNow.ToString("O"));

                await insert.ExecuteNonQueryAsync();
            }

            var rawId = Convert.ToInt64(await Scalar(
                "SELECT last_insert_rowid()") ?? 0L);

            if (rawId <= 0)
                throw new InvalidOperationException(
                    "Stok karti olusturulamadi.");

            await using (var link = db.CreateCommand())
            {
                link.Transaction = (SqliteTransaction)transaction;

                link.CommandText = @"
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
                        @raw,
                        @user,
                        @date
                    )";

                link.Parameters.AddWithValue(
                    "@catalog", input.CatalogId);

                link.Parameters.AddWithValue("@raw", rawId);

                link.Parameters.AddWithValue(
                    "@user", User.Identity?.Name ?? "");

                link.Parameters.AddWithValue(
                    "@date", DateTime.UtcNow.ToString("O"));

                await link.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch (SqliteException)
        {
            await transaction.RollbackAsync();

            return Conflict(
                "Kayit olusturulamadi. " +
                "Kod veya eslestirme baska bir islemde kullanilmis olabilir.");
        }

        TempData["MCStockMappingSuccess"] =
            "Gercek malzemeden hammadde stok karti olusturuldu.";

        return RedirectToAction(
            "Index",
            "MaterialStockMapping",
            new { q = code });
    }
}

public class CatalogRawCardInput
{
    public int CatalogId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string MaterialType { get; set; } = "";
    public string Unit { get; set; } = "";
}
