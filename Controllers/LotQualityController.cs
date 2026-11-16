
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using MasterChefTakip.Models;
using MasterChefTakip.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

[Authorize(Roles = AppRoles.Admin)]
public class LotQualityController : Controller
{
    private readonly ApplicationDbContext _db;

    public LotQualityController(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var lots = await _db.Set<ProductLot>()
            .AsNoTracking()
            .Include(x => x.Product)
            .OrderByDescending(x => x.Id)
            .Take(300)
            .ToListAsync();

        return View(lots);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await LoadProducts();

        return View(new ProductLotInput
        {
            ProductionDate = DateTime.Today,
            ExpiryDate = DateTime.Today.AddYears(1)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProductLotInput input)
    {
        var userId = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        input.LotNumber = (input.LotNumber ?? "")
            .Trim().ToUpperInvariant();

        if (input.LotNumber.Length == 0 ||
            input.LotNumber.Length > 70)
        {
            ModelState.AddModelError(
                nameof(input.LotNumber),
                "Gecerli lot numarasi giriniz.");
        }

        if (input.ProductionDate == default ||
            input.ExpiryDate == default ||
            input.ExpiryDate.Date < input.ProductionDate.Date)
        {
            ModelState.AddModelError(
                "",
                "SKT uretim tarihinden once olamaz.");
        }

        if (!await _db.Set<Product>()
            .AnyAsync(x => x.Id == input.ProductId && x.IsActive))
        {
            ModelState.AddModelError(
                nameof(input.ProductId),
                "Aktif urun seciniz.");
        }

        if (await _db.Set<ProductLot>()
            .AnyAsync(x =>
                x.ProductId == input.ProductId &&
                x.LotNumber == input.LotNumber))
        {
            ModelState.AddModelError(
                nameof(input.LotNumber),
                "Bu urunun lot numarasi zaten mevcut.");
        }

        if (!ModelState.IsValid)
        {
            await LoadProducts();
            return View(input);
        }

        var lot = new ProductLot
        {
            ProductId = input.ProductId,
            LotNumber = input.LotNumber,
            ProductionDate = input.ProductionDate.Date,
            ExpiryDate = input.ExpiryDate.Date,
            Notes = input.Notes?.Trim(),
            Status = "Pending",
            CreatedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Set<ProductLot>().Add(lot);

        await _db.SaveChangesAsync();

        return RedirectToAction(
            nameof(Details),
            new { id = lot.Id });
    }

    public async Task<IActionResult> Details(int id)
    {
        var lot = await _db.Set<ProductLot>()
            .AsNoTracking()
            .Include(x => x.Product)
            .Include(x => x.Inspections)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (lot == null)
            return NotFound();

        return View(lot);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Inspect(
        int lotId,
        string inspectionName,
        string result,
        string? notes)
    {
        var userId = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        inspectionName = (inspectionName ?? "").Trim();

        if (inspectionName.Length == 0 ||
            inspectionName.Length > 200 ||
            (notes?.Length ?? 0) > 1000)
        {
            return BadRequest("Kalite kontrol bilgileri gecersiz.");
        }

        if (!new[]
        {
            "Approved", "Rejected", "Pending"
        }.Contains(result))
        {
            return BadRequest("Gecersiz kalite sonucu.");
        }

        await using var transaction =
            await _db.Database.BeginTransactionAsync();

        var lot = await _db.Set<ProductLot>()
            .FirstOrDefaultAsync(x => x.Id == lotId);

        if (lot == null)
            return NotFound();

        _db.Set<ProductQualityInspection>().Add(
            new ProductQualityInspection
            {
                ProductLotId = lot.Id,
                InspectionName = inspectionName,
                Result = result,
                Notes = notes?.Trim(),
                InspectedByUserId = userId,
                InspectedAtUtc = DateTime.UtcNow
            });

        lot.Status = result;

        await _db.SaveChangesAsync();

        await transaction.CommitAsync();

        TempData["QualitySuccess"] =
            "Kalite kontrol sonucu kaydedildi.";

        return RedirectToAction(
            nameof(Details),
            new { id = lot.Id });
    }

    private async Task LoadProducts()
    {
        ViewBag.Products = await _db.Set<Product>()
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync();
    }
}

public class ProductLotInput
{
    public int ProductId { get; set; }

    [Required, StringLength(70)]
    public string LotNumber { get; set; } = "";

    [DataType(DataType.Date)]
    public DateTime ProductionDate { get; set; }

    [DataType(DataType.Date)]
    public DateTime ExpiryDate { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }
}
