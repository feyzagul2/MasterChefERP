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

// MC_UNIFIED_STOCK_LIST_V3
[Authorize(Roles = AppRoles.Admin)]
[Route("MCInventoryCards")]
public class MCInventoryListController : Controller
{
    private readonly ApplicationDbContext _db;

    public MCInventoryListController(ApplicationDbContext db)
    {
        _db = db;
    }

    private static async Task<bool> Exists(
        SqliteConnection db, string table)
    {
        await using var cmd = db.CreateCommand();

        cmd.CommandText =
            "SELECT COUNT(*) FROM sqlite_master " +
            "WHERE type='table' AND name=@name";

        cmd.Parameters.AddWithValue("@name", table);

        return Convert.ToInt64(
            await cmd.ExecuteScalarAsync() ?? 0L) > 0;
    }

    
    // MC_STOCK_CSV_EXPORT_V1
    
    // MC_FULL_EXCEL_EXPORT_V2
    
    // MC_FULL_EXCEL_EXPORT_V2
    [HttpGet("Export")]
    public async Task<IActionResult> Export(
        string? q, string? durum, string? eksikAlan)
    {
        var response = await Index(q, durum, eksikAlan);

        if (response is not ViewResult viewResult ||
            viewResult.Model is not MCInventoryListPage model)
        {
            return response;
        }

        var details = new Dictionary<int, string[]>();

        await using var db = new SqliteConnection(
            _db.Database.GetConnectionString());

        await db.OpenAsync();

        if (await Exists(db, "MCInventoryCardDetails"))
        {
            await using var command = db.CreateCommand();

            command.CommandText = @"
                SELECT CatalogId,
                       COALESCE(Barcode,''),
                       COALESCE(MinimumStock,''),
                       COALESCE(CriticalStock,''),
                       COALESCE(Supplier,''),
                       COALESCE(PurchasePrice,''),
                       COALESCE(Currency,''),
                       COALESCE(VatRate,''),
                       COALESCE(ShelfLocation,''),
                       COALESCE(Notes,'')
                FROM MCInventoryCardDetails";

            await using var reader =
                await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var id = Convert.ToInt32(reader.GetInt64(0));
                var values = new string[9];

                for (var i = 0; i < 9; i++)
                    values[i] = reader.GetString(i + 1);

                details[id] = values;
            }
        }

        static string Cell(string? value)
        {
            var text = (value ?? "")
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("\t", " ");

            var trimmed = text.TrimStart();

            // Excel formulu olarak calismasini engelle.
            if (trimmed.Length > 0 &&
                "=+-@".Contains(trimmed[0]))
            {
                text = "'" + text;
            }

            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        var csv = new System.Text.StringBuilder();

        csv.AppendLine("sep=;");

        csv.AppendLine(
            "Malzeme Kodu;Malzeme Ad?;T?r;?l?? Birimi;" +
            "Kart Grubu;Barkod;Minimum Stok;Kritik Stok;" +
            "Tedarik?i;Al?? Fiyat?;Para Birimi;KDV Oran?;" +
            "Raf Konumu;A??klama;Dolu Ek Bilgi Say?s?");

        foreach (var card in model.Cards)
        {
            if (!details.TryGetValue(
                card.CatalogId, out var values))
            {
                values = new string[9];
                Array.Fill(values, "");
            }

            var columns = new List<string>
            {
                Cell(card.Code),
                Cell(card.Name),
                Cell(card.Type),
                Cell(card.Unit),
                Cell(card.Source)
            };

            foreach (var value in values)
                columns.Add(Cell(value));

            columns.Add(
                Cell(card.SavedDetailCount.ToString()));

            csv.AppendLine(string.Join(";", columns));
        }

        var encoding = new System.Text.UTF8Encoding(true);
        var preamble = encoding.GetPreamble();
        var content = encoding.GetBytes(csv.ToString());

        var bytes = new byte[preamble.Length + content.Length];

        Buffer.BlockCopy(
            preamble, 0, bytes, 0, preamble.Length);

        Buffer.BlockCopy(
            content, 0, bytes,
            preamble.Length, content.Length);

        return File(
            bytes,
            "text/csv; charset=utf-8",
            "MASTER_CHEF_Tum_Stok_Karti_Bilgileri.csv");
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, string? durum, string? eksikAlan = null)
    {
        var model = new MCInventoryListPage
        {
            Search = (q ?? "").Trim(),
            MissingFieldFilter = (eksikAlan ?? "").Trim(),
            Filter = (durum ?? "tumu").Trim().ToLowerInvariant()
        };

        if (model.Search.Length > 150)
            return BadRequest("Arama ifadesi ?ok uzun.");

        await using var db = new SqliteConnection(
            _db.Database.GetConnectionString());

        await db.OpenAsync();

        bool hasGeneral = await Exists(db, "MCInventoryCards");
        bool hasRaw = await Exists(db, "RawMaterials");
        bool hasLinks = await Exists(db, "MCMaterialStockCardLinks");

        if (hasGeneral)
        {
            await using var cmd = db.CreateCommand();

            cmd.CommandText = @"
                SELECT
                    CatalogId,
                    COALESCE(MaterialCode,''),
                    COALESCE(MaterialName,''),
                    COALESCE(MaterialType,''),
                    COALESCE(Unit,'')
                FROM MCInventoryCards
                WHERE
                    @q = ''
                    OR MaterialCode LIKE '%' || @q || '%'
                    OR MaterialName LIKE '%' || @q || '%'
                    OR MaterialType LIKE '%' || @q || '%'
                ORDER BY MaterialCode
                LIMIT 5000";

            cmd.Parameters.AddWithValue("@q", model.Search);

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                model.Cards.Add(new MCInventoryListItem
                {
                    CatalogId = Convert.ToInt32(reader.GetInt64(0)),
                    Code = reader.GetString(1),
                    Name = reader.GetString(2),
                    Type = reader.GetString(3),
                    Unit = reader.GetString(4),
                    Source = "Genel Malzeme"
                });

                model.GeneralCount++;
            }
        }

