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

// MC_AUDIT_DETAILS_V1 - Salt okunur.
[Authorize(Roles = AppRoles.Admin)]
[Route("MCInventoryAuditDetails")]
public sealed class MCInventoryAuditDetailsController : Controller
{
    private readonly ApplicationDbContext _db;

    public MCInventoryAuditDetailsController(ApplicationDbContext db)
    {
        _db = db;
    }

    
    [HttpGet("Export")]
    public async Task<IActionResult> Export(
        string? tur, string? q = null)
    {
        var result = await Index(tur, q);

        if (result is not ViewResult viewResult ||
            viewResult.Model is not MCAuditDetailsPage model)
        {
            return result;
        }

        static string Cell(string? value)
        {
            var text = (value ?? "")
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("\t", " ");

            if (text.TrimStart().Length > 0 &&
                "=+-@".Contains(text.TrimStart()[0]))
            {
                text = "'" + text;
            }

            var quote = ((char)34).ToString();

            return quote +
                text.Replace(quote, quote + quote) +
                quote;
        }

        var csv = new System.Text.StringBuilder();

        csv.AppendLine("sep=;");
        csv.AppendLine("Kart ID;Depo ID;Birim;Sorun");

        foreach (var row in model.Rows)
        {
            csv.Append(Cell(row.CardId.ToString())).Append(';')
               .Append(Cell(row.WarehouseId?.ToString())).Append(';')
               .Append(Cell(row.Unit)).Append(';')
               .AppendLine(Cell(row.Problem));
        }

        var encoding = new System.Text.UTF8Encoding(true);
        var bom = encoding.GetPreamble();
        var body = encoding.GetBytes(csv.ToString());

        var bytes = new byte[bom.Length + body.Length];

        Buffer.BlockCopy(bom, 0, bytes, 0, bom.Length);
        Buffer.BlockCopy(body, 0, bytes, bom.Length, body.Length);

        return File(
            bytes,
            "text/csv; charset=utf-8",
            "MASTER_CHEF_Stok_Denetim_Detay.csv");
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? tur, string? q = null)
    {
        var model = new MCAuditDetailsPage
        {
            Type = tur ?? "eksik",
            Search = (q ?? "").Trim()
        };

        if (model.Type != "eksik" &&
            model.Type != "baglanti" &&
            model.Type != "birim")
            return BadRequest("Ge?ersiz denetim t?r?.");

        await using var db = new SqliteConnection(
            _db.Database.GetDbConnection().ConnectionString);

        await db.OpenAsync();

        foreach (var name in new[]
        {
            "MCInventoryCards",
            "MCInventoryWarehouseLinks",
            "RawMaterialWarehouses"
        })
        {
            await using var check = db.CreateCommand();
            check.CommandText =
                "SELECT COUNT(*) FROM sqlite_master " +
                "WHERE type='table' AND name=@name";
            check.Parameters.AddWithValue("@name", name);

            if (Convert.ToInt64(
                await check.ExecuteScalarAsync() ?? 0L) != 1)
            {
                model.Message = "Gerekli tablo bulunamad?: " + name;
                return View(model);
            }
        }

        string sql;

        switch (model.Type)
        {
            case "baglanti":
                model.Title = "Ge?ersiz veya Pasif Depo Ba?lant?lar?";
                sql = @"
                    SELECT COALESCE(c.Id, l.CardId),
                           COALESCE(c.Unit, ''),
                           l.WarehouseId,
                           CASE
                               WHEN c.Id IS NULL THEN 'Stok kart? bulunamad?'
                               WHEN w.Id IS NULL THEN 'Depo bulunamad?'
                               WHEN c.IsActive <> 1 THEN 'Stok kart? pasif'
                               WHEN w.IsActive <> 1 THEN 'Depo pasif'
                               ELSE 'Kontrol gerekli'
                           END
                    FROM MCInventoryWarehouseLinks l
                    LEFT JOIN MCInventoryCards c
                        ON c.Id=l.CardId
                    LEFT JOIN RawMaterialWarehouses w
                        ON w.Id=l.WarehouseId
                    WHERE c.Id IS NULL
                       OR w.Id IS NULL
                       OR c.IsActive<>1
                       OR w.IsActive<>1
                    ORDER BY l.CardId,l.WarehouseId
                    LIMIT 500";
                break;

            case "birim":
                model.Title = "Tan?nmayan ?l?? Birimleri";
                sql = @"
                    SELECT Id,
                           COALESCE(Unit,''),
                           NULL,
                           '?l?? birimi do?rulanmal?'
                    FROM MCInventoryCards
                    WHERE IsActive=1
                      AND UPPER(TRIM(COALESCE(Unit,''))) NOT IN
                      (
                          'KG','KILOGRAM','K?LOGRAM',
                          'GR','G','GRAM','TON',
                          'L','LT','LITRE','L?TRE','LITER',
                          'ML','MILILITRE','M?L?L?TRE',
                          'ADET','METRE','M'
                      )
                    ORDER BY Id
                    LIMIT 500";
                break;

            default:
                model.Title = "Aktif Depoya Ba?lanmam?? Kartlar";
                sql = @"
                    SELECT c.Id,
                           COALESCE(c.Unit,''),
                           NULL,
                           'Aktif depo e?le?tirmesi yok'
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
                      )
                    ORDER BY c.Id
                    LIMIT 500";
                break;
        }

