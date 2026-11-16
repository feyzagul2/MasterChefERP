// MC_MATERIAL_PICKER_2026
using MasterChefTakip.Data;
using MasterChefTakip.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

[Authorize(Roles = AppRoles.Admin)]
public class MaterialLookupController : Controller
{
    private readonly ApplicationDbContext _db;

    public MaterialLookupController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Search(string? q)
    {
        q = (q ?? "").Trim();

        if (q.Length < 2)
            return Json(Array.Empty<object>());

        if (q.Length > 100)
            q = q[..100];

        await using var connection = new SqliteConnection(
            _db.Database.GetConnectionString());

        await connection.OpenAsync();

        await using var command = connection.CreateCommand();

        command.CommandText = @"
            SELECT
                COALESCE(MaterialCode, ''),
                COALESCE(MaterialName, ''),
                COALESCE(MaterialType, '')
            FROM MCMaterialCatalog
            WHERE
                MaterialCode LIKE @contains
                OR MaterialName LIKE @contains
            ORDER BY
                CASE
                    WHEN MaterialCode = @exact THEN 0
                    WHEN MaterialCode LIKE @starts THEN 1
                    ELSE 2
                END,
                MaterialCode
            LIMIT 20";

        command.Parameters.AddWithValue("@exact", q);
        command.Parameters.AddWithValue("@starts", q + "%");
        command.Parameters.AddWithValue("@contains", "%" + q + "%");

        var results = new List<object>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            results.Add(new
            {
                code = reader.GetString(0),
                name = reader.GetString(1),
                type = reader.GetString(2)
            });
        }

        return Json(results);
    }
}
