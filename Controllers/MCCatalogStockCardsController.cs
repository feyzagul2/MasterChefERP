using System;
using MasterChefTakip.Data;
using MasterChefTakip.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

// MC_SAFE_STOCK_CARD_COMPAT_V2
// Gercek stok kaydi bu controller tarafindan olusturulmaz.

[Authorize(Roles = AppRoles.Admin)]
[Route("MCCatalogStockCards")]
public class MCCatalogStockCardsController : Controller
{
    private readonly ApplicationDbContext _db;

    public MCCatalogStockCardsController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? code)
    {
        code = (code ?? "").Trim();

        if (string.IsNullOrWhiteSpace(code))
        {
            return RedirectToAction(
                "Index",
                "MaterialCatalog");
        }

        if (code.Length > 100)
            return BadRequest("Malzeme kodu ge?ersiz.");

        await using var connection = new SqliteConnection(
            _db.Database.GetConnectionString());

        await connection.OpenAsync();

        await using var command = connection.CreateCommand();

        command.CommandText = @"
            SELECT Id
            FROM MCMaterialCatalog
            WHERE UPPER(TRIM(MaterialCode)) =
                  UPPER(TRIM(@code))
              AND UPPER(TRIM(MaterialType)) = 'HM'
            LIMIT 2";

        command.Parameters.AddWithValue("@code", code);

        await using var reader =
            await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return NotFound("Hammadde katalog kayd? bulunamad?.");

        var catalogId = reader.GetInt32(0);

        if (await reader.ReadAsync())
            return Conflict("Malzeme kodu birden fazla kay?tla e?le?iyor.");

        return RedirectToAction(
            "Create",
            "CatalogRawMaterialCards",
            new { catalogId });
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public IActionResult Create()
    {
        return BadRequest(
            "Eski stok kart? olu?turma i?lemi kapat?ld?. " +
            "Malzeme Katalo?u ?zerinden i?lem yap?n?z.");
    }
}


// Razor gorunumuyle geriye donuk uyumluluk.
public class MCCatalogStockCardPage
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Error { get; set; } = "";
    public bool Found { get; set; }
}
