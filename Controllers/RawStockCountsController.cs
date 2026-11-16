
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using MasterChefTakip.Models;
using MasterChefTakip.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

[Authorize(Roles = AppRoles.Admin)]
public class RawStockCountsController : Controller
{
    private readonly ApplicationDbContext _db;

    public RawStockCountsController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        await LoadLists();

        var counts = await _db.Set<RawStockCount>()
            .AsNoTracking()
            .Include(x => x.RawMaterial)
            .Include(x => x.Warehouse)
            .OrderByDescending(x => x.Id)
            .Take(300)
            .ToListAsync();


        ViewBag.Approvals = await _db
            .Set<RawStockCountApproval>()
            .AsNoTracking()
            .ToDictionaryAsync(
                x => x.RawStockCountId,
                x => x.Decision);

        ViewBag.Counts = counts;

        return View(new RawStockCountInput());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(RawStockCountInput input)
    {
        if (input.CountedQuantity < 0 ||
            decimal.Round(input.CountedQuantity, 4)
                != input.CountedQuantity)
        {
            ModelState.AddModelError(
                "",
                "Fiziksel sayim miktari negatif olamaz. " +
                "En fazla 4 ondalik kullaniniz.");
        }

        if ((input.Notes?.Length ?? 0) > 500)
            ModelState.AddModelError(
                "",
                "Aciklama 500 karakteri asamaz.");

        if (!ModelState.IsValid)
            return await Invalid(input);

        var userId = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        await using var transaction =
            await _db.Database.BeginTransactionAsync();

        var material = await _db.Set<RawMaterial>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Id == input.RawMaterialId &&
                x.IsActive);

