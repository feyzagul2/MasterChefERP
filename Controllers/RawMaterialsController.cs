
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using MasterChefTakip.Models;
using MasterChefTakip.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

[Authorize(Roles = AppRoles.Admin)]
public class RawMaterialsController : Controller
{
    private readonly ApplicationDbContext _db;

    public RawMaterialsController(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index([FromQuery] bool critical = false)
    {
        var materials = await _db.Set<RawMaterial>()
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync();

        var movements = await _db
            .Set<RawMaterialStockMovement>()
            .AsNoTracking()
            .Select(x => new
            {
                x.RawMaterialId,
                x.MovementType,
                x.Quantity
            })
            .ToListAsync();

        var stocks = movements
            .GroupBy(x => x.RawMaterialId)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(x =>
                    x.MovementType == "IN"
                    ? x.Quantity
                    : -x.Quantity));

        var model = materials.Select(x =>
            new RawMaterialListRow
            {
                Material = x,
                Quantity = stocks.GetValueOrDefault(x.Id)
            }).ToList();

        if (critical)
            model = model.Where(x => x.Material.IsActive && x.Quantity <= x.Material.MinimumStock).ToList();

        ViewBag.CriticalOnly = critical;

        return View(model);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new RawMaterialInput());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RawMaterialInput input)
    {
        if (!ModelState.IsValid)
            return View(input);

        var code = input.Code.Trim().ToUpperInvariant();
        var name = input.Name.Trim();

        if (!new[] { "Kg", "L" }.Contains(input.Unit))
            ModelState.AddModelError(
                nameof(input.Unit),
                "Birim Kg veya L olmalidir.");

        if (input.MinimumStock < 0)
            ModelState.AddModelError(
                nameof(input.MinimumStock),
                "Minimum stok negatif olamaz.");

        if (await _db.Set<RawMaterial>()
            .AnyAsync(x => x.Code == code))
        {
            ModelState.AddModelError(
                nameof(input.Code),
                "Bu hammadde kodu kullaniliyor.");
        }

        if (!ModelState.IsValid)
            return View(input);

        var material = new RawMaterial
        {
            Code = code,
            Name = name,
            Unit = input.Unit,
            MinimumStock = input.MinimumStock,
            Category = input.Category?.Trim(),
            Notes = input.Notes?.Trim(),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Set<RawMaterial>().Add(material);

        await _db.SaveChangesAsync();

        return RedirectToAction(
            nameof(Details),
            new { id = material.Id });
    }

    public async Task<IActionResult> Warehouses()
    {
        var warehouses = await _db
            .Set<RawMaterialWarehouse>()
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync();

        return View(warehouses);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddWarehouse(
        string code,
        string name,
        string? location)
    {
        code = (code ?? "").Trim().ToUpperInvariant();
        name = (name ?? "").Trim();
        location = location?.Trim();

        if (code.Length == 0 || code.Length > 40 ||
            name.Length == 0 || name.Length > 150 ||
            (location?.Length ?? 0) > 250)
        {
            TempData["RawError"] =
                "Depo kodu, adi veya konumu gecersiz.";

            return RedirectToAction(nameof(Warehouses));
        }

        if (await _db.Set<RawMaterialWarehouse>()
            .AnyAsync(x => x.Code == code))
        {
            TempData["RawError"] =
                "Bu depo kodu zaten mevcut.";

            return RedirectToAction(nameof(Warehouses));
        }

        _db.Set<RawMaterialWarehouse>().Add(
            new RawMaterialWarehouse
            {
                Code = code,
                Name = name,
                Location = location,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            });

        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Warehouses));
    }

    public async Task<IActionResult> Details(int id)
    {
        var material = await _db.Set<RawMaterial>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);

        if (material == null)
            return NotFound();

        var movements = await _db
            .Set<RawMaterialStockMovement>()
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Where(x => x.RawMaterialId == id)
            .OrderByDescending(x => x.Id)
            .ToListAsync();

        var balances = movements
            .GroupBy(x => new
            {
                x.WarehouseId,
                x.Warehouse.Name
            })
            .Select(x => new RawWarehouseBalance
            {
                WarehouseId = x.Key.WarehouseId,
                WarehouseName = x.Key.Name,
                Quantity = x.Sum(m =>
                    m.MovementType == "IN"
                    ? m.Quantity
                    : -m.Quantity)
            })
            .OrderBy(x => x.WarehouseName)
            .ToList();

        var warehouses = await _db
            .Set<RawMaterialWarehouse>()
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync();

        return View(new RawMaterialDetailsPage
        {
            Material = material,
            Movements = movements,
            Balances = balances,
            Warehouses = warehouses,
            TotalQuantity = balances.Sum(x => x.Quantity)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Stock(
        RawMaterialStockInput input)
    {
        if (input.Quantity <= 0 ||
            decimal.Round(input.Quantity, 4) != input.Quantity)
        {
            TempData["RawError"] =
                "Miktar sifirdan buyuk ve en fazla 4 ondalik olmali.";

            return RedirectToAction(
                nameof(Details),
                new { id = input.MaterialId });
        }

        if (input.MovementType != "IN" &&
            input.MovementType != "OUT")
        {
            return BadRequest("Gecersiz stok hareketi.");
        }

        if ((input.ReferenceNumber?.Length ?? 0) > 100 ||
            (input.Notes?.Length ?? 0) > 500)
        {
            return BadRequest("Aciklama alani cok uzun.");
        }

        var userId = User.FindFirstValue(
            ClaimTypes.NameIdentifier) ?? "";

        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        await using var transaction =
            await _db.Database.BeginTransactionAsync();

        var material = await _db.Set<RawMaterial>()
            .FirstOrDefaultAsync(x =>
                x.Id == input.MaterialId &&
                x.IsActive);

        var warehouse = await _db
            .Set<RawMaterialWarehouse>()
            .FirstOrDefaultAsync(x =>
                x.Id == input.WarehouseId &&
                x.IsActive);

        if (material == null || warehouse == null)
            return BadRequest(
                "Aktif hammadde ve depo seciniz.");

        if (input.MovementType == "OUT")
        {
            var history = await _db
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

            var available = history.Sum(x =>
                x.MovementType == "IN"
                ? x.Quantity
                : -x.Quantity);

            if (available < input.Quantity)
            {
                TempData["RawError"] =
                    "Yetersiz hammadde stogu. Mevcut: " +
                    available.ToString("N4") +
                    " " + material.Unit;

                return RedirectToAction(
                    nameof(Details),
                    new { id = material.Id });
            }
        }

        _db.Set<RawMaterialStockMovement>().Add(
            new RawMaterialStockMovement
            {
                RawMaterialId = material.Id,
                WarehouseId = warehouse.Id,
                MovementType = input.MovementType,
                Quantity = input.Quantity,
                Unit = material.Unit,
                OperationNumber = "HM-" +
                    Guid.NewGuid().ToString("N")
                    .ToUpperInvariant(),
                ReferenceNumber =
                    input.ReferenceNumber?.Trim(),
                Notes = input.Notes?.Trim(),
                CreatedByUserId = userId,
                CreatedAtUtc = DateTime.UtcNow
            });

        await _db.SaveChangesAsync();

        await transaction.CommitAsync();

        TempData["RawSuccess"] =
            "Hammadde stok hareketi kaydedildi.";

        return RedirectToAction(
            nameof(Details),
            new { id = material.Id });
    }
}

public class RawMaterialInput
{
    [Required, StringLength(50)]
    public string Code { get; set; } = "";

    [Required, StringLength(200)]
    public string Name { get; set; } = "";

    [Required]
    public string Unit { get; set; } = "Kg";

    public decimal MinimumStock { get; set; }

    [StringLength(100)]
    public string? Category { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }
}

public class RawMaterialStockInput
{
    public int MaterialId { get; set; }
    public int WarehouseId { get; set; }

    public string MovementType { get; set; } = "IN";

    public decimal Quantity { get; set; }

    public string? ReferenceNumber { get; set; }

    public string? Notes { get; set; }
}

public class RawMaterialListRow
{
    public RawMaterial Material { get; set; } = null!;

    public decimal Quantity { get; set; }
}

public class RawWarehouseBalance
{
    public int WarehouseId { get; set; }

    public string WarehouseName { get; set; } = "";

    public decimal Quantity { get; set; }
}

public class RawMaterialDetailsPage
{
    public RawMaterial Material { get; set; } = null!;

    public decimal TotalQuantity { get; set; }

    public List<RawMaterialStockMovement> Movements
        { get; set; } = new();

    public List<RawWarehouseBalance> Balances
        { get; set; } = new();

    public List<RawMaterialWarehouse> Warehouses
        { get; set; } = new();
}
