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

// MC_RECEIPT_PREPARATION_V1
// Sadece mevcut stok karti ve depo baglantilarini listeler.
[Authorize(Roles = AppRoles.Admin)]
[Route("MCInventoryReceiptPreparation")]
public sealed class MCInventoryReceiptPreparationController : Controller
{
    private readonly ApplicationDbContext _db;

    public MCInventoryReceiptPreparationController(
        ApplicationDbContext db)
    {
        _db = db;
    }

    
    // MC_RECEIPT_EXPORT_FIXED_V2
    [HttpGet("Export")]
    public async Task<IActionResult> Export(string? q = null, string? durum = null)
    {
        var result = await Index(q, durum);

        if (result is not ViewResult viewResult ||
            viewResult.Model is not MCReceiptPreparationPage model)
        {
            return result;
        }

        static string Cell(string? value)
        {
            var text = (value ?? "")
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("\t", " ");

            var trimmed = text.TrimStart();

            if (trimmed.Length > 0 &&
                "=+-@".Contains(trimmed[0]))
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
        csv.AppendLine(
            "Kart ID;Depo ID;?l?? Birimi;E?le?tirme Durumu");

        foreach (var row in model.Rows)
        {
            csv.Append(Cell(row.CardId.ToString())).Append(';')
               .Append(Cell(row.WarehouseId.ToString())).Append(';')
               .Append(Cell(row.Unit)).Append(';')
               .AppendLine(Cell("Aktif depoya ba?l?"));
        }

        foreach (var card in model.UnassignedCards)
        {
            csv.Append(Cell(card.CardId.ToString())).Append(';')
               .Append(Cell("")).Append(';')
               .Append(Cell(card.Unit)).Append(';')
               .AppendLine(Cell("Aktif depo e?le?tirmesi yok"));
        }

        var encoding = new System.Text.UTF8Encoding(true);
        var bom = encoding.GetPreamble();
        var body = encoding.GetBytes(csv.ToString());

        var bytes = new byte[bom.Length + body.Length];

        System.Buffer.BlockCopy(
            bom, 0, bytes, 0, bom.Length);

        System.Buffer.BlockCopy(
            body, 0, bytes, bom.Length, body.Length);

        return File(
            bytes,
            "text/csv; charset=utf-8",
            "MASTER_CHEF_Mal_Kabul_Hazirlik.csv");
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q = null, string? durum = null)
    {
        var model = new MCReceiptPreparationPage
        {
            Search = (q ?? "").Trim(),
            StatusFilter = (durum ?? "tumu").Trim().ToLowerInvariant()
        };

        await using var db = new SqliteConnection(
            _db.Database.GetDbConnection().ConnectionString);

        await db.OpenAsync();

        async Task<bool> HasTable(string table)
        {
            await using var command = db.CreateCommand();

            command.CommandText = @"
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type='table' AND name=@name";

            command.Parameters.AddWithValue("@name", table);

            return Convert.ToInt64(
                await command.ExecuteScalarAsync() ?? 0L) == 1;
        }

        foreach (var table in new[]
        {
            "MCInventoryCards",
            "MCInventoryWarehouseLinks",
            "RawMaterialWarehouses"
        })
        {
            if (!await HasTable(table))
            {
                model.Message =
                    "Hen?z gerekli stok kart? veya depo tablosu yok: "
                    + table;

                return View(model);
            }
        }

        await using var command = db.CreateCommand();

        command.CommandText = @"
            SELECT c.Id,
                   COALESCE(c.Unit,''),
                   w.Id
            FROM MCInventoryWarehouseLinks l
            INNER JOIN MCInventoryCards c
                ON c.Id=l.CardId
            INNER JOIN RawMaterialWarehouses w
                ON w.Id=l.WarehouseId
            WHERE c.IsActive=1
              AND w.IsActive=1
            ORDER BY c.Id, w.Id
            LIMIT 5000";

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            model.Rows.Add(new MCReceiptPreparationRow
            {
                CardId = reader.GetInt64(0),
                Unit = reader.GetString(1),
                WarehouseId = reader.GetInt64(2)
            });
        }

        
        // MC_UNASSIGNED_STOCK_CARDS_V2
        // Aktif depoya baglanmamis genel stok kartlari.
        await using (var unassignedCommand = db.CreateCommand())
        {
            unassignedCommand.CommandText = @"
                SELECT c.Id, COALESCE(c.Unit,'')
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
                LIMIT 5000";

            await using var unassignedReader =
                await unassignedCommand.ExecuteReaderAsync();

            while (await unassignedReader.ReadAsync())
            {
                model.UnassignedCards.Add(
                    new MCReceiptUnassignedCard
                    {
                        CardId = unassignedReader.GetInt64(0),
                        Unit = unassignedReader.GetString(1)
                    });
            }
        }

        
        // MC_RECEIPT_SEARCH_V1
        if (!string.IsNullOrWhiteSpace(model.Search))
        {
            if (model.Search.Length > 100)
                return BadRequest("Arama en fazla 100 karakter olabilir.");

            var term = model.Search;

            model.Rows.RemoveAll(x =>
                !x.CardId.ToString().Contains(term,
                    StringComparison.OrdinalIgnoreCase) &&
                !x.WarehouseId.ToString().Contains(term,
                    StringComparison.OrdinalIgnoreCase) &&
                !(x.Unit ?? "").Contains(term,
                    StringComparison.OrdinalIgnoreCase));

            model.UnassignedCards.RemoveAll(x =>
                !x.CardId.ToString().Contains(term,
                    StringComparison.OrdinalIgnoreCase) &&
                !(x.Unit ?? "").Contains(term,
                    StringComparison.OrdinalIgnoreCase));
        }

        
        // MC_RECEIPT_STATUS_FILTER_V1
        if (model.StatusFilter != "tumu" &&
            model.StatusFilter != "bagli" &&
            model.StatusFilter != "baglanmamis")
        {
            return BadRequest("Ge?ersiz e?le?tirme filtresi.");
        }

        if (model.StatusFilter == "bagli")
        {
            model.UnassignedCards.Clear();
        }
        else if (model.StatusFilter == "baglanmamis")
        {
            model.Rows.Clear();
        }

        if (model.Rows.Count == 0)
        {
            model.Message =
                "Mal kabule haz?rlanm?? aktif kart-depo e?le?tirmesi "
                + "hen?z bulunmuyor.";
        }

        return View(model);
    }
}

