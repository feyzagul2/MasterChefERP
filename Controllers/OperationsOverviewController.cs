
using MasterChefTakip.Data;
using MasterChefTakip.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

[Authorize(Roles = AppRoles.Admin)]
public class OperationsOverviewController : Controller
{
    private readonly ApplicationDbContext _db;

    public OperationsOverviewController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var model = new OperationsOverviewPage();

        model.ProductCount = await _db.Set<Product>()
            .AsNoTracking().CountAsync();

        model.PalletCount = await _db.Set<Pallet>()
            .AsNoTracking().CountAsync();

        model.PalletItemCount = await _db.Set<PalletItem>()
            .AsNoTracking().CountAsync();

        model.ShipmentCount = await _db.Set<Shipment>()
            .AsNoTracking().CountAsync();

        // MC_SHIPMENT_OVERVIEW_V1
        model.ActivePalletCount = await _db.Set<Pallet>()
            .AsNoTracking().CountAsync(x => x.IsActive && !x.IsCancelled);

        var shipments = await _db.Set<Shipment>()
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync();

        model.DraftShipmentCount = shipments.Count(x => x.Status == ShipmentStatus.Draft);
        model.ConfirmedShipmentCount = shipments.Count(x => x.Status == ShipmentStatus.Confirmed);
        model.ShippedShipmentCount = shipments.Count(x => x.Status == ShipmentStatus.Shipped);
        model.DeliveredShipmentCount = shipments.Count(x => x.Status == ShipmentStatus.Delivered);
        model.CancelledShipmentCount = shipments.Count(x => x.Status == ShipmentStatus.Cancelled);
        model.RecentShipments = shipments.Take(5).ToList();

        model.RawMaterialCount = await _db.Set<RawMaterial>()
            .AsNoTracking().CountAsync();

        var orders = await _db.Set<ProductionOrder>()
            .AsNoTracking()
            .Include(x => x.Recipe)
            .OrderByDescending(x => x.Id)
            .Take(100)
            .ToListAsync();

        model.ProductionOrders = orders;

        model.ProductionOrderCount =
            await _db.Set<ProductionOrder>().CountAsync();

        model.CompletedProductionCount =
            await _db.Set<ProductionOrder>()
                .CountAsync(x => x.Status == "Completed");

        var lots = await _db.Set<ProductLot>()
            .AsNoTracking()
            .Include(x => x.Product)
            .OrderBy(x => x.ExpiryDate)
            .ToListAsync();

        model.LotCount = lots.Count;

        model.ApprovedLotCount =
            lots.Count(x => x.Status == "Approved");

        model.RejectedLotCount =
            lots.Count(x => x.Status == "Rejected");

        model.PendingLotCount =
            lots.Count(x => x.Status == "Pending");

        model.ExpiredLotCount =
            lots.Count(x => x.ExpiryDate.Date < DateTime.Today);

        model.ExpiringLots = lots
            .Where(x =>
                x.ExpiryDate.Date >= DateTime.Today &&
                x.ExpiryDate.Date <= DateTime.Today.AddDays(90))
            .Take(30)
            .ToList();

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

        var balances = movements
            .GroupBy(x => x.RawMaterialId)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(x =>
                    x.MovementType == "IN"
                    ? x.Quantity
                    : -x.Quantity));

        model.Materials = materials
            .Select(x => new OverviewMaterialRow
            {
                Code = x.Code,
                Name = x.Name,
                Unit = x.Unit,
                Quantity = balances.GetValueOrDefault(x.Id),
                MinimumStock = x.MinimumStock,
                IsActive = x.IsActive
            })
            .ToList();

        model.CriticalMaterialCount = model.Materials
            .Count(x =>
                x.IsActive &&
                x.Quantity <= x.MinimumStock);

        model.StockCountTotal =
            await _db.Set<RawStockCount>()
                .AsNoTracking()
                .CountAsync();

        var approvals = await _db
            .Set<RawStockCountApproval>()
            .AsNoTracking()
            .Select(x => x.Decision)
            .ToListAsync();

        model.ApprovedStockCounts =
            approvals.Count(x => x == "Approved");

        model.RejectedStockCounts =
            approvals.Count(x => x == "Rejected");

        model.PendingStockCounts =
            model.StockCountTotal - approvals.Count;

        return View(model);
    }
}

public class OverviewMaterialRow
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal MinimumStock { get; set; }
    public bool IsActive { get; set; }
}

public class OperationsOverviewPage
{
    public int ProductCount { get; set; }
    public int PalletCount { get; set; }
    public int PalletItemCount { get; set; }
    public int ShipmentCount { get; set; }
    public int ActivePalletCount { get; set; }
    public int DraftShipmentCount { get; set; }
    public int ConfirmedShipmentCount { get; set; }
    public int ShippedShipmentCount { get; set; }
    public int DeliveredShipmentCount { get; set; }
    public int CancelledShipmentCount { get; set; }
    public int RawMaterialCount { get; set; }
    public int CriticalMaterialCount { get; set; }

    public int ProductionOrderCount { get; set; }
    public int CompletedProductionCount { get; set; }

    public int LotCount { get; set; }
    public int ApprovedLotCount { get; set; }
    public int RejectedLotCount { get; set; }
    public int PendingLotCount { get; set; }
    public int ExpiredLotCount { get; set; }

    public int StockCountTotal { get; set; }
    public int ApprovedStockCounts { get; set; }
    public int RejectedStockCounts { get; set; }
    public int PendingStockCounts { get; set; }

    public List<OverviewMaterialRow> Materials { get; set; } = new();
    public List<ProductionOrder> ProductionOrders { get; set; } = new();
    public List<ProductLot> ExpiringLots { get; set; } = new();
    public List<Shipment> RecentShipments { get; set; } = new();
}
