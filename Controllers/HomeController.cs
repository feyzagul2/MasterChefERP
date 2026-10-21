using System.Diagnostics;
using MasterChefTakip.Data;
using MasterChefTakip.Models;
using MasterChefTakip.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;


namespace MasterChefTakip.Controllers;

public class HomeController(
    ApplicationDbContext db,
    IAuthorizationService authorizationService) : Controller
{
    [Authorize]
    [ResponseCache(
        Location = ResponseCacheLocation.None,
        NoStore = true)]
    public async Task<IActionResult> Index()
    {
        var now = DateTime.UtcNow;
        var localToday = now.AddHours(3).Date;

        var fromUtc = DateTime.SpecifyKind(
            localToday.AddHours(-3),
            DateTimeKind.Utc);

        var untilUtc = fromUtc.AddDays(1);

        var model = new ProductionDashboardViewModel
        {
            LocalDate = localToday,

            // Palet ekranı mevcut sistemde yalnızca yöneticiye açık.
            CanViewStock = await HasPermissionAsync(AppPermissions.PalletsView),

            CanViewPackaging =
                await HasPermissionAsync(AppPermissions.PackagingManage),

            CanViewProducts =
                await HasPermissionAsync(AppPermissions.ProductsView),

            CanViewShipments =
                await HasPermissionAsync(AppPermissions.ShipmentsView),

            CanViewReturns =
                await HasPermissionAsync(AppPermissions.ReturnsView)
        };

        var productRows = new Dictionary<int, DashboardProductRow>();

        if (model.CanViewStock || model.CanViewPackaging)
        {
            var products = await db.Products
                .AsNoTracking()
                .Where(p => p.IsActive)
                .OrderBy(p => p.Name)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.Code,
                    p.LowStockThresholdUnits
                })
                .ToListAsync();

            foreach (var product in products)
            {
                productRows.Add(product.Id, new DashboardProductRow
                {
                    Id = product.Id,
                    Name = product.Name,
                    Code = product.Code,
                    LowStockThresholdUnits = product.LowStockThresholdUnits
                });
            }
        }

        if (model.CanViewStock)
        {
            var stocks = await db.PalletItems
                .AsNoTracking()
                .Where(i =>
                    i.Pallet.IsActive &&
                    !i.Pallet.IsCancelled &&
                    i.Product.IsActive)
                .Select(i => new
                {
                    i.ProductId,
                    i.BoxCount,
                    i.LooseUnitCount,
                    i.OpenBoxUnitCount,
                    i.UnitsPerBox
                })
                .ToListAsync();

            foreach (var stock in stocks)
            {
                model.Stock.Add(
                    stock.BoxCount,
                    stock.LooseUnitCount,
                    stock.UnitsPerBox,
                    stock.OpenBoxUnitCount);

                if (productRows.TryGetValue(
                    stock.ProductId,
                    out var product))
                {
                    product.Stock.Add(
                        stock.BoxCount,
                        stock.LooseUnitCount,
                        stock.UnitsPerBox,
                        stock.OpenBoxUnitCount);
                }
            }
        }

        if (model.CanViewPackaging)
        {
            // Paketleme ekranından oluşan geçerli stok girişleri.
            // Düzeltmeler ilk paketleme işleminin tarihine yazılır.
            // Geri alınmış girişler tekrar sayılmaz.
            var prepared = await (
                from movement in db.StockMovements.AsNoTracking()
                join batch in db.PackagingBatches.AsNoTracking()
                    on movement.PackagingBatchId equals batch.Id
                where movement.Type == StockMovementType.Receipt
                    && batch.CreatedAtUtc >= fromUtc
                    && batch.CreatedAtUtc < untilUtc
                    && !db.StockMovements.Any(reverse =>
                        reverse.ReversesMovementId == movement.Id)
                select new
                {
                    ProductId = movement.PalletItem.ProductId,
                    ProductName = movement.PalletItem.Product.Name,
                    ProductCode = movement.PalletItem.Product.Code,
                    movement.BoxCount,
                    movement.LooseUnitCount,
                    movement.UnitsPerBox
                })
                .ToListAsync();

            foreach (var entry in prepared)
            {
                model.Prepared.Add(
                    entry.BoxCount,
                    entry.LooseUnitCount,
                    entry.UnitsPerBox);

                // Bugün paketlenip sonradan pasife alınan ürün de
                // günlük paketleme miktarından kaybolmasın.
                if (!productRows.TryGetValue(
                    entry.ProductId,
                    out var product))
                {
                    product = new DashboardProductRow
                    {
                        Id = entry.ProductId,
                        Name = entry.ProductName,
                        Code = entry.ProductCode
                    };

                    productRows.Add(product.Id, product);
                }

                product.Prepared.Add(
                    entry.BoxCount,
                    entry.LooseUnitCount,
                    entry.UnitsPerBox);
            }
        }

        if (model.CanViewShipments)
        {
            var counts = await db.Shipments
                .AsNoTracking()
                .GroupBy(s => s.Status)
                .Select(g => new
                {
                    Status = g.Key,
                    Count = g.Count()
                })
                .ToListAsync();

            foreach (var count in counts)
            {
                switch (count.Status)
                {
                    case ShipmentStatus.Draft:
                        model.DraftShipments = count.Count;
                        break;

                    case ShipmentStatus.Confirmed:
                        model.ReadyShipments = count.Count;
                        break;

                    case ShipmentStatus.Shipped:
                        model.ShippedShipments = count.Count;
                        break;

                    case ShipmentStatus.Delivered:
                        model.DeliveredShipments = count.Count;
                        break;
                }
            }

            var pending = await db.Shipments
                .AsNoTracking()
                .Where(s =>
                    s.Status == ShipmentStatus.Draft ||
                    s.Status == ShipmentStatus.Confirmed)
                .OrderBy(s => s.CreatedAtUtc)
                .ThenBy(s => s.Id)
                .Take(10)
                .Select(s => new
                {
                    s.Id,
                    s.RecipientName,
                    s.Number,
                    s.Status,
                    s.CreatedAtUtc
                })
                .ToListAsync();

            model.PendingShipments = pending
                .Select(s => new DashboardShipmentRow
                {
                    Id = s.Id,
                    RecipientName = s.RecipientName,
                    Number = s.Number,
                    CreatedAtUtc = s.CreatedAtUtc,
                    Status = s.Status == ShipmentStatus.Draft
                        ? "Taslak"
                        : "Gönderime hazır"
                })
                .ToList();
        }

        if (model.CanViewReturns)
        {
            var pendingQuery = db.ReturnInspections
                .AsNoTracking()
                .Where(i =>
                    (
                        i.Status == ReturnInspectionStatus.Waiting ||
                        i.Status == ReturnInspectionStatus.Inspecting ||
                        i.Status == ReturnInspectionStatus.WaitingForPlacement
                    ) &&
                    (
                        i.ReturnReceipt == null ||
                        (
                            i.ReturnReceipt.Status != ReturnReceiptStatus.Cancelled &&
                            i.ReturnReceipt.Status != ReturnReceiptStatus.Completed
                        )
                    ));

            model.PendingReturnLines =
                await pendingQuery.CountAsync();

            model.PendingReturnUnits =
                await pendingQuery
                    .Select(i => (long?)i.ReceivedUnits)
                    .SumAsync() ?? 0;

            var pending = await pendingQuery
                .OrderBy(i => i.CreatedAtUtc)
                .ThenBy(i => i.Id)
                .Take(10)
                .Select(i => new
                {
                    i.Id,
                    i.ReceivedUnits,
                    i.Status,
                    i.CreatedAtUtc,
                    ProductName = i.ShipmentItem.ProductName,
                    RecipientName = i.ShipmentItem.Shipment.RecipientName,
                    ShipmentNumber = i.ShipmentItem.Shipment.Number
                })
                .ToListAsync();

            model.PendingReturns = pending
                .Select(i => new DashboardReturnRow
                {
                    Id = i.Id,
                    ReceivedUnits = i.ReceivedUnits,
                    CreatedAtUtc = i.CreatedAtUtc,
                    ProductName = i.ProductName,
                    RecipientName = i.RecipientName,
                    ShipmentNumber = i.ShipmentNumber,
                    Status = i.Status switch
                    {
                        ReturnInspectionStatus.Inspecting =>
                            "Kontrol ediliyor",

                        ReturnInspectionStatus.WaitingForPlacement =>
                            "Yerleştirme bekliyor",

                        _ => "Kontrol bekliyor"
                    }
                })
                .ToList();

            var damageMovements = await db.StockMovements
                .AsNoTracking()
                .Where(m =>
                    m.Type == StockMovementType.Damage &&
                    m.CreatedAtUtc >= fromUtc &&
                    m.CreatedAtUtc < untilUtc)
                .Select(m => new
                {
                    m.BoxCount,
                    m.LooseUnitCount,
                    m.UnitsPerBox
                })
                .ToListAsync();

            foreach (var movement in damageMovements)
            {
                model.TodayWarehouseDamage.Add(
                    movement.BoxCount,
                    movement.LooseUnitCount,
                    movement.UnitsPerBox);
            }

            model.TodayInspectedDamageUnits =
                await db.ReturnInspections
                    .AsNoTracking()
                    .Where(i =>
                        i.Status != ReturnInspectionStatus.Cancelled &&
                        i.InspectedAtUtc >= fromUtc &&
                        i.InspectedAtUtc < untilUtc &&
                        (
                            i.ReturnReceipt == null ||
                            i.ReturnReceipt.Status != ReturnReceiptStatus.Cancelled
                        ))
                    .Select(i => (long?)i.DamagedUnitCount)
                    .SumAsync() ?? 0;
        }

        // Stok uyarıları önce gösterilir.
        model.Products = productRows.Values
            .OrderByDescending(p =>
                model.CanViewStock && p.IsLowStock)
            .ThenByDescending(p =>
                model.CanViewStock && !p.Stock.IsComplete)
            .ThenBy(p => p.Name)
            .ToList();

        if (model.CanViewStock)
        {
            model.LowStockProductCount =
                model.Products.Count(p => p.IsLowStock);

            model.UnknownStockProductCount =
                model.Products.Count(p => !p.Stock.IsComplete);
        }
                var canViewDailyHistory =
            await HasPermissionAsync(AppPermissions.HistoryView);

        ViewData["CanViewDailyHistory"] = canViewDailyHistory;

        var dailyHistory = new List<HistoryEntry>();
        var currentUserId = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (canViewDailyHistory &&
            !string.IsNullOrWhiteSpace(currentUserId))
        {
            dailyHistory = await db.HistoryEntries
                .AsNoTracking()
                .Where(entry =>
                    entry.UserId == currentUserId &&
                    entry.CreatedAtUtc >= fromUtc &&
                    entry.CreatedAtUtc < untilUtc)
                .OrderByDescending(entry => entry.CreatedAtUtc)
                .ThenByDescending(entry => entry.Id)
                .Take(5)
                .ToListAsync();
        }

        ViewData["DailyHistory"] = dailyHistory;
        return View(model);
    }

    [HttpGet]
    [Authorize(Policy = AppPermissions.PalletsView)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ProductStockDetails(int productId)
    {
        if (productId <= 0)
            return BadRequest();

        var stocks = await db.PalletItems
            .AsNoTracking()
            .Include(i => i.Pallet)
            .Where(i =>
                i.ProductId == productId &&
                i.Product.IsActive &&
                i.Pallet.IsActive &&
                !i.Pallet.IsCancelled &&
                (
                    i.BoxCount > 0 ||
                    i.OpenBoxUnitCount > 0 ||
                    i.LooseUnitCount > 0
                ))
            .OrderBy(i => i.Pallet.Barcode)
            .ThenBy(i => i.Id)
            .ToListAsync();

        return PartialView("_ProductStockDetails", stocks);
    }

    private async Task<bool> HasPermissionAsync(string permission)
    {
        var result = await authorizationService.AuthorizeAsync(
            User,
            permission);

        return result.Succeeded;
    }

    public IActionResult Privacy() => View();

    [ResponseCache(
        Duration = 0,
        Location = ResponseCacheLocation.None,
        NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel
        {
            RequestId = Activity.Current?.Id
                ?? HttpContext.TraceIdentifier
        });
    }
}