        if (hasRaw && hasLinks)
        {
            await using var cmd = db.CreateCommand();

            var sql = @"
                SELECT
                    l.MaterialCatalogId,
                    COALESCE(r.Code,''),
                    COALESCE(r.Name,''),
                    COALESCE(r.Category,''),
                    COALESCE(r.Unit,'')
                FROM MCMaterialStockCardLinks l
                INNER JOIN RawMaterials r
                    ON r.Id = l.RawMaterialId
                WHERE
                    (
                        @q = ''
                        OR r.Code LIKE '%' || @q || '%'
                        OR r.Name LIKE '%' || @q || '%'
                        OR r.Category LIKE '%' || @q || '%'
                    )";

            if (hasGeneral)
            {
                sql += @"
                    AND NOT EXISTS
                    (
                        SELECT 1
                        FROM MCInventoryCards g
                        WHERE g.CatalogId = l.MaterialCatalogId
                    )";
            }

            sql += " ORDER BY r.Code LIMIT 5000";

            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@q", model.Search);

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                model.Cards.Add(new MCInventoryListItem
                {
                    CatalogId = Convert.ToInt32(reader.GetInt64(0)),
                    Code = reader.GetString(1),
                    Name = reader.GetString(2),
                    Type = reader.GetString(3),
                    Unit = reader.GetString(4),
                    Source = "Hammadde"
                });

                model.RawCount++;
            }
        }

        
        // MC_CARD_COMPLETENESS_V2
        if (await Exists(db, "MCInventoryCardDetails"))
        {
            var detailCounts = new Dictionary<int, int>();

            await using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT CatalogId,
                           COALESCE(Barcode,''),
                           COALESCE(MinimumStock,''),
                           COALESCE(CriticalStock,''),
                           COALESCE(Supplier,''),
                           COALESCE(PurchasePrice,''),
                           COALESCE(Currency,''),
                           COALESCE(VatRate,''),
                           COALESCE(ShelfLocation,''),
                           COALESCE(Notes,'')
                    FROM MCInventoryCardDetails";

                await using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var catalogId = Convert.ToInt32(reader.GetInt64(0));
                    var count = 0;

                    for (int i = 1; i <= 9; i++)
                    {
                        if (!string.IsNullOrWhiteSpace(reader.GetString(i)))
                            count++;
                    }

                    detailCounts[catalogId] = count;
                }
            }

            foreach (var card in model.Cards)
            {
                if (detailCounts.TryGetValue(
                    card.CatalogId, out var count))
                {
                    card.HasDetailsRecord = true;
                    card.SavedDetailCount = count;
                }
            }
        }

        
        // MC_CARD_FILTER_V1
        if (model.Filter != "tumu" &&
            model.Filter != "eksik" &&
            model.Filter != "tam" &&
            model.Filter != "kaydedilmedi")
        {
            model.Filter = "tumu";
        }

        if (model.Filter == "eksik")
        {
            model.Cards.RemoveAll(x =>
                x.SavedDetailCount >= 9);
        }
        else if (model.Filter == "tam")
        {
            model.Cards.RemoveAll(x =>
                x.SavedDetailCount != 9);
        }
        else if (model.Filter == "kaydedilmedi")
        {
            model.Cards.RemoveAll(x =>
                x.HasDetailsRecord);
        }

        
        // MC_MISSING_NAMES_FIXED_V2
        if (await Exists(db, "MCInventoryCardDetails"))
        {
            var missingByCatalog = new Dictionary<int, string>();

            string[] fieldNames =
            {
                "Barkod",
                "Minimum Stok",
                "Kritik Stok",
                "Tedarik?i",
                "Al?? Fiyat?",
                "Para Birimi",
                "KDV Oran?",
                "Raf Konumu",
                "A??klama"
            };

            await using (var missingCommand = db.CreateCommand())
            {
                missingCommand.CommandText = @"
                    SELECT CatalogId,
                           COALESCE(Barcode,''),
                           COALESCE(MinimumStock,''),
                           COALESCE(CriticalStock,''),
                           COALESCE(Supplier,''),
                           COALESCE(PurchasePrice,''),
                           COALESCE(Currency,''),
                           COALESCE(VatRate,''),
                           COALESCE(ShelfLocation,''),
                           COALESCE(Notes,'')
                    FROM MCInventoryCardDetails";

                await using var reader =
                    await missingCommand.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var catalogId = Convert.ToInt32(reader.GetInt64(0));
                    var missingNames = new List<string>();

                    for (int i = 1; i <= 9; i++)
                    {
                        if (string.IsNullOrWhiteSpace(reader.GetString(i)))
                            missingNames.Add(fieldNames[i - 1]);
                    }

                    missingByCatalog[catalogId] =
                        string.Join(", ", missingNames);
                }
            }

            foreach (var card in model.Cards)
            {
                if (missingByCatalog.TryGetValue(
                    card.CatalogId, out var missing))
                {
                    card.MissingFieldNames = missing;
                }
            }
        }

        
        // MC_FIELD_FILTER_SAFE_V2
        string[] allowedMissingFields =
        {
            "Barkod",
            "Minimum Stok",
            "Kritik Stok",
            "Tedarik?i",
            "Al?? Fiyat?",
            "Para Birimi",
            "KDV Oran?",
            "Raf Konumu",
            "A??klama"
        };

        if (!string.IsNullOrWhiteSpace(model.MissingFieldFilter))
        {
            if (!allowedMissingFields.Contains(
                model.MissingFieldFilter,
                StringComparer.Ordinal))
            {
                return BadRequest("Ge?ersiz alan filtresi.");
            }

            model.Cards.RemoveAll(card =>
                card.HasDetailsRecord &&
                !card.MissingFieldNames
                    .Split(',', StringSplitOptions.TrimEntries)
                    .Contains(
                        model.MissingFieldFilter,
                        StringComparer.Ordinal));
        }

        model.GeneralCount = model.Cards.Count(
            x => x.Source == "Genel Malzeme");

        model.RawCount = model.Cards.Count(
            x => x.Source == "Hammadde");

        model.Cards.Sort((a, b) =>
            string.Compare(
                a.Code,
                b.Code,
                StringComparison.CurrentCultureIgnoreCase));

        return View(model);
    }
}

public class MCInventoryListPage
{
    public string MissingFieldFilter { get; set; } = "";

    public string Search { get; set; } = "";
    public string Filter { get; set; } = "tumu";
    public int GeneralCount { get; set; }
    public int RawCount { get; set; }

    public List<MCInventoryListItem> Cards { get; set; } = new();
}

public class MCInventoryListItem
{
    public string MissingFieldNames { get; set; } = "";

    public int SavedDetailCount { get; set; }
    public bool HasDetailsRecord { get; set; }

    public int CatalogId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Source { get; set; } = "";
}
