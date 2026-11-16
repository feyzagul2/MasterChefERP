using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using MasterChefTakip.Data;
using MasterChefTakip.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace MasterChefTakip.Controllers;

[Authorize(Roles = AppRoles.Admin)]
public class MaterialCatalogController : Controller
{
    private readonly ApplicationDbContext _db;

    public MaterialCatalogController(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index(
        string? q,
        string? type,
        int page = 1)
    {
        page = Math.Clamp(page, 1, 100000);
        q = (q ?? "").Trim();
        type = (type ?? "").Trim();

        if (q.Length > 100)
            q = q[..100];

        var model = new MaterialCatalogPage
        {
            Search = q,
            Type = type,
            Page = page,
            PageSize = 20
        };

        await using var connection = new SqliteConnection(
            _db.Database.GetConnectionString());

        await connection.OpenAsync();

        var where = @"
            WHERE (@type = '' OR MaterialType = @type)
            AND (
                @q = ''
                OR MaterialCode LIKE @pattern
                OR MaterialName LIKE @pattern
            )";

        await using (var count = connection.CreateCommand())
        {
            count.CommandText =
                "SELECT COUNT(*) FROM MCMaterialCatalog " + where;

            AddParameters(count, q, type);

            model.Total = Convert.ToInt32(
                await count.ExecuteScalarAsync());
        }

        model.TotalPages = Math.Max(
            1,
            (int)Math.Ceiling(
                model.Total / (double)model.PageSize));

        model.Page = Math.Min(
            model.Page,
            model.TotalPages);

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT
                    Id,
                    COALESCE(MaterialCode, ''),
                    COALESCE(MaterialName, ''),
                    COALESCE(MaterialType, ''),
                    COALESCE(SheetName, ''),
                    ExcelRow,
                    COALESCE(OriginalRowJson, '[]')
                FROM MCMaterialCatalog "
                + where +
                @" ORDER BY Id
                   LIMIT @limit OFFSET @offset";

            AddParameters(cmd, q, type);

            cmd.Parameters.AddWithValue(
                "@limit", model.PageSize);

            cmd.Parameters.AddWithValue(
                "@offset",
                (model.Page - 1) * model.PageSize);

            await using var reader =
                await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                model.Rows.Add(new MaterialCatalogRow
                {
                    Id = reader.GetInt32(0),
                    Code = reader.GetString(1),
                    Name = reader.GetString(2),
                    Type = reader.GetString(3),
                    Sheet = reader.GetString(4),
                    ExcelRow = reader.GetInt32(5),
                    OriginalJson = reader.GetString(6)
                });
            }
        }

        await using (var types = connection.CreateCommand())
        {
            types.CommandText = @"
                SELECT
                    COALESCE(MaterialType, ''),
                    COUNT(*)
                FROM MCMaterialCatalog
                GROUP BY MaterialType
                ORDER BY COUNT(*) DESC";

            await using var reader =
                await types.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                model.Types.Add(new MaterialCatalogType
                {
                    Code = reader.GetString(0),
                    Count = reader.GetInt32(1)
                });
            }
        }

        return View(model);
    }

    private static void AddParameters(
        SqliteCommand cmd,
        string q,
        string type)
    {
        cmd.Parameters.AddWithValue("@q", q);

        var escaped = q
            .Replace("[", "[[]")
            .Replace("%", "[%]")
            .Replace("_", "[_]");

        cmd.Parameters.AddWithValue(
            "@pattern", "%" + escaped + "%");

        cmd.Parameters.AddWithValue("@type", type);
    }
}

public class MaterialCatalogPage
{
    public string Search { get; set; } = "";
    public string Type { get; set; } = "";
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public int TotalPages { get; set; }

    public List<MaterialCatalogRow> Rows { get; set; } = new();
    public List<MaterialCatalogType> Types { get; set; } = new();
}

public class MaterialCatalogType
{
    public string Code { get; set; } = "";
    public int Count { get; set; }
}

public class MaterialCatalogRow
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Sheet { get; set; } = "";
    public int ExcelRow { get; set; }
    public string OriginalJson { get; set; } = "[]";

    public List<string> OriginalValues
    {
        get
        {
            try
            {
                using var document =
                    JsonDocument.Parse(OriginalJson);

                return document.RootElement
                    .EnumerateArray()
                    .Select(x =>
                        x.ValueKind == JsonValueKind.Null
                            ? ""
                            : x.ToString())
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }
    }
}
