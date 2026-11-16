using MasterChefTakip.Models;
using System;
using System.Data;
using System.Globalization;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using MasterChefTakip.Services;

namespace MasterChefTakip.Controllers;

public sealed record MCProductionPalletTransferRequest(
    long ReceiptId,
    long PalletId,
    int BoxCount,
    int LooseUnitCount,
    Guid RequestId
);

[Authorize(Policy = AppPermissions.ProductionManage)]
[ApiController]
[Route("mc-production-pallet-transfer")]
public sealed class MCProductionPalletTransferController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public MCProductionPalletTransferController(
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    private SqliteConnection OpenDatabase()
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
                "Fiziksel veritabani yolu bulunamadi.");

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

        if (!System.IO.File.Exists(actual))
            throw new InvalidOperationException(
                "Veritabani dosyasi bulunamadi.");

        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = actual,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false,
                DefaultTimeout = 15
            }.ToString());

        connection.Open();

        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();

        return connection;
    }

    private static object? Scalar(
        SqliteConnection db,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;

        foreach (var parameter in parameters)
            command.Parameters.AddWithValue(
                parameter.Name,
                parameter.Value);

        return command.ExecuteScalar();
    }

    private static int Execute(
        SqliteConnection db,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;

        foreach (var parameter in parameters)
            command.Parameters.AddWithValue(
                parameter.Name,
                parameter.Value);

        return command.ExecuteNonQuery();
    }

    private static long AsLong(object? value)
    {
        return Convert.ToInt64(
            value,
            CultureInfo.InvariantCulture);
    }

    private static decimal AsDecimal(object? value)
    {
        return Convert.ToDecimal(
            value,
            CultureInfo.InvariantCulture);
    }

    // MC_PRODUCTION_CSRF_REQUIRED_V2
    [Microsoft.AspNetCore.Mvc.ValidateAntiForgeryToken]
    [Consumes("application/json")]
    [HttpPost("allocate")]
    public IActionResult Allocate(
        [FromBody] MCProductionPalletTransferRequest request)
    {
        if (request.ReceiptId <= 0 ||
            request.PalletId <= 0 ||
            request.RequestId == Guid.Empty ||
            request.BoxCount < 0 ||
            request.LooseUnitCount < 0)
        {
            return BadRequest(new
            {
                error = "Gecersiz mamul, palet, miktar veya islem kimligi."
            });
        }

        var userId = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        SqliteConnection db;

        try
        {
            db = OpenDatabase();
        }
        catch (InvalidOperationException)
        {
            return Conflict(new
            {
                error = "Guvenli veritabani baglantisi kurulamadi."
            });
        }

        using (db)
        {
            var started = false;

            try
            {
                Execute(db, "BEGIN IMMEDIATE");
                started = true;

                var requestKey =
                    "production:" +
                    request.ReceiptId.ToString(
                        CultureInfo.InvariantCulture) +
                    ":" +
                    request.RequestId.ToString("N");

                // BEGIN IMMEDIATE tum yazar islemlerini siraya alir.
                // Ayni istek anahtari ikinci kez stok olusturamaz.

                var previous = Scalar(
                    db,
                    """
                    SELECT COUNT(*)
                    FROM MCProductionPalletLinks
                    WHERE RequestKey = $key
                    """,
                    ("$key", requestKey));

                if (AsLong(previous) != 0)
                {
                    Execute(db, "ROLLBACK");
                    started = false;

                    return Conflict(new
                    {
                        error = "Bu aktarim istegi daha once islenmis.",
                        requestId = request.RequestId
                    });
                }

                var palletExists = AsLong(Scalar(
                    db,
                    "SELECT COUNT(*) FROM Pallets WHERE Id=$id",
                    ("$id", request.PalletId)));

                if (palletExists != 1)
                {
                    Execute(db, "ROLLBACK");
                    started = false;

                    return NotFound(new
                    {
                        error = "Palet bulunamadi. Once mevcut palet ekranindan palet olusturun."
                    });
                }

                long productId;
                decimal producedKg;
                decimal unitGrams;
                int unitsPerBox;

                using (var command = db.CreateCommand())
                {
                    command.CommandText = """
                        SELECT
                            r.CardId,
                            r.ActualKg,
                            p.DefaultUnitNetWeightGrams,
                            p.DefaultUnitsPerBox
                        FROM MCProductionFinishedGoodsReceipts r
                        JOIN Products p
                          ON p.Id = r.CardId
                        WHERE r.Id = $id
                          AND r.CardTable = 'Products'
                          AND p.IsActive = 1
                        """;

                    command.Parameters.AddWithValue(
                        "$id", request.ReceiptId);

                    using var reader = command.ExecuteReader();

                    if (!reader.Read())
                    {
                        Execute(db, "ROLLBACK");
                        started = false;

                        return Conflict(new
                        {
                            error = "Dogrulanmis ve aktif mamul urun karti bulunamadi."
                        });
                    }

                    productId = AsLong(reader.GetValue(0));
                    producedKg = AsDecimal(reader.GetValue(1));
                    unitGrams = AsDecimal(reader.GetValue(2));
                    unitsPerBox = Convert.ToInt32(
                        reader.GetValue(3),
                        CultureInfo.InvariantCulture);
                }

                if (producedKg <= 0 ||
                    unitGrams <= 0 ||
                    unitsPerBox <= 0 ||
                    request.LooseUnitCount >= unitsPerBox)
                {
                    Execute(db, "ROLLBACK");
                    started = false;

                    return BadRequest(new
                    {
                        error = "Uretim veya ambalaj miktari gecersiz."
                    });
                }

                var packaging =
                    MCProductionPackagingCalculator.Calculate(
                        producedKg,
                        unitGrams,
                        unitsPerBox);

                var allocatedUnits = AsLong(Scalar(
                    db,
                    """
                    SELECT COALESCE(SUM(AllocatedUnits),0)
                    FROM MCProductionPalletLinks
                    WHERE ReceiptId=$id
                    """,
                    ("$id", request.ReceiptId)));

                // MC_PRODUCTION_TRANSFER_QUANTITY_GUARD_V1
                // Uretim siniri asilmadan once kullaniciya
                // anlamli bir HTTP cevabi verilir.
                // BEGIN IMMEDIATE transaction korumasi devam eder.

                long requestedUnits;

                try
                {
                    requestedUnits = checked(
                        (long)request.BoxCount * unitsPerBox
                        + request.LooseUnitCount);
                }
                catch (OverflowException)
                {
                    Execute(db, "ROLLBACK");
                    started = false;

                    return BadRequest(new
                    {
                        error = "Aktarim miktari izin verilen sayisal siniri asiyor."
                    });
                }

                if (requestedUnits <= 0)
                {
                    Execute(db, "ROLLBACK");
                    started = false;

                    return BadRequest(new
                    {
                        error = "Aktarim miktari sifirdan buyuk olmalidir."
                    });
                }

                if (allocatedUnits < 0 ||
                    allocatedUnits > packaging.TotalUnits)
                {
                    Execute(db, "ROLLBACK");
                    started = false;

                    return Conflict(new
                    {
                        error = "Mamul tahsis kayitlari uretim miktariyla uyusmuyor.",
                        maximumProducedUnits = packaging.TotalUnits,
                        allocatedUnits
                    });
                }

                long remainingUnits =
                    packaging.TotalUnits - allocatedUnits;

                if (requestedUnits > remainingUnits)
                {
                    Execute(db, "ROLLBACK");
                    started = false;

                    return Conflict(new
                    {
                        error = "Uretim miktari asildi. Palete aktarim yapilmadi.",
                        maximumProducedUnits = packaging.TotalUnits,
                        previouslyAllocatedUnits = allocatedUnits,
                        requestedUnits,
                        remainingUnits
                    });
                }
                var validation =
                    MCProductionPalletValidation.Validate(
                        producedKg,
                        unitGrams,
                        unitsPerBox,
                        allocatedUnits,
                        request.BoxCount,
                        request.LooseUnitCount,
                        productId,
                        productId,
                        true);

                var now = DateTime.UtcNow.ToString("O");

                // Mevcut PalletItem yapisini kullan.
                // Bu islem sadece mevcut bir palete urun ekler.

                Execute(
                    db,
                    """
                    INSERT INTO PalletItems
                    (
                        PalletId,
                        ProductId,
                        BoxCount,
                        LooseUnitCount,
                        OpenBoxUnitCount,
                        OpenBoxCapacity,
                        UnitsPerBox,
                        ConcurrencyStamp
                    )
                    VALUES
                    (
                        $pallet,
                        $product,
                        $boxes,
                        $loose,
                        0,
                        NULL,
                        $capacity,
                        $stamp
                    )
                    """,
                    ("$pallet", request.PalletId),
                    ("$product", productId),
                    ("$boxes", request.BoxCount),
                    ("$loose", request.LooseUnitCount),
                    ("$capacity", unitsPerBox),
                    ("$stamp", Guid.NewGuid().ToString("N")));

                var palletItemId = AsLong(Scalar(
                    db,
                    "SELECT last_insert_rowid()"));

                // StockMovementType.Receipt = 1.
                // Mevcut mal kabul/ambalajlama kodundaki
                // stok girisi yaklasimi korunur.

                Execute(
                    db,
                    """
                    INSERT INTO StockMovements
                    (
                        PalletItemId,
                        Type,
                        BoxCount,
                        LooseUnitCount,
                        UnitsPerBox,
                        CreatedByUserId,
                        CreatedAtUtc,
                        Notes
                    )
                    VALUES
                    (
                        $item,
                        1,
                        $boxes,
                        $loose,
                        $capacity,
                        $user,
                        $created,
                        $notes
                    )
                    """,
                    ("$item", palletItemId),
                    ("$boxes", request.BoxCount),
                    ("$loose", request.LooseUnitCount),
                    ("$capacity", unitsPerBox),
                    ("$user", userId),
                    ("$created", now),
                    ("$notes",
                        "Uretim mamul girisi: " +
                        request.ReceiptId.ToString(
                            CultureInfo.InvariantCulture)));

                // Veritabanindaki koruma tetikleyicileri:
                // - urun karti eslesmesi
                // - koli bilgisi
                // - toplam uretim miktari siniri
                // - pozitif aktarim miktari
                // yeniden kontrol eder.

                Execute(
                    db,
                    """
                    INSERT INTO MCProductionPalletLinks
                    (
                        ReceiptId,
                        PalletItemId,
                        AllocatedUnits,
                        RequestKey
                    )
                    VALUES
                    (
                        $receipt,
                        $item,
                        $units,
                        $key
                    )
                    """,
                    ("$receipt", request.ReceiptId),
                    ("$item", palletItemId),
                    ("$units", validation.RequestedUnits),
                    ("$key", requestKey));

                Execute(db, "COMMIT");
                started = false;

                return Ok(new
                {
                    success = true,
                    receiptId = request.ReceiptId,
                    palletId = request.PalletId,
                    palletItemId,
                    productId,
                    boxCount = request.BoxCount,
                    looseUnitCount = request.LooseUnitCount,
                    allocatedUnits = validation.RequestedUnits,
                    remainingUnits = validation.RemainingUnits,
                    maximumProducedUnits = packaging.TotalUnits,
                    requestId = request.RequestId,
                    message = "Mamul palete aktarildi ve stok girisi kaydedildi."
                });
            }
            catch (SqliteException)
            {
                if (started)
                {
                    try { Execute(db, "ROLLBACK"); }
                    catch { }
                }

                return Conflict(new
                {
                    error = "Palet aktarimi veritabani kurallari nedeniyle tamamlanamadi. Degisiklikler geri alindi."
                });
            }
            catch (Exception)
            {
                if (started)
                {
                    try { Execute(db, "ROLLBACK"); }
                    catch { }
                }

                return StatusCode(500, new
                {
                    error = "Palet aktarimi tamamlanamadi. Islem geri alindi."
                });
            }
        }
    }
}