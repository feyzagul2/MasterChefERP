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
[Route("mc-production-shipment-eligibility")]
public sealed class MCProductionShipmentEligibilityController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public MCProductionShipmentEligibilityController(
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

    private static long Number(
        SqliteDataReader reader,
        int index)
    {
        return reader.IsDBNull(index)
            ? 0
            : Convert.ToInt64(
                reader.GetValue(index),
                CultureInfo.InvariantCulture);
    }

    private static long? NullableNumber(
        SqliteDataReader reader,
        int index)
    {
        return reader.IsDBNull(index)
            ? null
            : Number(reader, index);
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

        using var cmd = db.CreateCommand();

        cmd.CommandText = """
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

                (
                    SELECT COUNT(*)
                    FROM ShipmentItems si
                    WHERE si.PalletItemId = pi.Id
                ),

                (
                    SELECT COUNT(DISTINCT si.ShipmentId)
                    FROM ShipmentItems si
                    WHERE si.PalletItemId = pi.Id
                ),

                COALESCE((
                    SELECT GROUP_CONCAT(
                        DISTINCT CAST(si.ShipmentId AS TEXT)
                    )
                    FROM ShipmentItems si
                    WHERE si.PalletItemId = pi.Id
                ),''),

                COALESCE((
                    SELECT GROUP_CONCAT(
                        DISTINCT CAST(s.Status AS TEXT)
                    )
                    FROM ShipmentItems si
                    JOIN Shipments s
                      ON s.Id = si.ShipmentId
                    WHERE si.PalletItemId = pi.Id
                ),''),

                (
                    SELECT COUNT(*)
                    FROM ShipmentItems si
                    WHERE si.PalletItemId = pi.Id
                      AND si.HasStockSnapshot = 1
                ),

                (
                    SELECT COUNT(*)
                    FROM ShipmentItems si
                    WHERE si.PalletItemId = pi.Id
                      AND (
                          si.HasStockSnapshot = 0
                          OR si.AfterStockStamp IS NULL
                      )
                ),

                (
                    SELECT COUNT(*)
                    FROM StockMovements sm
                    WHERE sm.PalletItemId = pi.Id
                      AND sm.Type IN (3,4,5,6)
                )

            FROM PalletItems pi
            LEFT JOIN Products p
              ON p.Id = pi.ProductId

            WHERE ($palletId IS NULL OR pi.PalletId = $palletId)
              AND ($productId IS NULL OR pi.ProductId = $productId)

            ORDER BY pi.PalletId DESC, pi.Id DESC
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

        cmd.Parameters.AddWithValue("$limit", limit + 1);

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            long palletItemId = Number(reader, 0);
            long currentPalletId = Number(reader, 1);
            long currentProductId = Number(reader, 2);

            string productName = reader.IsDBNull(3)
                ? ""
                : reader.GetString(3);

            long boxes = Number(reader, 4);
            long loose = Number(reader, 5);
            long? unitsPerBox = NullableNumber(reader, 6);
            long? openCapacity = NullableNumber(reader, 7);
            long openUnits = Number(reader, 8);

            long shipmentItemCount = Number(reader, 9);
            long shipmentCount = Number(reader, 10);

            string shipmentIds = reader.IsDBNull(11)
                ? ""
                : reader.GetString(11);

            string shipmentStatusCodes = reader.IsDBNull(12)
                ? ""
                : reader.GetString(12);

            long snapshotCount = Number(reader, 13);
            long incompleteSnapshotCount = Number(reader, 14);
            long adjustmentMovementCount = Number(reader, 15);

            var warnings = new List<string>();

            if (currentPalletId <= 0 || currentProductId <= 0)
                warnings.Add("Palet veya urun kimligi gecersiz.");

            if (boxes < 0 || loose < 0 || openUnits < 0)
                warnings.Add("Negatif stok miktari.");

            if (!unitsPerBox.HasValue || unitsPerBox.Value <= 0)
                warnings.Add("Koli ici adet bilgisi gecersiz.");

            if (openCapacity.HasValue && openCapacity.Value < 0)
                warnings.Add("Acik koli kapasitesi gecersiz.");

            if (openUnits > 0 &&
                (!openCapacity.HasValue || openCapacity.Value <= 0))
            {
                warnings.Add("Acik koli kapasitesi eksik.");
            }

            if (openCapacity.HasValue &&
                openCapacity.Value > 0 &&
                openUnits > openCapacity.Value)
            {
                warnings.Add("Acik koli kapasitesi asildi.");
            }

            long? recordedUnits = null;

            if (warnings.Count == 0)
            {
                try
                {
                    recordedUnits = checked(
                        checked(boxes * unitsPerBox!.Value)
                        + checked(loose + openUnits));
                }
                catch (OverflowException)
                {
                    warnings.Add("Stok miktarinda sayisal tasma.");
                }
            }

            if (shipmentItemCount > 0)
            {
                warnings.Add(
                    "Bu palet kaleminin sevkiyat gecmisi var. " +
                    "Sevkiyat durumlari kontrol edilmeli.");
            }

            if (incompleteSnapshotCount > 0)
            {
                warnings.Add(
                    "Eksik sevkiyat stok goruntusu mevcut.");
            }

            if (adjustmentMovementCount > 0)
            {
                warnings.Add(
                    "Iade, hasar veya iptal hareketi mevcut.");
            }

            string status;

            if (warnings.Count > 0)
            {
                status = "INCELEME_GEREKLI";
            }
            else if (!recordedUnits.HasValue ||
                     recordedUnits.Value == 0)
            {
                status = "STOK_YOK";
            }
            else
            {
                status = "ON_KONTROLDEN_GECTI";
            }

            rows.Add(new
            {
                palletItemId,
                palletId = currentPalletId,
                productId = currentProductId,
                productName,

                fullBoxes = boxes,
                unitsPerBox,
                looseUnits = loose,
                openBoxCapacity = openCapacity,
                openBoxUnits = openUnits,

                currentRecordedUnits = recordedUnits,

                shipmentItemCount,
                shipmentCount,
                shipmentIds,
                shipmentStatusCodes,

                snapshotCount,
                incompleteSnapshotCount,
                adjustmentMovementCount,

                status,
                warnings,

                shipmentApproved = false,
                automaticShipmentAllowed = false
            });
        }

        bool hasMore = rows.Count > limit;

        if (hasMore)
            rows.RemoveAt(rows.Count - 1);


        Response.Headers.CacheControl = "no-store";

        return Ok(new
        {
            success = true,
            checkedCount = rows.Count,
            hasMore,

            shipmentApprovalImplemented = false,
            automaticShipmentAllowed = false,

            note =
                "Bu API yalnizca sevkiyat on kontrolu yapar. " +
                "Mevcut sevkiyat onay ve stok dusme islemlerini " +
                "degistirmez. Sevkiyat durum kodlari ve rezervasyon " +
                "kurallari dogrulanmadan sevkiyat izni vermez.",

            rows
        });
    }
}