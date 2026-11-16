using MasterChefTakip.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace MasterChefTakip.Controllers;

[Authorize(Policy = AppPermissions.ProductionManage)]
[ApiController]
[Route("mc-production-pallet-audit")]
public sealed class MCProductionPalletAuditController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public MCProductionPalletAuditController(
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

        if (string.IsNullOrWhiteSpace(builder.DataSource))
            throw new InvalidOperationException(
                "Veritabani yolu bulunamadi.");

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

    [HttpGet("data")]
    public IActionResult Data()
    {
        using var db = OpenReadOnly();

        var rows = new List<object>();

        long checkedCount = 0;
        long problemCount = 0;
        long validCount = 0;

        using var cmd = db.CreateCommand();

        cmd.CommandText = """
            SELECT
                l.Id,
                l.ReceiptId,
                l.PalletItemId,
                l.AllocatedUnits,
                r.CardId,
                r.CardTable,
                r.ActualKg,
                pi.ProductId,
                pi.PalletId,
                pi.BoxCount,
                pi.LooseUnitCount,
                pi.UnitsPerBox,
                p.DefaultUnitNetWeightGrams,
                COALESCE((
                    SELECT SUM(x.AllocatedUnits)
                    FROM MCProductionPalletLinks x
                    WHERE x.ReceiptId = l.ReceiptId
                ),0),
                (
                    SELECT COUNT(*)
                    FROM StockMovements sm
                    WHERE sm.PalletItemId = l.PalletItemId
                      AND sm.Type = 1
                ),
                COALESCE((
                    SELECT SUM(
                        sm.BoxCount * COALESCE(
                            sm.UnitsPerBox,0
                        ) + sm.LooseUnitCount
                    )
                    FROM StockMovements sm
                    WHERE sm.PalletItemId = l.PalletItemId
                      AND sm.Type = 1
                ),0)
            FROM MCProductionPalletLinks l
            LEFT JOIN MCProductionFinishedGoodsReceipts r
              ON r.Id = l.ReceiptId
            LEFT JOIN PalletItems pi
              ON pi.Id = l.PalletItemId
            LEFT JOIN Products p
              ON p.Id = r.CardId
             AND r.CardTable = 'Products'
            ORDER BY l.Id DESC
            LIMIT 500
            """;

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            checkedCount++;

            long id = reader.GetInt64(0);
            long receiptId = reader.GetInt64(1);
            long palletItemId = reader.GetInt64(2);
            long allocated = reader.GetInt64(3);

            long receiptProductId = reader.IsDBNull(4)
                ? 0 : Convert.ToInt64(reader.GetValue(4));

            string cardTable = reader.IsDBNull(5)
                ? "" : Convert.ToString(reader.GetValue(5)) ?? "";

            decimal actualKg = reader.IsDBNull(6)
                ? 0 : Convert.ToDecimal(
                    reader.GetValue(6),
                    CultureInfo.InvariantCulture);

            long palletProductId = reader.IsDBNull(7)
                ? 0 : Convert.ToInt64(reader.GetValue(7));

            long palletId = reader.IsDBNull(8)
                ? 0 : Convert.ToInt64(reader.GetValue(8));

            long boxes = reader.IsDBNull(9)
                ? 0 : Convert.ToInt64(reader.GetValue(9));

            long loose = reader.IsDBNull(10)
                ? 0 : Convert.ToInt64(reader.GetValue(10));

            long unitsPerBox = reader.IsDBNull(11)
                ? 0 : Convert.ToInt64(reader.GetValue(11));

            decimal grams = reader.IsDBNull(12)
                ? 0 : Convert.ToDecimal(
                    reader.GetValue(12),
                    CultureInfo.InvariantCulture);

            long allAllocated = reader.IsDBNull(13)
                ? 0 : Convert.ToInt64(reader.GetValue(13));

            long movementCount = reader.IsDBNull(14)
                ? 0 : Convert.ToInt64(reader.GetValue(14));

            long movementUnits = reader.IsDBNull(15)
                ? 0 : Convert.ToInt64(reader.GetValue(15));

            var problems = new List<string>();

            if (cardTable != "Products" ||
                receiptProductId <= 0 ||
                receiptProductId != palletProductId)
            {
                problems.Add("Mamul ve palet urun karti uyusmuyor.");
            }

            long palletUnits = -1;

            try
            {
                palletUnits = checked(
                    boxes * unitsPerBox + loose);
            }
            catch (OverflowException)
            {
                problems.Add("Palet miktarinda sayisal tasma.");
            }

            if (palletUnits != allocated)
            {
                problems.Add(
                    "Palet miktari ile mamul tahsisi farkli.");
            }

            if (movementCount != 1)
            {
                problems.Add(
                    "Stok giris hareketi sayisi bir degil.");
            }

            if (movementUnits != allocated)
            {
                problems.Add(
                    "Stok giris miktari ile tahsis farkli.");
            }

            long maximumUnits = 0;

            if (actualKg > 0 && grams > 0)
            {
                decimal calculated = decimal.Floor(
                    actualKg * 1000m / grams);

                if (calculated <= long.MaxValue)
                    maximumUnits = (long)calculated;
            }

            if (maximumUnits <= 0 ||
                allAllocated > maximumUnits)
            {
                problems.Add(
                    "Uretim miktari veya gramaj uyumsuz.");
            }

            bool valid = problems.Count == 0;

            if (valid)
                validCount++;
            else
                problemCount++;

            rows.Add(new
            {
                id,
                receiptId,
                palletId,
                palletItemId,
                receiptProductId,
                palletProductId,
                allocatedUnits = allocated,
                palletUnits,
                stockMovementUnits = movementUnits,
                stockReceiptMovementCount = movementCount,
                maximumProducedUnits = maximumUnits,
                totalReceiptAllocatedUnits = allAllocated,
                valid,
                problems
            });
        }

        return Ok(new
        {
            success = true,
            checkedCount,
            validCount,
            problemCount,
            hasMore = checkedCount == 500,
            rows
        });
    }
}