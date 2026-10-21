using System.Security.Claims;
using MasterChefTakip.Models;
using MasterChefTakip.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

public partial class ShipmentsController
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissions.ShipmentsManage)]
    public async Task<IActionResult> SaveAndConfirm(
        [Bind(Prefix = "Batch")] BatchShipmentViewModel model,
        bool confirmStock = true)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
            return Challenge();

        if (model.Rows is null ||
            model.Rows.Count == 0 ||
            model.Rows.Count > 100)
        {
            ModelState.AddModelError(
                string.Empty,
                "1 ile 100 arasında ürün satırı girin.");
        }

        // Gönderim kolilerinin içeriği gerçek ürün adediyle girilir.
       if (model.Rows?.Any(row =>
    row.QuantityType != 1 && row.QuantityType != 2) == true)
{
    ModelState.AddModelError(
        string.Empty,
        "Geçersiz koli türü seçildi.");
}

        if (model.Rows?.GroupBy(row => row.PackageNumber)
            .Any(group => group.Select(row => row.QuantityType)
                .Distinct().Count() > 1) == true)
        {
            ModelState.AddModelError(
                string.Empty,
                "Koli türleri aynı koli numarasında karıştırılamaz.");
        }

        if (model.Rows?.GroupBy(row => row.PackageNumber)
            .Any(group => group.First().QuantityType == 2
                && group.Count() != 1) == true)
        {
            ModelState.AddModelError(
                string.Empty,
                "Kapalı koli numarasında yalnızca bir ürün bulunabilir.");
        }
        if (!ModelState.IsValid)
        {
            return await BatchError(
                model,
                "Koli, ürün ve ürün adetlerini kontrol edin.");
        }

        var rows = model.Rows!;

        // SALE_CODE_VALIDATION_V1
        // Sales codes are verified against the physical pallet product.
        var rowStockIds = rows
            .Select(row => row.PalletItemId)
            .Distinct()
            .ToArray();

        var availableSales = await (
            from palletItem in db.PalletItems
            join sale in db.ProductSalesOptions
                on palletItem.ProductId equals sale.ProductId
            where rowStockIds.Contains(palletItem.Id)
            select new
            {
                PalletItemId = palletItem.Id,
                sale.SalesCode,
                sale.UnitsPerSale,
                sale.IsActive
            }
        ).ToListAsync();

        foreach (var row in rows)
        {
            // SALES_CLOSED_BOX_COMPAT_V1
            // Kapali koli mevcut fiziksel koli hesabi ile calisir.
            // Satis paketi adedi kontrolu yalnizca adetli satirlara uygulanir.
            if (row.QuantityType == 2)
            {
                // Kapali koli fiziksel stok hesabiyla islenir.
                // Satis kodu verilmis ise urun ve aktiflik kontrol edilir.
                if (!string.IsNullOrWhiteSpace(row.SalesCode))
                {
                    var closedBoxSale = availableSales.FirstOrDefault(option =>
                        option.PalletItemId == row.PalletItemId &&
                        string.Equals(
                            option.SalesCode,
                            row.SalesCode,
                            StringComparison.OrdinalIgnoreCase));

                    if (closedBoxSale is null || !closedBoxSale.IsActive)
                    {
                        ModelState.AddModelError(
                            string.Empty,
                            "Kapali koli icin gecersiz veya pasif satis kodu.");
                    }
                }

                continue;
            }

            var options = availableSales
                .Where(option =>
                    option.PalletItemId == row.PalletItemId)
                .ToList();

            if (options.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(row.SalesCode))
                {
                    ModelState.AddModelError(
                        string.Empty,
                        "Bu stok için geçersiz satış kodu gönderildi.");
                }

                continue;
            }

            var selected = options.FirstOrDefault(option =>
                string.Equals(
                    option.SalesCode,
                    row.SalesCode,
                    StringComparison.OrdinalIgnoreCase));

            if (selected is null || !selected.IsActive)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Ürünün geçerli satış seçeneğini seçin.");
                continue;
            }

            if (selected.UnitsPerSale <= 0 ||
                row.Quantity % selected.UnitsPerSale != 0)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Ürün adedi seçilen satış paketinin katı olmalıdır.");
            }
        }

        // A single physical stock item cannot have two different
        // sales codes within the same shipment package.
        if (rows
            .GroupBy(row => new
            {
                row.PackageNumber,
                row.PalletItemId
            })
            .Any(group => group
                .Select(row => row.SalesCode ?? "")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() > 1))
        {
            ModelState.AddModelError(
                string.Empty,
                "Aynı kolide aynı stok için farklı satış kodları kullanılamaz.");
        }

        if (!ModelState.IsValid)
        {
            return await BatchError(
                model,
                "Satış kodu ve ürün miktarlarını kontrol edin.");
        }


        try
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync();

            var shipment = await db.Shipments
                .Include(s => s.Items)
                .Include(s => s.Packages)
                    .ThenInclude(package => package.Items)
                .SingleOrDefaultAsync(
                    s => s.Id == model.ShipmentId);

            if (shipment is null)
                return NotFound();

            if (shipment.Status != ShipmentStatus.Draft)
            {
                return Failure(
                    shipment.Id,
                    "Sevkiyat artık taslak değil. Stok tekrar düşülmedi.");
            }

            if (!string.Equals(
                shipment.ConcurrencyStamp,
                model.ConcurrencyStamp,
                StringComparison.Ordinal))
            {
                return Failure(
                    shipment.Id,
                    "Taslak değişti. Sayfayı yenileyip kontrol edin.");
            }

            // Aynı stok farklı kolilere dağıtılabilir.
          var requested = rows
    .GroupBy(row => row.PalletItemId)
    .Select(group => new
    {
        StockId = group.Key,
        Boxes = group
            .Where(row => row.QuantityType == 2)
            .Sum(row => (long)row.Quantity),
        Units = group
            .Where(row => row.QuantityType == 1)
            .Sum(row => (long)row.Quantity)
    })
    .ToList();

            if (requested.Any(request => request.Units > int.MaxValue || request.Boxes > int.MaxValue))
            {
                return await BatchError(
                    model,
                    "Bir üründen istenen toplam miktar sistem sınırını aşıyor.");
            }

            var orderError = await ValidateOrderContentsAsync(
                shipment,
                requested.Select(request => (
                    StockId: request.StockId,
                    Boxes: request.Boxes, Units: request.Units)));

            if (orderError is not null)
                return await BatchError(model, orderError);

            var stockIds = requested
                .Select(request => request.StockId)
                .ToList();

            var stocks = await db.PalletItems
                .Include(stock => stock.Pallet)
                .Include(stock => stock.Product)
                .Where(stock => stockIds.Contains(stock.Id))
                .ToDictionaryAsync(stock => stock.Id);

            var plans = new List<(
                PalletItem Stock,
                int Boxes,
                int Units,
                int RemainingBoxes,
                int RemainingLoose,
                int RemainingOpen,
                int? RemainingCapacity,
                int OpenedBoxes)>();

            // Önce bütün ürünler kontrol edilir.
            // Bir üründe sorun varsa hiçbir stok değiştirilmez.
            foreach (var request in requested)
            {
                if (!stocks.TryGetValue(request.StockId, out var stock))
                {
                    return await BatchError(
                        model,
                        "Seçilen palet içeriği bulunamadı.");
                }

                if (!stock.Product.IsActive ||
                    !stock.Pallet.IsActive ||
                    stock.Pallet.IsCancelled)
                {
                    return await BatchError(
                        model,
                        $"{stock.Product.Name}: Ürün veya palet aktif değil.");
                }

                var boxes = (int)request.Boxes;
                var units = (int)request.Units;

                var balanceOk = TryCalculateBalance(
                    stock,
                    boxes,
                    units,
                    out var remainingBoxes,
                    out var remainingLoose,
                    out var remainingOpen,
                    out var remainingCapacity,
                    out var openedBoxes,
                    out var error);
                if (!balanceOk && confirmStock)
                {
                    return await BatchError(
                        model,
                        $"{stock.Product.Name}: {error}");
                }

                plans.Add((
                    stock,
                    boxes,
                    units,
                    remainingBoxes,
                    remainingLoose,
                    remainingOpen,
                    remainingCapacity,
                    openedBoxes));
            }

            var existingLines = shipment.Items
                .ToDictionary(item => item.PalletItemId);

            var now = DateTime.UtcNow;

            // Eski taslak koli dağılımı yeni formdaki içerikle değiştirilir.
            db.ShipmentPackageItems.RemoveRange(
                shipment.Packages.SelectMany(package => package.Items));

            db.ShipmentPackages.RemoveRange(shipment.Packages);

            shipment.Packages.Clear();

            await db.SaveChangesAsync();

            foreach (var plan in plans)
            {
                var stock = plan.Stock;

                if (!existingLines.TryGetValue(stock.Id, out var line))
                {
                    line = new ShipmentItem
                    {
                        PalletItemId = stock.Id
                    };

                    shipment.Items.Add(line);
                    existingLines.Add(stock.Id, line);
                }

                // Stok çıkışı ürün adedi olarak saklanır.
                // Gönderim kolileri ayrı kayıtlarda tutulur.
                line.BoxCount = plan.Boxes;
                line.LooseUnitCount = plan.Units;
                line.UnitsPerBox = stock.UnitsPerBox;

                line.ProductName = stock.Product.Name;
                line.ProductCode = stock.Product.Code;
                line.PalletBarcode = stock.Pallet.Barcode;

                if (confirmStock)
                {
// İptal durumunda önceki stok dağılımı geri alınabilir.
                line.HasStockSnapshot = true;
                line.BeforeBoxCount = stock.BoxCount;
                line.BeforeLooseUnitCount = stock.LooseUnitCount;
                line.BeforeOpenBoxUnitCount = stock.OpenBoxUnitCount;
                line.BeforeOpenBoxCapacity = stock.OpenBoxCapacity;

                stock.BoxCount = plan.RemainingBoxes;
                stock.LooseUnitCount = plan.RemainingLoose;
                stock.OpenBoxUnitCount = plan.RemainingOpen;
                stock.OpenBoxCapacity = plan.RemainingCapacity;
                stock.ConcurrencyStamp = Guid.NewGuid().ToString("N");

                line.AfterStockStamp = stock.ConcurrencyStamp;

                db.StockMovements.Add(new StockMovement
                {
                    RelatedShipmentId = shipment.Id,
                    PalletItemId = stock.Id,
                    Type = StockMovementType.Shipment,
                    BoxCount = plan.Boxes,
                    LooseUnitCount = plan.Units,
                    UnitsPerBox = stock.UnitsPerBox,
                    CreatedByUserId = userId,
                    CreatedAtUtc = now,
                    Notes =
                        $"Sevkiyat: {shipment.Number}. "
                        + $"Gönderilen: {plan.Boxes} kapalı koli, {plan.Units} adet. "
                        + $"Açılan depo kolisi: {plan.OpenedBoxes}. "
                        + $"Kalan: {plan.RemainingBoxes} kapalı koli, "
                        + $"açık kolide {plan.RemainingOpen} adet, "
                        + $"{plan.RemainingLoose} bağımsız tekli adet."
                });
                }
                else
                {
                    line.HasStockSnapshot = false;
                    line.BeforeBoxCount = 0;
                    line.BeforeLooseUnitCount = 0;
                    line.BeforeOpenBoxUnitCount = 0;
                    line.BeforeOpenBoxCapacity = null;
                    line.AfterStockStamp = null;
                }
            }

            // Kapalı koli ve karma koli kayıtları birbirinden ayrılır.
            // Kapalı kolide Quantity = 1, yani bir fiziksel koli.
            var nextPackageNumber = rows.Max(row => row.PackageNumber);

            foreach (var packageGroup in rows
                .GroupBy(row => row.PackageNumber)
                .OrderBy(group => group.Key))
            {
                var first = packageGroup.First();

                if (first.QuantityType == 2)
                {
                    // Bir satırdaki 5 kapalı koli, 5 fiziksel koli kaydıdır.
                    for (var boxIndex = 0; boxIndex < first.Quantity; boxIndex++)
                    {
                        var packageNumber = boxIndex == 0
                            ? packageGroup.Key
                            : checked(++nextPackageNumber);

                        var closedPackage = new ShipmentPackage
                        {
                            Number = packageNumber
                        };

                        closedPackage.Items.Add(new ShipmentPackageItem
                        {
                            ShipmentItem = existingLines[first.PalletItemId],
                            Quantity = 1,
                            QuantityType = 2,
                            SalesCode = first.SalesCode
                        });

                        shipment.Packages.Add(closedPackage);
                    }
                }
                else
                {
                    var mixedPackage = new ShipmentPackage
                    {
                        Number = packageGroup.Key
                    };

                    foreach (var productGroup in packageGroup
                        .GroupBy(row => row.PalletItemId))
                    {
                        mixedPackage.Items.Add(new ShipmentPackageItem
                        {
                            ShipmentItem = existingLines[productGroup.Key],
                            Quantity = checked((int)productGroup.Sum(
                                row => (long)row.Quantity)),
                            QuantityType = 1,
                            SalesCode = productGroup.First().SalesCode
                        });
                    }

                    shipment.Packages.Add(mixedPackage);
                }
            }
            // Yeni formda bulunmayan eski taslak ürünleri kaldır.
            var omitted = shipment.Items
                .Where(item => !stockIds.Contains(item.PalletItemId))
                .ToList();

            db.ShipmentItems.RemoveRange(omitted);

            if (confirmStock)
            {
                shipment.Status = ShipmentStatus.Confirmed;
                shipment.ConfirmedAtUtc = now;
                shipment.ConfirmedByUserId = userId;
            }
            shipment.ConcurrencyStamp = Guid.NewGuid().ToString("N");

            // Koli içerikleri, stok ve geçmiş aynı işlemde kaydedilir.
            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            var totalUnits = plans.Sum(plan =>
                (long)plan.Boxes * (plan.Stock.UnitsPerBox ?? 0)
                + plan.Units);

            TempData["ShipmentSuccess"] = confirmStock
                ? $"{shipment.Packages.Count} koli, "
                    + $"{totalUnits} adet ürün kaydedildi. "
                    + "Ürünler stoktan düşüldü. Sevkiyat gönderime hazır."
                : $"Taslak kaydedildi: {shipment.Packages.Count} koli, "
                    + $"{totalUnits} adet ürün. "
                    + "Stok değişmedi. Daha sonra açıp devam edebilirsiniz.";

            return RedirectToAction(
                nameof(Index),
                new { shipmentId = shipment.Id });
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogWarning(
                exception,
                "Sevkiyat sırasında stok veya taslak değişti.");

            return await BatchError(
                model,
                "Stok veya taslak başka bir işlemde değişti. "
                + "Sayfayı yenileyip kontrol edin.");
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Koli içerikleri ve sevkiyat kaydedilemedi.");

            return await BatchError(
                model,
                "Sevkiyat kaydedilemedi. Stok değişiklikleri geri alındı.");
        }
    }

    private async Task<IActionResult> BatchError(
        BatchShipmentViewModel form,
        string message)
    {
        db.ChangeTracker.Clear();

        ModelState.AddModelError(string.Empty, message);
        ViewData["BatchForm"] = form;

        return View(
            "Index",
            await BuildPageAsync(form.ShipmentId));
    }
}