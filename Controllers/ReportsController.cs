using MasterChefTakip.Data;
using MasterChefTakip.Models;
using MasterChefTakip.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

[Authorize(Policy = AppPermissions.ReportsView)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ReportsController(ApplicationDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        DateOnly? start = null,
        DateOnly? end = null,
        string group = "day")
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

        var model = new ReportIndexViewModel
        {
            Start = start ?? today.AddDays(-6),
            End = end ?? today,
            Group = group
        };

        if (group is not ("day" or "week" or "month"))
        {
            ModelState.AddModelError(
                "group", "Günlük, haftalık veya aylık görünüm seçin.");
        }

        if (model.Start > model.End)
        {
            ModelState.AddModelError(
                "start", "Başlangıç tarihi bitişten sonra olamaz.");
        }

        if (model.End > today)
        {
            ModelState.AddModelError(
                "end", "Bitiş tarihi bugünden sonra olamaz.");
        }

        if (model.End.DayNumber - model.Start.DayNumber > 365)
        {
            ModelState.AddModelError(
                "start", "En fazla 366 günlük aralık seçin.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var fromUtc = DateTime.SpecifyKind(
            model.Start.ToDateTime(TimeOnly.MinValue).AddHours(-3),
            DateTimeKind.Utc);

        var untilUtc = DateTime.SpecifyKind(
            model.End.AddDays(1).ToDateTime(TimeOnly.MinValue).AddHours(-3),
            DateTimeKind.Utc);

        var buckets = new Dictionary<DateOnly, ReportBucketViewModel>();

        // Boş günler de grafikte gösterilir.
        for (var number = model.Start.DayNumber;
             number <= model.End.DayNumber;
             number++)
        {
            var date = DateOnly.FromDayNumber(number);
            var key = BucketStart(date, group);

            if (!buckets.ContainsKey(key))
            {
                buckets.Add(key, new ReportBucketViewModel
                {
                    Start = key,
                    Label = BucketLabel(key, group)
                });
            }
        }

        var movements = await db.StockMovements
            .AsNoTracking()
            .Where(m =>
                m.CreatedAtUtc >= fromUtc &&
                m.CreatedAtUtc < untilUtc)
            .OrderBy(m => m.CreatedAtUtc)
            .Select(m => new
            {
                m.Type,
                m.BoxCount,
                m.LooseUnitCount,
                m.UnitsPerBox,
                m.CreatedAtUtc,
                ProductId = m.PalletItem.ProductId,
                ProductName = m.PalletItem.Product.Name,
                ProductCode = m.PalletItem.Product.Code
            })
            .ToListAsync();

        model.MovementCount = movements.Count;

        var products = new Dictionary<int, ReportProductViewModel>();

        foreach (var movement in movements)
        {
            var localDate = DateOnly.FromDateTime(
                movement.CreatedAtUtc.AddHours(3));

            var bucket = buckets[BucketStart(localDate, group)];

            ReportQuantity? total = null;
            ReportQuantity? period = null;

            switch (movement.Type)
            {
                case StockMovementType.Receipt:
                    total = model.Receipts;
                    period = bucket.Receipts;
                    break;

                case StockMovementType.Shipment:
                    total = model.Shipments;
                    period = bucket.Shipments;
                    break;

                case StockMovementType.Return:
                    total = model.Returns;
                    period = bucket.Returns;
                    break;

                case StockMovementType.Damage:
                    total = model.Damage;
                    period = bucket.Damage;
                    break;

                case StockMovementType.ReceiptCancellation:
                    total = model.ReceiptCancellations;
                    period = bucket.ReceiptCancellations;
                    break;

                case StockMovementType.ShipmentCancellation:
                    total = model.ShipmentCancellations;
                    period = bucket.ShipmentCancellations;
                    break;
            }

            total?.Add(
                movement.BoxCount,
                movement.LooseUnitCount,
                movement.UnitsPerBox);

            period?.Add(
                movement.BoxCount,
                movement.LooseUnitCount,
                movement.UnitsPerBox);

            if (movement.Type == StockMovementType.Shipment)
            {
                if (!products.TryGetValue(
                    movement.ProductId, out var product))
                {
                    product = new ReportProductViewModel
                    {
                        ProductId = movement.ProductId,
                        Name = movement.ProductName,
                        Code = movement.ProductCode
                    };

                    products.Add(product.ProductId, product);
                }

                product.Quantity.Add(
                    movement.BoxCount,
                    movement.LooseUnitCount,
                    movement.UnitsPerBox);
            }
        }

        // Yalnızca Paketleme ekranından oluşturulan girişler.
        // Geri alınmış eski girişler sayılmaz.
        // Düzeltmeler, ilk paketleme işleminin gününe yazılır.
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
                movement.BoxCount,
                movement.LooseUnitCount,
                movement.UnitsPerBox,
                PreparedAtUtc = batch.CreatedAtUtc
            })
            .ToListAsync();

        foreach (var entry in prepared)
        {
            var date = DateOnly.FromDateTime(
                entry.PreparedAtUtc.AddHours(3));

            var bucket = buckets[BucketStart(date, group)];

            model.Prepared.Add(
                entry.BoxCount,
                entry.LooseUnitCount,
                entry.UnitsPerBox);

            bucket.Prepared.Add(
                entry.BoxCount,
                entry.LooseUnitCount,
                entry.UnitsPerBox);
        }

        model.Buckets = buckets.Values
            .OrderBy(b => b.Start)
            .ToList();

        model.UnrankedProductCount = products.Values
            .Count(p => !p.Quantity.IsComplete);

        model.TopProducts = products.Values
            .Where(p => p.Quantity.IsComplete)
            .OrderByDescending(p => p.Quantity.KnownUnits)
            .ThenBy(p => p.Name)
            .Take(10)
            .ToList();

        var currentStocks = await db.PalletItems
            .AsNoTracking()
            .Where(i =>
                i.Pallet.IsActive &&
                !i.Pallet.IsCancelled &&
                i.Product.IsActive)
            .Select(i => new
            {
                ProductId = i.Product.Id,
                ProductName = i.Product.Name,
                ProductCode = i.Product.Code,
                i.BoxCount,
                i.LooseUnitCount,
                i.OpenBoxUnitCount,
                i.UnitsPerBox
            })
            .ToListAsync();

        foreach (var stock in currentStocks)
        {
            model.CurrentStock.Add(
                stock.BoxCount,
                stock.LooseUnitCount,
                stock.UnitsPerBox);

            model.CurrentStock.Add(
                0, stock.OpenBoxUnitCount, null);
        }

        foreach (var stockGroup in currentStocks
            .GroupBy(s => new
            {
                s.ProductId,
                s.ProductName,
                s.ProductCode
            })
            .OrderBy(g => g.Key.ProductName))
        {
            var product = new ReportProductViewModel
            {
                ProductId = stockGroup.Key.ProductId,
                Name = stockGroup.Key.ProductName,
                Code = stockGroup.Key.ProductCode
            };

            foreach (var stock in stockGroup)
            {
                product.Quantity.Add(
                    stock.BoxCount,
                    stock.LooseUnitCount,
                    stock.UnitsPerBox);

                product.Quantity.Add(
                    0,
                    stock.OpenBoxUnitCount,
                    null);
            }

            model.CurrentStockProducts.Add(product);
        }


        return View(model);
    }

    private static DateOnly BucketStart(DateOnly date, string group)
    {
        if (group == "month")
        {
            return new DateOnly(date.Year, date.Month, 1);
        }

        if (group == "week")
        {
            var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
            return date.AddDays(-daysSinceMonday);
        }

        return date;
    }

    private static string BucketLabel(DateOnly date, string group)
    {
        return group switch
        {
            "month" => date.ToString("MM.yyyy"),
            "week" => date.ToString("dd.MM.yyyy") + " haftası",
            _ => date.ToString("dd.MM.yyyy")
        };
    }
}