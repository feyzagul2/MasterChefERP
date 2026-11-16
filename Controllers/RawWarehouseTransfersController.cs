
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using MasterChefTakip.Data;
using MasterChefTakip.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

[Authorize(Roles = AppRoles.Admin)]
public class RawWarehouseTransfersController : Controller
{
    private readonly ApplicationDbContext _db;

    public RawWarehouseTransfersController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        await LoadLists();

        var history = await _db.Set<RawMaterialStockMovement>()
            .AsNoTracking()
            .Include(x => x.RawMaterial)
            .Include(x => x.Warehouse)
            .Where(x => x.ReferenceNumber != null &&
                        x.ReferenceNumber.StartsWith("TR-"))
            .OrderByDescending(x => x.Id)
            .Take(300)
            .ToListAsync();

        ViewBag.History = history;

        return View(new RawTransferInput());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(RawTransferInput input)
    {
        if (input.SourceWarehouseId == input.TargetWarehouseId)
            ModelState.AddModelError(
                "", "Kaynak ve hedef depo ayni olamaz.");

        if (input.Quantity <= 0 ||
            decimal.Round(input.Quantity, 4) != input.Quantity)
            ModelState.AddModelError(
                "", "Miktar pozitif ve en fazla 4 ondalik olmali.");

        if (input.Notes?.Length > 500)
            ModelState.AddModelError(
                "", "Aciklama 500 karakteri asamaz.");

        if (!ModelState.IsValid)
            return await InvalidForm(input);

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        await using var transaction =
            await _db.Database.BeginTransactionAsync();

        var material = await _db.Set<RawMaterial>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Id == input.RawMaterialId && x.IsActive);

        var warehouses = await _db.Set<RawMaterialWarehouse>()
            .AsNoTracking()
            .Where(x => x.IsActive &&
                (x.Id == input.SourceWarehouseId ||
                 x.Id == input.TargetWarehouseId))
            .Select(x => x.Id)
            .ToListAsync();

        if (material == null || warehouses.Distinct().Count() != 2)
        {
            ModelState.AddModelError(
                "", "Aktif hammadde ve iki farkli aktif depo seciniz.");

            return await InvalidForm(input);
        }

        var movements = await _db.Set<RawMaterialStockMovement>()
            .AsNoTracking()
            .Where(x =>
                x.RawMaterialId == material.Id &&
                x.WarehouseId == input.SourceWarehouseId)
            .Select(x => new
            {
                x.MovementType,
                x.Quantity
            })
            .ToListAsync();

        var available = movements.Sum(x =>
            x.MovementType == "IN" ? x.Quantity : -x.Quantity);

        if (available < input.Quantity)
        {
            ModelState.AddModelError(
                "",
                "Yetersiz stok. Kaynak depoda " +
                available.ToString("N4") + " " +
                material.Unit + " bulunuyor.");

            return await InvalidForm(input);
        }

        var reference = "TR-" +
            Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();

        var now = DateTime.UtcNow;
        var note = input.Notes?.Trim();

        _db.Set<RawMaterialStockMovement>().AddRange(
            new RawMaterialStockMovement
            {
                RawMaterialId = material.Id,
                WarehouseId = input.SourceWarehouseId,
                MovementType = "OUT",
                Quantity = input.Quantity,
                Unit = material.Unit,
                OperationNumber = reference + "-OUT",
                ReferenceNumber = reference,
                Notes = "Depolar arasi transfer cikisi. " + note,
                CreatedByUserId = userId,
                CreatedAtUtc = now
            },
            new RawMaterialStockMovement
            {
                RawMaterialId = material.Id,
                WarehouseId = input.TargetWarehouseId,
                MovementType = "IN",
                Quantity = input.Quantity,
                Unit = material.Unit,
                OperationNumber = reference + "-IN",
                ReferenceNumber = reference,
                Notes = "Depolar arasi transfer girisi. " + note,
                CreatedByUserId = userId,
                CreatedAtUtc = now
            });

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        TempData["RawTransferSuccess"] =
            "Transfer tamamlandi. Belge numarasi: " + reference;

        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> InvalidForm(RawTransferInput input)
    {
        await LoadLists();

        ViewBag.History = new List<RawMaterialStockMovement>();

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

public class RawTransferInput
{
    [Range(1, int.MaxValue)]
    public int RawMaterialId { get; set; }

    [Range(1, int.MaxValue)]
    public int SourceWarehouseId { get; set; }

    [Range(1, int.MaxValue)]
    public int TargetWarehouseId { get; set; }

    [Range(typeof(decimal), "0.0001", "999999999", ParseLimitsInInvariantCulture = true)]
    public decimal Quantity { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }
}
