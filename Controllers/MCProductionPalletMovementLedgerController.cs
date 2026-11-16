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
[Route("mc-production-pallet-ledger")]
public sealed class MCProductionPalletMovementLedgerController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public MCProductionPalletMovementLedgerController(
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
        {
            throw new InvalidOperationException(
                "Beklenmeyen veritabani baglantisi.");
        }

        var db = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = expected,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());

        db.Open();

        return db;
    }

    private static long GetLong(
        SqliteDataReader reader,
        int index)
    {
        return Convert.ToInt64(
            reader.GetValue(index),
            CultureInfo.InvariantCulture);
    }

    private static long? GetNullableLong(
        SqliteDataReader reader,
        int index)
    {
        return reader.IsDBNull(index)
            ? null
            : GetLong(reader, index);
    }

    private static string GetText(
        SqliteDataReader reader,
        int index)
    {
        return reader.IsDBNull(index)
            ? ""
            : Convert.ToString(
                reader.GetValue(index),
                CultureInfo.InvariantCulture) ?? "";
    }

    private static string MovementName(long type)
    {
        return type switch
        {
            1 => "Stok Girisi",
            2 => "Sevkiyat",
            3 => "Iade",
            4 => "Hasar",
            5 => "Mal Kabul Iptali",
            6 => "Sevkiyat Iptali",
            _ => "Bilinmeyen Hareket"
        };
    }

    [HttpGet("data")]
    public IActionResult Data(
        [FromQuery] long? palletId = null,
        [FromQuery] long? productId = null,
        [FromQuery] int limit = 200)
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

        if (limit < 1 || limit > 500)
            return BadRequest(new
            {
                error = "Kayit siniri 1 ile 500 arasinda olmalidir."
            });

        using var db = OpenReadOnly();

        var rows = new List<object>();

        using var cmd = db.CreateCommand();

        cmd.CommandText = """
            SELECT
                sm.Id,
                sm.PalletItemId,
                pi.PalletId,
                pi.ProductId,
                COALESCE(p.Name,''),
                sm.Type,
                sm.BoxCount,
                sm.LooseUnitCount,
                sm.UnitsPerBox,
                sm.CreatedAtUtc,
                sm.CreatedByUserId,
                sm.Notes,
                sm.PackagingBatchId,
                sm.RelatedShipmentId,
                sm.ReversesMovementId,
                sm.CorrectsMovementId,
                COALESCE((
                    SELECT COUNT(*)
                    FROM StockMovements x
                    WHERE x.ReversesMovementId = sm.Id
                ),0),
                COALESCE((
                    SELECT COUNT(*)
                    FROM StockMovements x
                    WHERE x.CorrectsMovementId = sm.Id
                ),0)
            FROM StockMovements sm
            JOIN PalletItems pi
              ON pi.Id = sm.PalletItemId
            LEFT JOIN Products p
              ON p.Id = pi.ProductId
            WHERE ($palletId IS NULL OR pi.PalletId = $palletId)
              AND ($productId IS NULL OR pi.ProductId = $productId)
            ORDER BY sm.Id DESC
            LIMIT $limit
            """;

        cmd.Parameters.AddWithValue(
            "$palletId",
            palletId.HasValue
                ? (object)palletId.Value
                : DBNull.Value);

        cmd.Parameters.AddWithValue(
            "$productId",
            productId.HasValue
                ? (object)productId.Value
                : DBNull.Value);

        cmd.Parameters.AddWithValue(
            "$limit",
            limit + 1);

        long receiptCount = 0;
        long shipmentCount = 0;
        long returnCount = 0;
        long damageCount = 0;
        long cancellationCount = 0;
        long unknownTypeCount = 0;

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            long id = GetLong(reader, 0);
            long palletItemId = GetLong(reader, 1);
            long currentPalletId = GetLong(reader, 2);
            long currentProductId = GetLong(reader, 3);

            string productName = GetText(reader, 4);

            long type = GetLong(reader, 5);
            long boxes = GetLong(reader, 6);
            long loose = GetLong(reader, 7);

            long? unitsPerBox = GetNullableLong(reader, 8);

            string createdAtUtc = GetText(reader, 9);
            string createdByUserId = GetText(reader, 10);
            string notes = GetText(reader, 11);
            string packagingBatchId = GetText(reader, 12);

            long? shipmentId = GetNullableLong(reader, 13);
            long? reversesMovementId = GetNullableLong(reader, 14);
            long? correctsMovementId = GetNullableLong(reader, 15);

            long reversalCount = GetLong(reader, 16);
            long correctionCount = GetLong(reader, 17);

            long? movementUnits = null;
            bool quantityValid = false;

            try
            {
                if (boxes >= 0 &&
                    loose >= 0 &&
                    unitsPerBox.HasValue &&
                    unitsPerBox.Value > 0)
                {
                    movementUnits = checked(
                        boxes * unitsPerBox.Value + loose);

                    quantityValid = true;
                }
            }
            catch (OverflowException)
            {
                quantityValid = false;
            }

            switch (type)
            {
                case 1:
                    receiptCount++;
                    break;

                case 2:
                    shipmentCount++;
                    break;

                case 3:
                    returnCount++;
                    break;

                case 4:
                    damageCount++;
                    break;

                case 5:
                case 6:
                    cancellationCount++;
                    break;

                default:
                    unknownTypeCount++;
                    break;
            }

            rows.Add(new
            {
                movementId = id,
                palletItemId,
                palletId = currentPalletId,
                productId = currentProductId,
                productName,
                movementType = type,
                movementTypeName = MovementName(type),
                boxCount = boxes,
                looseUnitCount = loose,
                unitsPerBox,
                movementUnits,
                quantityValid,
                createdAtUtc,
                createdByUserId,
                notes,
                packagingBatchId,
                relatedShipmentId = shipmentId,
                reversesMovementId,
                correctsMovementId,
                reversalCount,
                correctionCount,
                hasReversal = reversalCount > 0,
                hasCorrection = correctionCount > 0
            });
        }

        bool hasMore = rows.Count > limit;

        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        Response.Headers.CacheControl = "no-store";

        return Ok(new
        {
            success = true,
            listedCount = rows.Count,
            hasMore,
            summary = new
            {
                receiptCount,
                shipmentCount,
                returnCount,
                damageCount,
                cancellationCount,
                unknownTypeCount
            },
            netStockCalculated = false,
            note =
                "Hareketler salt okunur listelenir. " +
                "Sevkiyat, iade ve iptal kurallari dogrulanmadan " +
                "net stok hesaplanmaz.",
            rows
        });
    }
}