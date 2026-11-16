
using System.Security.Claims;
using MasterChefTakip.Models;
using MasterChefTakip.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

[Authorize(Roles = AppRoles.Admin)]
public class ProductionPackagingController : Controller
{
    private readonly ApplicationDbContext _db;

    public ProductionPackagingController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var batches = await _db.Set<PackagingBatch>()
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(200)
            .ToListAsync();

        var linkedIds = await _db
            .Set<ProductionPackagingLink>()
            .AsNoTracking()
            .Select(x => x.PackagingBatchId)
            .ToListAsync();

        var linkedSet = linkedIds.ToHashSet();

        var options = new List<PackagingBatchOption>();

        foreach (var batch in batches)
        {
            if (linkedSet.Contains(batch.Id))
                continue;

            var productIds = await _db.Set<StockMovement>()
                .AsNoTracking()
                .Where(x =>
                    x.PackagingBatchId == batch.Id)
                .Select(x => x.PalletItem.ProductId)
                .Distinct()
                .ToListAsync();

            if (productIds.Count != 1)
                continue;

            var hasNonReceipt = await _db
                .Set<StockMovement>()
                .AnyAsync(x =>
                    x.PackagingBatchId == batch.Id &&
                    x.Type != StockMovementType.Receipt);

            if (hasNonReceipt)
                continue;

            options.Add(new PackagingBatchOption
            {
                Id = batch.Id,
                ProductId = productIds[0],
                Date = batch.CreatedAtUtc
            });
        }

        var orders = await _db.Set<ProductionOrder>()
            .AsNoTracking()
            .Include(x => x.Recipe)
            .Where(x =>
                x.Status == "Completed" &&
                x.Recipe.ProductId != null)
            .OrderByDescending(x => x.Id)
            .Take(200)
            .ToListAsync();

        var lots = await _db.Set<ProductLot>()
            .AsNoTracking()
            .Include(x => x.Product)
            .Where(x => x.Status == "Approved")
            .OrderByDescending(x => x.Id)
            .Take(200)
            .ToListAsync();

        var links = await _db
            .Set<ProductionPackagingLink>()
            .AsNoTracking()
            .Include(x => x.ProductionOrder)
            .Include(x => x.ProductLot)
            .Include(x => x.ProductLot.Product)
            .OrderByDescending(x => x.Id)
            .Take(200)
            .ToListAsync();

        return View(new ProductionPackagingPage
        {
            Batches = options,
            Orders = orders,
            Lots = lots,
            Links = links
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Connect(
        string packagingBatchId,
        int productionOrderId,
        int productLotId)
    {
        var userId = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(packagingBatchId) ||
            packagingBatchId.Length != 32 ||
            productionOrderId <= 0 ||
            productLotId <= 0)
        {
            return BadRequest("Baglanti bilgileri gecersiz.");
        }

        await using var transaction =
            await _db.Database.BeginTransactionAsync();

        var batch = await _db.Set<PackagingBatch>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Id == packagingBatchId);

        if (batch == null)
            return NotFound("Paketleme kaydi bulunamadi.");

        var exists = await _db
            .Set<ProductionPackagingLink>()
            .AnyAsync(x =>
                x.PackagingBatchId == batch.Id);

        if (exists)
            return Conflict(
                "Bu paketleme kaydi zaten bir uretime bagli.");

        var order = await _db.Set<ProductionOrder>()
            .AsNoTracking()
            .Include(x => x.Recipe)
            .FirstOrDefaultAsync(x =>
                x.Id == productionOrderId &&
                x.Status == "Completed");

        if (order == null)
            return BadRequest(
                "Tamamlanmis uretim emri seciniz.");

        if (!order.Recipe.ProductId.HasValue)
            return BadRequest(
                "Uretim recetesinde mamul tanimli degil.");

        var lot = await _db.Set<ProductLot>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Id == productLotId &&
                x.Status == "Approved");

        if (lot == null)
            return BadRequest(
                "Kalite onayli lot seciniz.");

        if (lot.ExpiryDate.Date < DateTime.Today)
            return BadRequest(
                "SKT gecmis lot baglanamaz.");

