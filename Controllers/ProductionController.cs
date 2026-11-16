
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using MasterChefTakip.Models;
using MasterChefTakip.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

[Authorize(Roles = AppRoles.Admin)]
public class ProductionController : Controller
{
    private readonly ApplicationDbContext _db;

    public ProductionController(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var recipes = await _db.Set<ProductionRecipe>()
            .AsNoTracking()
            .Include(x => x.Product)
            .OrderBy(x => x.Name)
            .ToListAsync();

        return View(recipes);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddRecipe(
        string code,
        string name,
        decimal batchQuantityKg,
        int? productId)
    {
        code = (code ?? "").Trim().ToUpperInvariant();
        name = (name ?? "").Trim();

        if (code.Length == 0 || code.Length > 50 ||
            name.Length == 0 || name.Length > 200 ||
            batchQuantityKg <= 0 ||
            decimal.Round(batchQuantityKg, 4) != batchQuantityKg)
        {
            TempData["ProductionError"] =
                "Recete bilgileri gecersiz.";

            return RedirectToAction(nameof(Index));
        }

        if (productId.HasValue &&
            !await _db.Set<Product>()
                .AnyAsync(x => x.Id == productId.Value && x.IsActive))
        {
            TempData["ProductionError"] =
                "Gecerli mamul seciniz.";

            return RedirectToAction(nameof(Index));
        }

        if (await _db.Set<ProductionRecipe>()
            .AnyAsync(x => x.Code == code))
        {
            TempData["ProductionError"] = "Recete kodu mevcut.";
            return RedirectToAction(nameof(Index));
        }

        var recipe = new ProductionRecipe
        {
            Code = code,
            Name = name,
            BatchQuantityKg = batchQuantityKg,
            ProductId = productId,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Set<ProductionRecipe>().Add(recipe);
        await _db.SaveChangesAsync();

        return RedirectToAction(
            nameof(Recipe),
            new { id = recipe.Id });
    }

    public async Task<IActionResult> Recipe(int id)
    {
        var recipe = await _db.Set<ProductionRecipe>()
            .AsNoTracking()
            .Include(x => x.Product)
            .Include(x => x.Lines)
            .ThenInclude(x => x.RawMaterial)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (recipe == null)
            return NotFound();

        ViewBag.Materials = await _db.Set<RawMaterial>()
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync();

        return View(recipe);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddLine(
        int recipeId,
        int rawMaterialId,
        decimal quantity)
    {
        if (quantity <= 0 ||
            decimal.Round(quantity, 4) != quantity)
        {
            TempData["ProductionError"] = "Miktar gecersiz.";
            return RedirectToAction(nameof(Recipe),
                new { id = recipeId });
        }

        var recipe = await _db.Set<ProductionRecipe>()
            .FirstOrDefaultAsync(x => x.Id == recipeId && x.IsActive);

        var material = await _db.Set<RawMaterial>()
            .FirstOrDefaultAsync(x =>
                x.Id == rawMaterialId && x.IsActive);

        if (recipe == null || material == null)
            return BadRequest("Recete veya hammadde bulunamadi.");

        var hasOrders = await _db.Set<ProductionOrder>()
            .AnyAsync(x => x.ProductionRecipeId == recipeId);

        if (hasOrders)
        {
            TempData["ProductionError"] =
                "Uretim emri acilmis recetenin icerigi degistirilemez.";

            return RedirectToAction(nameof(Recipe),
                new { id = recipeId });
        }

        var existing = await _db.Set<ProductionRecipeLine>()
            .AnyAsync(x =>
                x.ProductionRecipeId == recipeId &&
                x.RawMaterialId == rawMaterialId);

        if (existing)
        {
            TempData["ProductionError"] =
                "Bu hammadde recetede zaten bulunuyor.";

            return RedirectToAction(nameof(Recipe),
                new { id = recipeId });
        }

        _db.Set<ProductionRecipeLine>().Add(
            new ProductionRecipeLine
            {
                ProductionRecipeId = recipeId,
                RawMaterialId = rawMaterialId,
                Quantity = quantity
            });

        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Recipe),
            new { id = recipeId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveLine(int id)
    {
        var line = await _db.Set<ProductionRecipeLine>()
            .FirstOrDefaultAsync(x => x.Id == id);

        if (line == null)
            return NotFound();

        var recipeId = line.ProductionRecipeId;

        if (await _db.Set<ProductionOrder>()
            .AnyAsync(x => x.ProductionRecipeId == recipeId))
        {
            return Conflict(
                "Uretim emri olan receteden hammadde silinemez.");
        }

        _db.Set<ProductionRecipeLine>().Remove(line);
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Recipe),
            new { id = recipeId });
    }

    public async Task<IActionResult> Orders()
    {
        var orders = await _db.Set<ProductionOrder>()
            .AsNoTracking()
            .Include(x => x.Recipe)
            .Include(x => x.Warehouse)
            .OrderByDescending(x => x.Id)
            .Take(200)
            .ToListAsync();

        ViewBag.Recipes = await _db.Set<ProductionRecipe>()
            .AsNoTracking()
            .Where(x => x.IsActive && x.Lines.Any())
            .OrderBy(x => x.Name)
            .ToListAsync();

        ViewBag.Warehouses = await _db.Set<RawMaterialWarehouse>()
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync();

        return View(orders);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddOrder(
        int recipeId,
        int warehouseId,
        decimal plannedQuantityKg,
        string? notes)
    {
        if (plannedQuantityKg <= 0 ||
            decimal.Round(plannedQuantityKg, 4)
                != plannedQuantityKg ||
            (notes?.Length ?? 0) > 500)
        {
            TempData["ProductionError"] =
                "Uretim miktari veya aciklama gecersiz.";

            return RedirectToAction(nameof(Orders));
        }

        var recipe = await _db.Set<ProductionRecipe>()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x =>
                x.Id == recipeId && x.IsActive);

        var warehouse = await _db.Set<RawMaterialWarehouse>()
            .FirstOrDefaultAsync(x =>
                x.Id == warehouseId && x.IsActive);

        if (recipe == null ||
            warehouse == null ||
            recipe.BatchQuantityKg <= 0 ||
            recipe.Lines.Count == 0)
        {
            TempData["ProductionError"] =
                "Gecerli recete ve hammadde deposu seciniz.";

            return RedirectToAction(nameof(Orders));
        }

        var order = new ProductionOrder
        {
            OrderNumber = "URT-" +
                Guid.NewGuid().ToString("N")[..12]
                    .ToUpperInvariant(),
            ProductionRecipeId = recipe.Id,
            WarehouseId = warehouse.Id,
            PlannedQuantityKg = plannedQuantityKg,
            Status = "Planned",
            Notes = notes?.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Set<ProductionOrder>().Add(order);
        await _db.SaveChangesAsync();

        return RedirectToAction(
            nameof(Order), new { id = order.Id });
    }

    public async Task<IActionResult> Order(int id)
    {
        var order = await _db.Set<ProductionOrder>()
            .AsNoTracking()
            .Include(x => x.Recipe)
            .ThenInclude(x => x.Lines)
            .ThenInclude(x => x.RawMaterial)
            .Include(x => x.Warehouse)
            .Include(x => x.Consumptions)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (order == null)
            return NotFound();

        var movements = await _db.Set<RawMaterialStockMovement>()
            .AsNoTracking()
            .Where(x => x.WarehouseId == order.WarehouseId)
            .Select(x => new
            {
                x.RawMaterialId,
                x.MovementType,
                x.Quantity
            })
            .ToListAsync();

        var balances = movements
            .GroupBy(x => x.RawMaterialId)
            .ToDictionary(
                x => x.Key,
                x => x.Sum(m => m.MovementType == "IN"
                    ? m.Quantity
                    : -m.Quantity));

        ViewBag.Balances = balances;

        return View(order);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(int id)
    {
        var userId = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        await using var transaction =
            await _db.Database.BeginTransactionAsync();

        var order = await _db.Set<ProductionOrder>()
            .Include(x => x.Recipe)
            .ThenInclude(x => x.Lines)
            .ThenInclude(x => x.RawMaterial)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (order == null)
            return NotFound();

        if (order.Status != "Planned")
            return Conflict("Bu uretim emri zaten islenmis.");

        if (order.Recipe.Lines.Count == 0 ||
            order.Recipe.BatchQuantityKg <= 0)
        {
            return BadRequest("Recete gecersiz.");
        }

        var requirements = new List<(ProductionRecipeLine Line, decimal Quantity)>();

        foreach (var line in order.Recipe.Lines)
        {
            decimal needed;

            try
            {
                needed = decimal.Round(
                    line.Quantity *
                    order.PlannedQuantityKg /
                    order.Recipe.BatchQuantityKg,
                    4,
                    MidpointRounding.AwayFromZero);
            }
            catch (OverflowException)
            {
                return BadRequest("Uretim miktari cok buyuk.");
            }

            if (needed <= 0 || !line.RawMaterial.IsActive)
                return BadRequest("Hammadde recetesi gecersiz.");

            requirements.Add((line, needed));
        }

        var materialIds = requirements
            .Select(x => x.Line.RawMaterialId)
            .ToHashSet();

        var history = await _db.Set<RawMaterialStockMovement>()
            .AsNoTracking()
            .Where(x =>
                x.WarehouseId == order.WarehouseId &&
                materialIds.Contains(x.RawMaterialId))
            .Select(x => new
            {
                x.RawMaterialId,
                x.MovementType,
                x.Quantity
            })
            .ToListAsync();

        var balances = history
            .GroupBy(x => x.RawMaterialId)
            .ToDictionary(
                x => x.Key,
                x => x.Sum(m =>
                    m.MovementType == "IN"
                    ? m.Quantity
                    : -m.Quantity));

        foreach (var entry in requirements)
        {
            var balance = balances.GetValueOrDefault(
                entry.Line.RawMaterialId);

            if (balance < entry.Quantity)
            {
                TempData["ProductionError"] =
                    entry.Line.RawMaterial.Name +
                    ": Yetersiz stok. Mevcut " +
                    balance.ToString("N4") + " " +
                    entry.Line.RawMaterial.Unit;

                return RedirectToAction(
                    nameof(Order), new { id });
            }
        }

        foreach (var entry in requirements)
        {
            var movement = new RawMaterialStockMovement
            {
                RawMaterialId = entry.Line.RawMaterialId,
                WarehouseId = order.WarehouseId,
                MovementType = "OUT",
                Quantity = entry.Quantity,
                Unit = entry.Line.RawMaterial.Unit,
                OperationNumber = "HM-URT-" +
                    Guid.NewGuid().ToString("N")
                        .ToUpperInvariant(),
                ReferenceNumber = order.OrderNumber,
                Notes = "Uretim emri hammadde tuketimi",
                CreatedByUserId = userId,
                CreatedAtUtc = DateTime.UtcNow
            };

            order.Consumptions.Add(
                new ProductionConsumption
                {
                    RawMaterialId = entry.Line.RawMaterialId,
                    Movement = movement,
                    Quantity = entry.Quantity,
                    Unit = entry.Line.RawMaterial.Unit
                });
        }

        // MC_PACKAGING_ERROR_HANDLING_V1
        // Ambalaj stok yetersizliginde uretimi ve tum stok
        // hareketlerini ayni transaction icinde geri al.
        try
        {
            MCProductionPackagingStock.Apply(
                _db,
                order.Recipe.ProductId,
                order.Id,
                order.PlannedQuantityKg);
        }
        catch (InvalidOperationException ex)
        {
            await transaction.RollbackAsync();

            TempData["ProductionError"] =
                "Ambalaj stok kontrolu: " + ex.Message;

            return RedirectToAction(
                nameof(Order), new { id });
        }
        order.Status = "Completed";
        order.CompletedAtUtc = DateTime.UtcNow;
        order.CompletedByUserId = userId;

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        TempData["ProductionSuccess"] =
            "Uretim tamamlandi. Hammaddeler stoktan dusuldu.";

        return RedirectToAction(
            nameof(Order), new { id });
    }
}