public sealed class MCReceiptPreparationPage
{
    public string StatusFilter { get; set; } = "tumu";

    public string Search { get; set; } = "";

    public List<MCReceiptUnassignedCard> UnassignedCards
        { get; set; } = new();

    public string Message { get; set; } = "";

    public List<MCReceiptPreparationRow> Rows { get; set; } = new();
}


// MC_UNIT_SAFETY_V1
public static class MCReceiptUnitSafety
{
    public static string Category(string? unit)
    {
        var value = (unit ?? "")
            .Trim()
            .ToUpperInvariant();

        return value switch
        {
            "KG" or "KILOGRAM" or "K?LOGRAM"
                or "GR" or "G" or "GRAM"
                or "TON" => "A??rl?k",

            "L" or "LT" or "LITRE" or "L?TRE"
                or "LITER" or "ML"
                or "MILILITRE" or "M?L?L?TRE"
                => "Hacim",

            "ADET" => "Adet",

            "METRE" or "M" => "Uzunluk",

            _ => "Tan?mlanmam??"
        };
    }

    public static bool IsSupported(string? unit)
        => Category(unit) != "Tan?mlanmam??";
}

public sealed class MCReceiptPreparationRow
{
    public long CardId { get; set; }
    public long WarehouseId { get; set; }
    public string Unit { get; set; } = "";
}


public sealed class MCReceiptUnassignedCard
{
    public long CardId { get; set; }
    public string Unit { get; set; } = "";
}