        if (lot.ProductionDate.Date > DateTime.Today)
            return BadRequest(
                "Uretim tarihi gelecekte olan lot baglanamaz.");

        if (lot.ProductId != order.Recipe.ProductId.Value)
            return BadRequest(
                "Lot urunu ile uretim emri mamulu farkli.");

        var movements = await _db.Set<StockMovement>()
            .AsNoTracking()
            .Where(x => x.PackagingBatchId == batch.Id)
            .Select(x => new
            {
                x.Type,
                x.PalletItem.ProductId
            })
            .ToListAsync();

        if (movements.Count == 0)
            return BadRequest(
                "Paketleme kaydinda stok hareketi yok.");

        if (movements.Any(x =>
            x.Type != StockMovementType.Receipt))
        {
            return BadRequest(
                "Paketleme kaydinda beklenmeyen stok hareketi var.");
        }

        if (movements.Any(x =>
            x.ProductId != lot.ProductId))
        {
            return BadRequest(
                "Paketlenen urun ile lot urunu farkli.");
        }

        
        // MC_PRODUCTION_PACKAGING_QUANTITY_GUARD_2026

        var packagingProduct = await _db.Set<Product>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == lot.ProductId);

        if (packagingProduct == null)
        {
            return BadRequest("Paketlenen urun bulunamadi.");
        }

        decimal? unitWeightGrams =
            packagingProduct.DefaultUnitNetWeightGrams;

        if (!unitWeightGrams.HasValue ||
            unitWeightGrams.Value <= 0)
        {
            return BadRequest(
                "Urun kartinda birim net agirlik (gram) " +
                "tanimlanmadan paketleme uretime baglanamaz.");
        }

        var currentMovements = await _db
            .Set<StockMovement>()
            .AsNoTracking()
            .Where(x =>
                x.PackagingBatchId == packagingBatchId &&
                x.Type == StockMovementType.Receipt)
            .Select(x => new
            {
                x.BoxCount,
                x.LooseUnitCount,
                x.UnitsPerBox
            })
            .ToListAsync();

        decimal currentUnits = 0m;

        foreach (var movement in currentMovements)
        {
            if (movement.BoxCount < 0 ||
                movement.LooseUnitCount < 0 ||
                (movement.BoxCount > 0 &&
                 (!movement.UnitsPerBox.HasValue ||
                  movement.UnitsPerBox.Value <= 0)))
            {
                return BadRequest(
                    "Paketleme koli veya adet bilgileri gecersiz.");
            }

            currentUnits +=
                (decimal)movement.BoxCount *
                (movement.UnitsPerBox ?? 0) +
                movement.LooseUnitCount;
        }

        if (currentUnits <= 0)
        {
            return BadRequest(
                "Paketleme kaydinda gecerli miktar bulunamadi.");
        }

        var previousBatchIds = await _db
            .Set<ProductionPackagingLink>()
            .AsNoTracking()
            .Where(x =>
                x.ProductionOrderId == productionOrderId)
            .Select(x => x.PackagingBatchId)
            .ToListAsync();

        var previousMovements = await _db
            .Set<StockMovement>()
            .AsNoTracking()
            .Where(x =>
                x.PackagingBatchId != null &&
                previousBatchIds.Contains(x.PackagingBatchId) &&
                x.Type == StockMovementType.Receipt)
            .Select(x => new
            {
                x.BoxCount,
                x.LooseUnitCount,
                x.UnitsPerBox
            })
            .ToListAsync();

        decimal previousUnits = 0m;

        foreach (var movement in previousMovements)
        {
            if (movement.BoxCount < 0 ||
                movement.LooseUnitCount < 0 ||
                (movement.BoxCount > 0 &&
                 (!movement.UnitsPerBox.HasValue ||
                  movement.UnitsPerBox.Value <= 0)))
            {
                return BadRequest(
                    "Onceki paketleme miktarlari gecersiz.");
            }

            previousUnits +=
                (decimal)movement.BoxCount *
                (movement.UnitsPerBox ?? 0) +
                movement.LooseUnitCount;
        }

        decimal totalUnits = previousUnits + currentUnits;

        decimal totalKg =
            totalUnits * unitWeightGrams.Value / 1000m;

        if (totalKg > order.PlannedQuantityKg)
        {
            return BadRequest(
                "Paketleme miktari uretim emrini asiyor. " +
                "Planlanan: " +
                order.PlannedQuantityKg.ToString("N3") +
                " Kg. Paketlenen: " +
                totalKg.ToString("N3") + " Kg.");
        }

                // MC_FINISHED_GOODS_REAL_STOCK_GUARD_V1
        // Mamul stogu burada yeniden olusturulmaz.
        // PackagingController tarafinda kaydedilmis
        // gercek stok girisi dogrulanir.

        if (order.Status != "Completed")
        {
            return BadRequest(
                "Uretim emri tamamlanmadan mamul stok baglantisi kurulamaz.");
        }

        var mcExistingPackagingLink =
            await _db.Set<ProductionPackagingLink>()
                .AsNoTracking()
                .AnyAsync(x => x.PackagingBatchId == batch.Id);

        if (mcExistingPackagingLink)
        {
            return Conflict(
                "Bu paketleme partisi zaten bir uretim emrine baglanmis. " +
                "Ikinci baglanti olusturulmadi.");
        }

        var mcReceiptMovements =
            await _db.Set<StockMovement>()
                .AsNoTracking()
                .Where(x =>
                    x.PackagingBatchId == batch.Id &&
                    x.Type == StockMovementType.Receipt)
                .Include(x => x.PalletItem)
                .ToListAsync();

        if (mcReceiptMovements.Count == 0)
        {
            return BadRequest(
                "Bu paketleme partisinin gercek mamul stok girisi bulunamadi. " +
                "Once mevcut paketleme ekranindan stok girisi yapilmalidir.");
        }

        if (mcReceiptMovements.Any(x =>
                x.BoxCount < 0 ||
                x.LooseUnitCount < 0 ||
                (x.BoxCount == 0 && x.LooseUnitCount == 0)))
        {
            return BadRequest(
                "Paketleme partisinde gecersiz mamul stok girisi bulundu. " +
                "Uretim-lot baglantisi olusturulmadi.");
        }
                // MC_FINISHED_GOODS_PRODUCT_MATCH_V1
        // Gercek stok girisindeki urun ile uretim recetesindeki
        // urun ayni olmadan lot baglantisi olusturulamaz.

        var mcExpectedProductId = order.Recipe.ProductId;

        if (mcReceiptMovements.Any(x =>
                x.PalletItem == null ||
                x.PalletItem.ProductId != mcExpectedProductId))
        {
            return BadRequest(
                "Mamul stok girisindeki urun ile uretim emrindeki " +
                "urun ayni degil. Lot baglantisi olusturulmadi.");
        }

        if (mcReceiptMovements.Any(x =>
                x.UnitsPerBox <= 0 ||
                ((decimal)x.BoxCount * x.UnitsPerBox +
                 x.LooseUnitCount) <= 0))
        {
            return BadRequest(
                "Mamul stok girisinde gecersiz koli veya adet " +
                "bilgisi bulunuyor. Lot baglantisi olusturulmadi.");
        }
        var link = new ProductionPackagingLink
        {
            PackagingBatchId = batch.Id,
            ProductionOrderId = order.Id,
            ProductLotId = lot.Id,
            CreatedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Set<ProductionPackagingLink>().Add(link);

        try
        {
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();

            return Conflict(
                "Baglanti kaydedilemedi. " +
                "Paketleme zaten baglanmis olabilir.");
        }

        TempData["PackagingLinkSuccess"] =
            "Uretim, paketleme ve lot baglantisi kaydedildi.";

        return RedirectToAction(nameof(Index));
    }
}

public class PackagingBatchOption
{
    public string Id { get; set; } = "";
    public int ProductId { get; set; }
    public DateTime Date { get; set; }
}

public class ProductionPackagingPage
{
    public List<PackagingBatchOption> Batches
        { get; set; } = new();

    public List<ProductionOrder> Orders
        { get; set; } = new();

    public List<ProductLot> Lots
        { get; set; } = new();

    public List<ProductionPackagingLink> Links
        { get; set; } = new();
}
