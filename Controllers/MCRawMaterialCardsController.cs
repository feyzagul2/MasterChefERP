using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Claims;
using System.Threading.Tasks;
using MasterChefTakip.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace MasterChefTakip.Controllers;

// MC_RAW_MATERIAL_CARD_FIXED_V2
[Authorize(Roles = AppRoles.Admin)]
[Route("MCRawMaterialCards")]
public sealed class MCRawMaterialCardsController : Controller
{
    private readonly IConfiguration _configuration;

    public MCRawMaterialCardsController(
        IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private async Task<SqliteConnection> OpenDatabase()
    {
        var connectionString =
            _configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Veritaban? ba?lant?s? bulunamad?.");

        var db = new SqliteConnection(connectionString);

        await db.OpenAsync();

        return db;
    }

    
    // MC_RAW_SEARCH_SAFE_V4
    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? q = null,
        string? durum = null)
    {
        q = (q ?? "").Trim();
        durum = (durum ?? "tumu").Trim().ToLowerInvariant();

        if (q.Length > 100)
            return BadRequest("Arama en fazla 100 karakter olabilir.");

        if (durum != "tumu" &&
            durum != "aktif" &&
            durum != "pasif")
            return BadRequest("Ge?ersiz durum filtresi.");

        var model = new MCRawCardsPage
        {
            Search = q,
            Status = durum
        };

        await using var db = await OpenDatabase();
        await using var cmd = db.CreateCommand();

        cmd.CommandText = @"
            SELECT
                Id,
                Code,
                Name,
                Unit,
                IsActive,
                MinimumStock
            FROM RawMaterials
            WHERE
                (
                    @q = ''
                    OR instr(lower(Code), lower(@q)) > 0
                    OR instr(lower(Name), lower(@q)) > 0
                    OR instr(lower(Unit), lower(@q)) > 0
                )
                AND
                (
                    @durum = 'tumu'
                    OR (@durum = 'aktif' AND IsActive = 1)
                    OR (@durum = 'pasif' AND IsActive = 0)
                )
            ORDER BY Id DESC
            LIMIT 200";

        cmd.Parameters.AddWithValue("@q", q);
        cmd.Parameters.AddWithValue("@durum", durum);

        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            model.Rows.Add(new MCRawCardRow
            {
                Id = reader.GetInt64(0),

                Code = Convert.ToString(
                    reader.GetValue(1)) ?? "",

                Name = Convert.ToString(
                    reader.GetValue(2)) ?? "",

                Unit = Convert.ToString(
                    reader.GetValue(3)) ?? "",

                IsActive = Convert.ToInt32(
                    reader.GetValue(4)) == 1,

                MinimumStock = Convert.ToDecimal(
                    reader.GetValue(5),
                    System.Globalization.CultureInfo.InvariantCulture)
            });
        }

        return View(model);
    }


    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        string? code,
        string? name,
        string? unit,
        string? minimumStock)
    {
        code = (code ?? "").Trim();
        name = (name ?? "").Trim();
        unit = (unit ?? "").Trim().ToUpperInvariant();

        if (code.Length < 1 || code.Length > 50 ||
            name.Length < 1 || name.Length > 200)
        {
            TempData["MCError"] =
                "Hammadde kodu ve ad? zorunludur.";
            return RedirectToAction(nameof(Index));
        }

        if (unit != "KG" && unit != "L" && unit != "ADET")
        {
            TempData["MCError"] =
                "Birim KG, L veya ADET olmal?d?r.";
            return RedirectToAction(nameof(Index));
        }

        var minimumText = (minimumStock ?? "0").Trim();

        if (!decimal.TryParse(
                minimumText,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var minimum))
        {
            TempData["MCError"] =
                "Minimum stok miktar? ge?erli de?il.";
            return RedirectToAction(nameof(Index));
        }

        if (minimum < 0 ||
            decimal.Round(minimum, 4) != minimum)
        {
            TempData["MCError"] =
                "Minimum stok negatif olamaz; en fazla 4 ondal?k kullan?labilir.";
            return RedirectToAction(nameof(Index));
        }

        var userId =
            User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        await using var db = await OpenDatabase();

        await using var transaction =
            await db.BeginTransactionAsync();

        await using var check = db.CreateCommand();

        check.Transaction = (SqliteTransaction)transaction;

        check.CommandText = @"
            SELECT COUNT(*)
            FROM RawMaterials
            WHERE UPPER(TRIM(Code)) = UPPER(@code)";

        check.Parameters.AddWithValue("@code", code);

        var exists =
            Convert.ToInt64(await check.ExecuteScalarAsync()) > 0;

        if (exists)
        {
            TempData["MCError"] =
                "Bu hammadde kodu zaten kay?tl?.";
            return RedirectToAction(nameof(Index));
        }

        await using var cmd = db.CreateCommand();

        cmd.Transaction = (SqliteTransaction)transaction;

        cmd.CommandText = @"INSERT INTO RawMaterials (Code, Name, Unit, IsActive, MinimumStock, CreatedAtUtc) VALUES (@code, @name, @unit, 1, @minimum, @created)";

        cmd.Parameters.AddWithValue("@code", code);
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@unit", unit);
        cmd.Parameters.AddWithValue("@minimum", minimum);
        cmd.Parameters.AddWithValue(
            "@created",
            DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@user", userId);

        try
        {
            await cmd.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }
        catch (SqliteException)
        {
            await transaction.RollbackAsync();

            TempData["MCError"] =
                "Hammadde kayd? yap?lamad?. Kod veya alanlar? kontrol edin.";

            return RedirectToAction(nameof(Index));
        }

        TempData["MCSuccess"] =
            "Hammadde kart? kaydedildi. Fiili stok miktar? de?i?medi.";

        return RedirectToAction(nameof(Index));
    }
}

public sealed class MCRawCardsPage
{
    public string Search { get; set; } = "";
    public string Status { get; set; } = "tumu";

    public List<MCRawCardRow> Rows { get; set; } = new();
}

public sealed class MCRawCardRow
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public bool IsActive { get; set; }
    public decimal MinimumStock { get; set; }
}