        var warehouse = await _db.Set<RawMaterialWarehouse>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Id == input.WarehouseId &&
                x.IsActive);

        if (material == null || warehouse == null)
        {
            ModelState.AddModelError(
                "", "Aktif hammadde ve depo seciniz.");

            return await Invalid(input);
        }

        var movements = await _db
            .Set<RawMaterialStockMovement>()
            .AsNoTracking()
            .Where(x =>
                x.RawMaterialId == material.Id &&
                x.WarehouseId == warehouse.Id)
            .Select(x => new
            {
                x.MovementType,
                x.Quantity
            })
            .ToListAsync();

        var systemQuantity = movements.Sum(x =>
            x.MovementType == "IN"
                ? x.Quantity
                : -x.Quantity);

        var difference =
            input.CountedQuantity - systemQuantity;

        _db.Set<RawStockCount>().Add(new RawStockCount
        {
            CountNumber = "SAY-" +
                Guid.NewGuid().ToString("N")[..16]
                    .ToUpperInvariant(),

            RawMaterialId = material.Id,
            WarehouseId = warehouse.Id,

            SystemQuantity = systemQuantity,
            CountedQuantity = input.CountedQuantity,
            Difference = difference,

            Unit = material.Unit,
            Notes = input.Notes?.Trim(),
            CreatedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        TempData["CountSuccess"] =
            "Stok sayimi kaydedildi. " +
            "Fark: " + difference.ToString("N4") +
            " " + material.Unit;

        return RedirectToAction(nameof(Index));
    }


    // MC_STOCK_COUNT_APPROVAL_2026
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Decide(
        int countId,
        string decision,
        string? notes)
    {
        if (countId <= 0)
            return BadRequest("Gecersiz sayim.");

        if (decision != "Approve" && decision != "Reject")
            return BadRequest("Gecersiz karar.");

        if ((notes?.Length ?? 0) > 500)
            return BadRequest("Aciklama cok uzun.");

        var userId = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        await using var transaction =
            await _db.Database.BeginTransactionAsync();

        var count = await _db.Set<RawStockCount>()
            .Include(x => x.RawMaterial)
            .Include(x => x.Warehouse)
            .FirstOrDefaultAsync(x => x.Id == countId);

        if (count == null)
            return NotFound();

        var alreadyDecided = await _db
            .Set<RawStockCountApproval>()
            .AnyAsync(x => x.RawStockCountId == count.Id);

        if (alreadyDecided)
        {
            TempData["CountError"] =
                "Bu sayim icin daha once karar verilmis.";

            return RedirectToAction(nameof(Index));
        }

        var approval = new RawStockCountApproval
        {
            RawStockCountId = count.Id,
            Decision = decision == "Approve"
                ? "Approved"
                : "Rejected",
            ApprovedByUserId = userId,
            ApprovedAtUtc = DateTime.UtcNow,
            Notes = notes?.Trim()
        };

        if (decision == "Approve")
        {
            if (!count.RawMaterial.IsActive ||
                !count.Warehouse.IsActive)
            {
                TempData["CountError"] =
                    "Pasif hammadde veya depo icin onay verilemez.";

                return RedirectToAction(nameof(Index));
            }

            var history = await _db
                .Set<RawMaterialStockMovement>()
                .AsNoTracking()
                .Where(x =>
                    x.RawMaterialId == count.RawMaterialId &&
                    x.WarehouseId == count.WarehouseId)
                .Select(x => new
                {
                    x.MovementType,
                    x.Quantity
                })
                .ToListAsync();

            var currentStock = history.Sum(x =>
                x.MovementType == "IN"
                    ? x.Quantity
                    : -x.Quantity);

            if (currentStock != count.SystemQuantity)
            {
                TempData["CountError"] =
                    "Sayimdan sonra sistem stogu degismis. " +
                    "Eski sayim onaylanamaz. Yeni sayim yapiniz.";

                return RedirectToAction(nameof(Index));
            }

            if (count.CountedQuantity < 0 ||
                count.Difference !=
                    count.CountedQuantity - count.SystemQuantity)
            {
                return BadRequest("Sayim verileri tutarsiz.");
            }

            if (count.Difference < 0 &&
                currentStock < -count.Difference)
            {
                TempData["CountError"] =
                    "Duzeltme icin yeterli stok yok.";

                return RedirectToAction(nameof(Index));
            }

            if (count.Difference != 0)
            {
                var movement = new RawMaterialStockMovement
                {
                    RawMaterialId = count.RawMaterialId,
                    WarehouseId = count.WarehouseId,
                    MovementType =
                        count.Difference > 0 ? "IN" : "OUT",
                    Quantity = Math.Abs(count.Difference),
                    Unit = count.RawMaterial.Unit,
                    OperationNumber = "SAY-DUZ-" +
                        Guid.NewGuid().ToString("N")
                            .ToUpperInvariant(),
                    ReferenceNumber = count.CountNumber,
                    Notes = "Onayli stok sayim farki duzeltmesi",
                    CreatedByUserId = userId,
                    CreatedAtUtc = DateTime.UtcNow
                };

                approval.StockMovement = movement;
            }
        }

        _db.Set<RawStockCountApproval>().Add(approval);

        try
        {
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();

            TempData["CountError"] =
                "Karar kaydedilemedi. Sayim daha once islenmis olabilir.";

            return RedirectToAction(nameof(Index));
        }

        TempData["CountSuccess"] =
            decision == "Approve"
                ? "Sayim onaylandi. Fark varsa stok duzeltildi."
                : "Sayim reddedildi. Stok degistirilmedi.";

        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> Invalid(
        RawStockCountInput input)
    {
        await LoadLists();

        ViewBag.Counts = new List<RawStockCount>();
        ViewBag.Approvals = new Dictionary<int, string>();

        return View("Index", input);
    }

    private async Task LoadLists()
    {
        ViewBag.Materials = await _db.Set<RawMaterial>()
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync();

        ViewBag.Warehouses = await _db
            .Set<RawMaterialWarehouse>()
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync();
    }
}

public class RawStockCountInput
{
    [Range(1, int.MaxValue)]
    public int RawMaterialId { get; set; }

    [Range(1, int.MaxValue)]
    public int WarehouseId { get; set; }

    [Range(typeof(decimal), "0", "999999999999", ParseLimitsInInvariantCulture = true)]
    public decimal CountedQuantity { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }
}
