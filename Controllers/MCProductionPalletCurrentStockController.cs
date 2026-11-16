using MasterChefTakip.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace MasterChefTakip.Controllers;

[Authorize(Policy = AppPermissions.ProductionManage)]
[ApiController]
[Route("mc-production-pallet-current-stock")]
public sealed class MCProductionPalletCurrentStockController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public MCProductionPalletCurrentStockController(
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
                "Veritabani baglantisi bulunamadi.");

        var builder = new SqliteConnectionStringBuilder(configured);

        if (string.IsNullOrWhiteSpace(builder.DataSource) ||
            builder.DataSource == ":memory:")
            throw new InvalidOperationException(
                "Gecersiz veritabani yolu.");

        var actual = System.IO.Path.GetFullPath(
            System.IO.Path.IsPathRooted(builder.DataSource)
                ? builder.DataSource
                : System.IO.Path.Combine(
                    _environment.ContentRootPath,
                    builder.DataSource));

        var expected = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(
                _environment.ContentRootPath,
                "app.db"));

        if (!string.Equals(
            actual,
            expected,
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Beklenmeyen veritabani baglantisi.");

        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = expected,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());

        connection.Open();
        return connection;
    }

    private static long ReadLong(
        SqliteDataReader reader,
        int index)
    {
        return Convert.ToInt64(
            reader.GetValue(index),
            CultureInfo.InvariantCulture);
    }

    private static long? ReadNullableLong(
        SqliteDataReader reader,
        int index)
    {
        return reader.IsDBNull(index)
            ? null
            : ReadLong(reader, index);
    }

    [HttpGet("data")]
    public IActionResult Data(
        [FromQuery] long? palletId = null,
        [FromQuery] long? productId = null,
        [FromQuery] int limit = 500)
    {
        if (palletId.HasValue && palletId.Value <= 0)
            return BadRequest(new
            {
                error = "Gecersiz palet numarasi."
            });

        if (productId.HasValue && productId.Value <= 0)
            return BadRequest(new
            {
                error = "Gecersiz urun numarasi."
            });

        if (limit < 1 || limit > 1000)
            return BadRequest(new
            {
                error = "Kayit siniri 1-1000 arasinda olmalidir."
            });

        using var db = OpenReadOnly();

        var rows = new List<object>();

        long checkedCount = 0;
        long validCount = 0;
        long problemCount = 0;

        long totalUnits = 0;
        bool totalReliable = true;

        using var command = db.CreateCommand();

        command.CommandText = """
            SELECT
                pi.Id,
                pi.PalletId,
                pi.ProductId,
                COALESCE(p.Name,''),
                pi.BoxCount,
                pi.LooseUnitCount,
                pi.UnitsPerBox,
                pi.OpenBoxCapacity,
                pi.OpenBoxUnitCount,
                COALESCE((
                    SELECT COUNT(*)
                    FROM StockMovements sm
                    WHERE sm.PalletItemId = pi.Id
                ),0),
                COALESCE((
                    SELECT COUNT(*)
                    FROM MCProductionPalletLinks l
                    WHERE l.PalletItemId = pi.Id
                ),0)
            FROM PalletItems pi
            LEFT JOIN Products p
              ON p.Id = pi.ProductId
            WHERE ($palletId IS NULL OR pi.PalletId=$palletId)
              AND ($productId IS NULL OR pi.ProductId=$productId)
            ORDER BY pi.PalletId DESC, pi.Id DESC
            LIMIT $limit
            """;

        command.Parameters.AddWithValue(
            "$palletId",
            palletId.HasValue
                ? (object)palletId.Value
                : DBNull.Value);

        command.Parameters.AddWithValue(
            "$productId",
            productId.HasValue
                ? (object)productId.Value
                : DBNull.Value);

        command.Parameters.AddWithValue(
            "$limit",
            limit + 1);

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var itemId = ReadLong(reader, 0);
            var currentPalletId = ReadLong(reader, 1);
            var currentProductId = ReadLong(reader, 2);

            var productName = reader.IsDBNull(3)
                ? ""
                : reader.GetString(3);

            var boxes = ReadLong(reader, 4);
            var loose = ReadLong(reader, 5);
            var unitsPerBox = ReadNullableLong(reader, 6);
            var openCapacity = ReadNullableLong(reader, 7);
            var openUnits = ReadLong(reader, 8);

            var movementCount = ReadLong(reader, 9);
            var productionLinkCount = ReadLong(reader, 10);

            var problems = new List<string>();

            if (boxes < 0 || loose < 0 || openUnits < 0)
                problems.Add("Negatif stok miktari.");

            if (boxes > 0 &&
                (!unitsPerBox.HasValue || unitsPerBox.Value <= 0))
            {
                problems.Add(
                    "Tam koli icin koli kapasitesi eksik.");
            }

            if (openUnits > 0 &&
                (!openCapacity.HasValue || openCapacity.Value <= 0))
            {
                problems.Add(
                    "Acik koli kapasitesi eksik.");
            }

            if (openCapacity.HasValue && openCapacity.Value < 0)
                problems.Add("Gecersiz acik koli kapasitesi.");

            if (openCapacity.HasValue &&
                openCapacity.Value > 0 &&
                openUnits > openCapacity.Value)
            {
                problems.Add(
                    "Acik koli adedi kapasiteyi asiyor.");
            }

            if (unitsPerBox.HasValue && unitsPerBox.Value < 0)
                problems.Add("Gecersiz koli ici adet.");

            long? currentUnits = null;

            if (problems.Count == 0)
            {
                try
                {
                    currentUnits = checked(
                        checked(
                            boxes * unitsPerBox.GetValueOrDefault()
                        ) + checked(loose + openUnits));
                }
                catch (OverflowException)
                {
                    problems.Add(
                        "Stok miktarinda sayisal tasma.");
                }
            }

            bool valid = problems.Count == 0;

            if (valid)
            {
                validCount++;

                try
                {
                    totalUnits = checked(
                        totalUnits + currentUnits.GetValueOrDefault());
                }
                catch (OverflowException)
                {
                    totalReliable = false;
                }
            }
            else
            {
                problemCount++;
                totalReliable = false;
            }

            checkedCount++;

            rows.Add(new
            {
                palletItemId = itemId,
                palletId = currentPalletId,
                productId = currentProductId,
                productName,
                fullBoxes = boxes,
                unitsPerBox,
                looseUnits = loose,
                openBoxCapacity = openCapacity,
                openBoxUnits = openUnits,
                currentRecordedUnits = currentUnits,
                stockMovementCount = movementCount,
                productionLinkCount,
                valid,
                problems
            });
        }

        bool hasMore = rows.Count > limit;

        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
            totalReliable = false;
        }

        Response.Headers.CacheControl = "no-store";

        return Ok(new
        {
            success = true,
            checkedCount = rows.Count,
            validCount,
            problemCount,
            hasMore,
            totalRecordedUnits = totalReliable
                ? (long?)totalUnits
                : null,
            totalReliable,
            shipmentAvailabilityVerified = false,
            stockSource = "PalletItems",
            note =
                "Mevcut palet kalemlerinin kayitli miktarlari " +
                "gosterilir. Stok hareketleri ikinci kez dusulmez. " +
                "Bu sonuclar sevke uygun stok onayi degildir.",
            rows
        });
    }
}