        await using var cmd = db.CreateCommand();
        
        // MC_AUDIT_DATABASE_SEARCH_V2
        // Arama LIMIT uygulanmadan once veritabaninda yapilir.
        if (model.Search.Length > 100)
            return BadRequest("Arama en fazla 100 karakter olabilir.");

        if (!string.IsNullOrWhiteSpace(model.Search))
        {
            var baseSql = System.Text.RegularExpressions.Regex.Replace(
                sql.Trim(),
                @"\s+LIMIT\s+500\s*;?\s*$",
                "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (baseSql == sql.Trim())
                throw new InvalidOperationException(
                    "Denetim sorgusunun LIMIT bolumu bulunamadi.");

            sql = @"
                WITH audit(CardId, Unit, WarehouseId, Problem)
                AS
                (
                    " + baseSql + @"
                )
                SELECT CardId, Unit, WarehouseId, Problem
                FROM audit
                WHERE instr(
                    lower(CAST(CardId AS TEXT)),
                    lower(@search)) > 0
                   OR instr(
                    lower(COALESCE(CAST(WarehouseId AS TEXT),'')),
                    lower(@search)) > 0
                   OR instr(
                    lower(COALESCE(Unit,'')),
                    lower(@search)) > 0
                   OR instr(
                    lower(COALESCE(Problem,'')),
                    lower(@search)) > 0
                ORDER BY CardId
                LIMIT 500";
        }

        cmd.CommandText = sql;

        if (!string.IsNullOrWhiteSpace(model.Search))
        {
            cmd.Parameters.AddWithValue("@search", model.Search);
        }


        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            model.Rows.Add(new MCAuditDetailsRow
            {
                CardId = reader.GetInt64(0),
                Unit = reader.GetString(1),
                WarehouseId = reader.IsDBNull(2)
                    ? null
                    : reader.GetInt64(2),
                Problem = reader.GetString(3)
            });
        }

        
        return View(model);
    }
}

public sealed class MCAuditDetailsPage
{
    public string Search { get; set; } = "";

    public string Type { get; set; } = "eksik";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";

    public List<MCAuditDetailsRow> Rows { get; set; } = new();
}

public sealed class MCAuditDetailsRow
{
    public long CardId { get; set; }
    public long? WarehouseId { get; set; }
    public string Unit { get; set; } = "";
    public string Problem { get; set; } = "";
}
