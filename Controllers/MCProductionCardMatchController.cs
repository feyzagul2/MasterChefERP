using MasterChefTakip.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;

namespace MasterChefTakip.Controllers;

[Authorize(Policy = AppPermissions.ProductionManage)]
[ApiController]
[Route("mc-production-card-match")]
public sealed class MCProductionCardMatchController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public MCProductionCardMatchController(
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    private SqliteConnection OpenReadOnly()
    {
        var configured = _configuration.GetConnectionString(
            "DefaultConnection");

        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException(
                "DefaultConnection bulunamadi.");

        var builder = new SqliteConnectionStringBuilder(
            configured);

        if (string.IsNullOrWhiteSpace(builder.DataSource) ||
            builder.DataSource == ":memory:")
            throw new InvalidOperationException(
                "Fiziksel veritabani bulunamadi.");

        var selected = Path.GetFullPath(
            Path.IsPathRooted(builder.DataSource)
                ? builder.DataSource
                : Path.Combine(
                    _environment.ContentRootPath,
                    builder.DataSource));

        var expected = Path.GetFullPath(
            Path.Combine(
                _environment.ContentRootPath,
                "app.db"));

        if (!string.Equals(
            selected,
            expected,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Beklenmeyen veritabani baglantisi.");
        }

        if (!System.IO.File.Exists(selected))
            throw new InvalidOperationException(
                "Veritabani dosyasi bulunamadi.");

        var readOnly = new SqliteConnectionStringBuilder
        {
            DataSource = selected,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        };

        var db = new SqliteConnection(readOnly.ToString());
        db.Open();

        return db;
    }

    private static async Task<bool> TableExists(
        SqliteConnection db,
        string name)
    {
        using var command = db.CreateCommand();

        command.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name = $name
            """;

        command.Parameters.AddWithValue("$name", name);

        return Convert.ToInt64(
            await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task<HashSet<string>> Columns(
        SqliteConnection db,
        string table)
    {
        var result = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        using var command = db.CreateCommand();

        command.CommandText =
            "PRAGMA table_info(\"" +
            table.Replace("\"", "\"\"") +
            "\")";

        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
            result.Add(reader.GetString(1));

        return result;
    }

    private static string? FirstColumn(
        HashSet<string> columns,
        params string[] candidates)
    {
        return candidates.FirstOrDefault(columns.Contains);
    }

    [HttpGet("receipt/{receiptId:long}")]
    public async Task<IActionResult> MatchReceipt(
        long receiptId)
    {
        if (receiptId <= 0)
            return BadRequest(new
            {
                error = "Gecerli mamul girisi gerekli."
            });

        try
        {
            using var db = OpenReadOnly();

            const string receiptTable =
                "MCProductionFinishedGoodsReceipts";

            if (!await TableExists(db, receiptTable))
                return Conflict(new
                {
                    status = "ProductionSchemaMissing",
                    message = "Mamul girisi tablosu bulunamadi."
                });

            long orderId;
            long cardId;
            string cardTable;
            string cardLabel;
            decimal actualKg;

            using (var command = db.CreateCommand())
            {
                command.CommandText = """
                    SELECT
                        OrderId,
                        CardId,
                        COALESCE(CardTable, ''),
                        COALESCE(CardLabel, ''),
                        ActualKg
                    FROM MCProductionFinishedGoodsReceipts
                    WHERE Id = $id
                    """;

                command.Parameters.AddWithValue(
                    "$id", receiptId);

                using var reader =
                    await command.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                    return NotFound(new
                    {
                        status = "ReceiptNotFound",
                        receiptId
                    });

                orderId = Convert.ToInt64(
                    reader.GetValue(0));

                cardId = reader.IsDBNull(1)
                    ? 0
                    : Convert.ToInt64(reader.GetValue(1));

                cardTable = reader.GetString(2);
                cardLabel = reader.GetString(3);

                actualKg = reader.IsDBNull(4)
                    ? 0
                    : Convert.ToDecimal(reader.GetValue(4));
            }

            if (cardId <= 0)
                return Ok(new
                {
                    status = "CardMissing",
                    receiptId,
                    orderId,
                    cardTable,
                    cardId,
                    cardLabel,
                    actualKg,
                    verified = false,
                    message = "Mamul girisinde urun karti yok."
                });

            if (!string.Equals(
                cardTable,
                "Products",
                StringComparison.OrdinalIgnoreCase))
            {
                return Ok(new
                {
                    status = "MappingRequired",
                    receiptId,
                    orderId,
                    cardTable,
                    cardId,
                    cardLabel,
                    actualKg,
                    verified = false,
                    message =
                        "Uretim karti Products tablosuna ait degil. " +
                        "CardId ile ProductId ayni kabul edilmedi."
                });
            }

            if (!await TableExists(db, "Products"))
                return Ok(new
                {
                    status = "ProductsTableMissing",
                    receiptId,
                    cardId,
                    verified = false,
                    message =
                        "Palet urun karti tablosu dogrulanamadi."
                });

            var fields = await Columns(db, "Products");

            if (!fields.Contains("Id"))
                return Conflict(new
                {
                    status = "ProductIdMissing",
                    verified = false
                });

            var weightColumn = FirstColumn(
                fields,
                "UnitGrams",
                "WeightGrams",
                "NetWeightGrams",
                "Gramaj");

            var boxColumn = FirstColumn(
                fields,
                "UnitsPerBox",
                "PiecesPerBox",
                "QuantityPerBox",
                "BoxQuantity",
                "KoliIciAdet");

            object? gramValue = null;
            object? boxValue = null;

            using (var command = db.CreateCommand())
            {
                var selectWeight = weightColumn is null
                    ? "NULL"
                    : "\"" + weightColumn + "\"";

                var selectBox = boxColumn is null
                    ? "NULL"
                    : "\"" + boxColumn + "\"";

                command.CommandText =
                    "SELECT " + selectWeight +
                    ", " + selectBox +
                    " FROM Products WHERE Id = $id";

                command.Parameters.AddWithValue(
                    "$id", cardId);

                using var reader =
                    await command.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                    return Ok(new
                    {
                        status = "ProductNotFound",
                        receiptId,
                        cardId,
                        verified = false
                    });

                if (!reader.IsDBNull(0))
                    gramValue = reader.GetValue(0);

                if (!reader.IsDBNull(1))
                    boxValue = reader.GetValue(1);
            }

            decimal? unitGrams = null;
            int? unitsPerBox = null;

            if (gramValue is not null &&
                decimal.TryParse(
                    Convert.ToString(
                        gramValue,
                        System.Globalization.CultureInfo.InvariantCulture),
                    System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var grams) &&
                grams > 0)
            {
                unitGrams = grams;
            }

            if (boxValue is not null &&
                int.TryParse(
                    Convert.ToString(
                        boxValue,
                        System.Globalization.CultureInfo.InvariantCulture),
                    out var boxes) &&
                boxes > 0)
            {
                unitsPerBox = boxes;
            }

            // Urun kimligi dogrulanabilir; ambalaj bilgileri
            // eksikse paketleme hesabina izin verilmez.

            var packagingReady =
                unitGrams.HasValue &&
                unitsPerBox.HasValue;

            return Ok(new
            {
                status = packagingReady
                    ? "PackagingReady"
                    : "PackagingDataMissing",

                receiptId,
                orderId,
                cardTable,
                cardId,
                cardLabel,
                productId = cardId,
                actualKg,
                unitGrams,
                unitsPerBox,
                weightColumn,
                boxColumn,
                verified = true,
                packagingReady,

                message = packagingReady
                    ? "Urun karti ve ambalaj bilgileri bulundu."
                    : "Urun karti bulundu ancak gramaj veya " +
                      "koli ici adet bilgisi eksik."
            });
        }
        catch (SqliteException)
        {
            return StatusCode(500, new
            {
                status = "DatabaseReadFailed",
                message = "Urun karti bilgileri okunamadi."
            });
        }
        catch (InvalidOperationException)
        {
            return Conflict(new
            {
                status = "DatabaseConnectionMismatch",
                message =
                    "Uretim veritabani baglantisi dogrulanamadi."
            });
        }
    }
}