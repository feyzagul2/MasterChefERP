using System.Security.Claims;
using MasterChefTakip.Data;
using MasterChefTakip.Models;
using MasterChefTakip.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

[Authorize(Policy = AppPermissions.ReturnsView)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public partial class ReturnsController(
    ApplicationDbContext db,
    ILogger<ReturnsController> logger) : Controller
{
    private const string InspectionRoles =
        AppRoles.Admin + "," +
        AppRoles.Packaging + "," +
        AppRoles.Control;

    private bool CanInspect =>
    MasterChefTakip.Services.OperationAuthorization.Can(
        User,
        AppPermissions.ReturnsInspect);

    private string? CurrentUserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet]
    public async Task<IActionResult> Index(
        string? search = null,
        ReturnReceiptStatus? statusFilter = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        int page = 1)
    {
        if (startDate.HasValue &&
            endDate.HasValue &&
            startDate.Value > endDate.Value)
        {
            ModelState.AddModelError(
                string.Empty,
                "Başlangıç tarihi bitiş tarihinden sonra olamaz.");
        }

        return View(await BuildPageAsync(
            search, statusFilter, startDate, endDate, page));
    }

    // Bir sevkiyata bağlı, birden fazla ürün içeren iade.
    // Bu aşamada kullanılabilir stok değişmez.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissions.ReturnsReceive)]
    public async Task<IActionResult> CreateReceipt(
        [Bind(Prefix = "ReceiptForm")]
        CreateReturnReceiptViewModel model)
    {
        var userId = CurrentUserId;

        if (string.IsNullOrWhiteSpace(userId))
            return Challenge();

        if (!ValidRequestId(model.RequestId))
        {
            ModelState.AddModelError(
                "ReceiptForm.RequestId",
                "Form kimliği geçersiz. Sayfayı yenileyin.");
        }

        if (string.IsNullOrWhiteSpace(model.Reason))
        {
            ModelState.AddModelError(
                "ReceiptForm.Reason",
                "İade nedenini girin.");
        }

        if (model.Rows is null ||
            model.Rows.Count == 0 ||
            model.Rows.Count > 100)
        {
            ModelState.AddModelError(
                "ReceiptForm.Rows",
                "1 ile 100 arasında ürün satırı girin.");
        }

        if (model.Rows is null || !ModelState.IsValid)
        {
        return await FormErrorAsync(receipt: model);
        }

        try
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync();

            var existing = await db.ReturnReceipts
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    r => r.RequestId == model.RequestId);

            if (existing is not null)
                return Success("Bu iade daha önce kaydedilmiş.");

            var shipment = await db.Shipments
                .Include(s => s.Items)
                    .ThenInclude(i => i.PalletItem)
                .SingleOrDefaultAsync(
                    s => s.Id == model.ShipmentId);

            if (shipment is null ||
                shipment.Status is not (ShipmentStatus.Confirmed or ShipmentStatus.Shipped or ShipmentStatus.Delivered))
            {
                return await FormErrorAsync(
                    "Gönderime hazır, gönderilmiş veya teslim edilmiş bir sevkiyat seçin.",
                    receipt: model);
            }

            var lines = shipment.Items.ToDictionary(i => i.Id);

            // Aynı sevkiyat satırı formda tekrar seçilirse
            // toplam miktar birleştirilir.
            var requested = new Dictionary<int, long>();

            for (var index = 0; index < model.Rows.Count; index++)
            {
                var row = model.Rows[index];
                var prefix = $"ReceiptForm.Rows[{index}]";

                if (!lines.TryGetValue(
                    row.ShipmentItemId, out var line))
                {
                    ModelState.AddModelError(
                        $"{prefix}.ShipmentItemId",
                        "Ürün seçilen sevkiyata ait değil.");

                    continue;
                }

                if (row.BoxCount > 0)
                {
                    if (row.UnitsPerBox is not > 0)
                    {
                        ModelState.AddModelError(
                            $"{prefix}.UnitsPerBox",
                            "Tam koli için koli içi adedi girin.");

                        continue;
                    }

                    // Bilinen sevkiyat kapasitesini değiştirmeyiz.
                    if (line.UnitsPerBox is > 0 &&
                        row.UnitsPerBox != line.UnitsPerBox)
                    {
                        ModelState.AddModelError(
                            $"{prefix}.UnitsPerBox",
                            $"Bu sevkiyatın koli içi adedi "
                            + $"{line.UnitsPerBox}. Eksik veya karışık "
                            + "kolinin içeriğini ürün adedi olarak girin.");

                        continue;
                    }
                }

                var total =
                    (long)row.BoxCount * (row.UnitsPerBox ?? 0)
                    + row.AdditionalUnitCount;

                if (total <= 0 || total > int.MaxValue)
                {
                    ModelState.AddModelError(
                        $"{prefix}.AdditionalUnitCount",
                        "Pozitif ve geçerli bir ürün miktarı girin.");

                    continue;
                }

                requested.TryGetValue(line.Id, out var previous);
                requested[line.Id] = checked(previous + total);
            }

            if (!ModelState.IsValid)
                return await FormErrorAsync(receipt: model);

            var requestedIds = requested.Keys.ToList();

            var previousReturns = await db.ReturnInspections
                .Where(r =>
                    requestedIds.Contains(r.ShipmentItemId) &&
                    r.Status != ReturnInspectionStatus.Cancelled)
                .Select(r => new
                {
                    r.ShipmentItemId,
                    r.ReceivedUnits
                })
                .ToListAsync();

            foreach (var request in requested)
            {
                var line = lines[request.Key];

                if (line.BoxCount > 0 &&
                    line.UnitsPerBox is not > 0)
                {
                    return await FormErrorAsync(
                        $"{line.ProductName}: Sevkiyatın koli içi "
                        + "adedi bilinmediği için iade sınırı "
                        + "hesaplanamıyor. Sevkiyat bilgisi "
                        + "doğrulanmadan iade oluşturulmadı.",
                        receipt: model);
                }

                var shipped =
                    (long)line.BoxCount * (line.UnitsPerBox ?? 0)
                    + line.LooseUnitCount;

                var returned = previousReturns
                    .Where(r => r.ShipmentItemId == line.Id)
                    .Sum(r => (long)r.ReceivedUnits);

                var remaining = Math.Max(0L, shipped - returned);

                if (request.Value > remaining ||
                    request.Value > int.MaxValue)
                {
                    return await FormErrorAsync(
                        $"{line.ProductName}: Bu sevkiyat satırında "
                        + $"iade edilebilecek kalan miktar "
                        + $"{remaining} adet.",
                        receipt: model);
                }
            }

            var now = DateTime.UtcNow;

            var receipt = new ReturnReceipt
            {
                RequestId = model.RequestId,
                ShipmentId = shipment.Id,
                PackageCount = model.PackageCount,
                Reason = model.Reason.Trim(),
                CreatedByUserId = userId,
                CreatedAtUtc = now
            };

            foreach (var request in requested)
            {
                var line = lines[request.Key];

                var rows = model.Rows
                    .Where(r => r.ShipmentItemId == line.Id)
                    .ToList();

                var boxes = rows.Sum(r => (long)r.BoxCount);
                var capacities = rows
                    .Where(r => r.BoxCount > 0)
                    .Select(r => r.UnitsPerBox)
                    .Distinct()
                    .ToList();

                var canKeepBoxBreakdown =
                    boxes <= int.MaxValue &&
                    capacities.Count <= 1;

                receipt.Items.Add(new ReturnInspection
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    ShipmentItemId = line.Id,
                    ReceivedUnits = (int)request.Value,
                    UnitsPerBox = line.UnitsPerBox,

                    ReceivedBoxCount = canKeepBoxBreakdown
                        ? (int)boxes
                        : 0,

                    ReceivedUnitsPerBox =
                        canKeepBoxBreakdown && boxes > 0
                            ? capacities[0]
                            : null,

                    ReceivedAdditionalUnitCount =
                        canKeepBoxBreakdown
                            ? (int)rows.Sum(
                                r => (long)r.AdditionalUnitCount)
                            : (int)request.Value,

                    Reason = receipt.Reason,
                    CreatedByUserId = userId,
                    CreatedAtUtc = now
                });
            }

            db.ReturnReceipts.Add(receipt);

            // Aynı sevkiyata eşzamanlı iade/iptal kontrolü.
            shipment.ConcurrencyStamp =
                Guid.NewGuid().ToString("N");

            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            return Success(
                "İade ve ürünleri kaydedildi. "
                + "Kontrol bekliyor; kullanılabilir stok değişmedi.");
        }
        catch (Exception exception)
        {
            LogFailure(exception, "İade kaydı oluşturulamadı.");

            return await FormErrorAsync(
                "İade kaydedilemedi. Sayfayı yenileyip "
                + "sevkiyat miktarlarını kontrol edin.",
                receipt: model);
        }
    }

    // Kontrol taslağı kaydedilebilir.
    // Kontrol tamamlanınca sağlam ürünler yerleştirme bekler.
  // Kontrol ve stok girişi aynı işlemde tamamlanır.
[HttpPost]
[ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissions.ReturnsInspect)]
public async Task<IActionResult> InspectItem(
    [Bind(Prefix = "InspectionForm")]
    InspectReturnItemViewModel model)
{
    var userId = CurrentUserId;

    if (string.IsNullOrWhiteSpace(userId))
        return Challenge();

    if (!ModelState.IsValid)
        return await FormErrorAsync(inspection: model);

    try
    {
        await using var transaction =
            await db.Database.BeginTransactionAsync();

        var item = await db.ReturnInspections
            .Include(i => i.Placements)
            .Include(i => i.ShipmentItem)
                .ThenInclude(s => s.Shipment)
            .Include(i => i.ShipmentItem)
                .ThenInclude(s => s.PalletItem)
            .SingleOrDefaultAsync(i => i.Id == model.Id);

        if (item is null)
            return NotFound();

        // Tekrar gönderilirse stok ikinci kez eklenmez.
        if (item.Status == ReturnInspectionStatus.Completed)
        {
            return Success(
                "Bu ürünün iadesi zaten tamamlandı. "
                + "Stok tekrar eklenmedi.");
        }

        var canComplete =
            item.Status == ReturnInspectionStatus.Waiting ||
            item.Status == ReturnInspectionStatus.Inspecting ||
            item.Status == ReturnInspectionStatus.WaitingForPlacement;

        if (!canComplete)
        {
            return await FormErrorAsync(
                "Bu iade işleme uygun değil.",
                inspection: model);
        }

        if (item.Placements.Count > 0)
        {
            return await FormErrorAsync(
                "Bu kayıt için stok girişi mevcut. "
                + "İşlem tekrarlanmadı.",
                inspection: model);
        }

        if (!StampMatches(
            item.ConcurrencyStamp,
            model.ConcurrencyStamp))
        {
            return await FormErrorAsync(
                "Kayıt değişti. Sayfayı yenileyip kontrol edin.",
                inspection: model);
        }

        if (item.ShipmentItem.Shipment.Status
            is not (ShipmentStatus.Confirmed or ShipmentStatus.Shipped or ShipmentStatus.Delivered))
        {
            return await FormErrorAsync(
                "Bağlı sevkiyat iade işlemi için uygun değil.",
                inspection: model);
        }

        var good = model.GoodUnits!.Value;
        var damaged = model.DamagedUnits!.Value;

        if ((long)good + damaged != item.ReceivedUnits)
        {
            ModelState.AddModelError(
                "InspectionForm.DamagedUnits",
                $"Sağlam ve hasarlı toplamı geri gelen "
                + $"{item.ReceivedUnits} ürüne eşit olmalı.");
        }

        var productId =
            item.ShipmentItem.PalletItem.ProductId;

        if (good > 0)
        {
            var validPallet =
                model.DestinationPalletId.HasValue &&
                await db.Pallets.AnyAsync(p =>
                    p.Id == model.DestinationPalletId.Value &&
                    p.IsActive &&
                    !p.IsCancelled);

            if (!validPallet)
            {
                ModelState.AddModelError(
                    "InspectionForm.DestinationPalletId",
                    "Sağlam ürünler için aktif bir palet seçin.");
            }

            var activeProduct = await db.Products.AnyAsync(p =>
                p.Id == productId &&
                p.IsActive);

            if (!activeProduct)
            {
                ModelState.AddModelError(
                    "InspectionForm.GoodUnits",
                    "Ürün pasif. Stoğa eklemek için önce aktif edin.");
            }
        }

        if (model.GoodBoxCount > 0 &&
            model.PlacementUnitsPerBox is not > 0)
        {
            ModelState.AddModelError(
                "InspectionForm.PlacementUnitsPerBox",
                "Kapalı koli için koli içi ürün adedini girin.");
        }

        var boxedUnits =
            (long)model.GoodBoxCount *
            (model.PlacementUnitsPerBox ?? 0);

        if (boxedUnits > good ||
            (good == 0 && model.GoodBoxCount > 0))
        {
            ModelState.AddModelError(
                "InspectionForm.GoodBoxCount",
                "Kapalı kolilerdeki ürün miktarı "
                + "sağlam miktarı aşamaz.");
        }

        if (!ModelState.IsValid)
            return await FormErrorAsync(inspection: model);

        var now = DateTime.UtcNow;
        var loose = good - (int)boxedUnits;
        var notes = model.InspectionNotes?.Trim();

        // Yalnızca sağlam ürünler stok oluşturur.
        if (good > 0)
        {
            var stock = new PalletItem
            {
                ProductId = productId,
                PalletId = model.DestinationPalletId!.Value,

                BoxCount = model.GoodBoxCount,

                UnitsPerBox = model.GoodBoxCount > 0
                    ? model.PlacementUnitsPerBox
                    : null,

                LooseUnitCount = loose,
                OpenBoxUnitCount = 0,
                OpenBoxCapacity = null,

                ConcurrencyStamp =
                    Guid.NewGuid().ToString("N")
            };

            db.PalletItems.Add(stock);

            db.ReturnPlacements.Add(new ReturnPlacement
            {
                RequestId = Guid.NewGuid().ToString("N"),
                ReturnInspectionId = item.Id,

                PalletId = stock.PalletId,
                PalletItem = stock,

                BoxCount = stock.BoxCount,
                UnitsPerBox = stock.UnitsPerBox,
                LooseUnitCount = loose,

                CreatedByUserId = userId,
                CreatedAtUtc = now,
                Notes = notes
            });

            db.StockMovements.Add(new StockMovement
            {
                PalletItem = stock,

                RelatedShipmentId =
                    item.ShipmentItem.ShipmentId,

                Type = StockMovementType.Return,

                BoxCount = stock.BoxCount,
                UnitsPerBox = stock.UnitsPerBox,
                LooseUnitCount = loose,

                CreatedByUserId = userId,
                CreatedAtUtc = now,

                Notes = LimitNote(
                    $"İade #{item.Id}: {good} sağlam ürün "
                    + "kontrolden geçip palete yerleştirildi. "
                    + $"{damaged} hasarlı ürün stoğa alınmadı. "
                    + notes)
            });
        }

        item.InspectedGoodUnits = good;
        item.DamagedUnitCount = damaged;

        // Önceki kayıtların toplam hesabını da korur.
        item.GoodBoxCount = item.ReturnReceiptId.HasValue
            ? model.GoodBoxCount
            : 0;

        item.GoodUnitCount = item.ReturnReceiptId.HasValue
            ? loose
            : good;

        item.DestinationPalletId = good > 0
            ? model.DestinationPalletId
            : null;

        item.InspectionNotes = notes;
        item.InspectedByUserId = userId;
        item.InspectedAtUtc = now;

        item.Status = ReturnInspectionStatus.Completed;

        item.ConcurrencyStamp =
            Guid.NewGuid().ToString("N");

        item.ShipmentItem.Shipment.ConcurrencyStamp =
            Guid.NewGuid().ToString("N");

        await db.SaveChangesAsync();

        if (item.ReturnReceiptId.HasValue)
        {
            await RefreshReceiptStatusAsync(
                item.ReturnReceiptId.Value,
                userId,
                now);

            await db.SaveChangesAsync();
        }

        await transaction.CommitAsync();

        return Success(good > 0
            ? $"İade ürünü tamamlandı: {good} sağlam ürün "
                + "seçilen paletin stokuna eklendi; "
                + $"{damaged} hasarlı ürün kaydedildi."
            : $"İade ürünü tamamlandı: {damaged} ürünün "
                + "tamamı hasarlı. Stok değişmedi.");
    }
    catch (Exception exception)
    {
        LogFailure(
            exception,
            "İade kontrolü ve stok girişi tamamlanamadı.");

        return await FormErrorAsync(
            "İşlem kaydedilemedi. Stok değişiklikleri "
            + "geri alındı. Sayfayı yenileyip tekrar deneyin.",
            inspection: model);
    }
}

    // Bir ürünün bütün sağlam miktarı, bir veya birden fazla
    // yerleştirme satırında birlikte onaylanır.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissions.ReturnsInspect)]
    public async Task<IActionResult> PlaceItems(
        [Bind(Prefix = "PlacementForm")]
        PlaceReturnItemsViewModel model)
    {
        var userId = CurrentUserId;

        if (string.IsNullOrWhiteSpace(userId))
            return Challenge();

        if (!ValidRequestId(model.RequestId))
        {
            ModelState.AddModelError(
                "PlacementForm.RequestId",
                "Form kimliği geçersiz. Sayfayı yenileyin.");
        }

        if (model.Rows is null ||
            model.Rows.Count == 0 ||
            model.Rows.Count > 100)
        {
            ModelState.AddModelError(
                "PlacementForm.Rows",
                "1 ile 100 arasında yerleştirme satırı girin.");
        }
        if (model.Rows is null || !ModelState.IsValid)
        {
            return await FormErrorAsync(placement: model);
        }

        try
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync();

            var previous = await db.ReturnPlacements
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    p => p.RequestId == model.RequestId);

            if (previous is not null)
            {
                if (previous.ReturnInspectionId
                    != model.ReturnInspectionId)
                {
                    return await FormErrorAsync(
                        "Form kimliği başka bir yerleştirmeye ait.",
                        placement: model);
                }

                return Success(
                    "Bu yerleştirme daha önce kaydedilmiş. "
                    + "Stok tekrar eklenmedi.");
            }

            var item = await db.ReturnInspections
                .Include(i => i.ShipmentItem)
                    .ThenInclude(i => i.Shipment)
                .Include(i => i.ShipmentItem)
                    .ThenInclude(i => i.PalletItem)
                .Include(i => i.Placements)
                .SingleOrDefaultAsync(
                    i => i.Id == model.ReturnInspectionId);

            if (item is null)
                return NotFound();

            if (item.Status
                != ReturnInspectionStatus.WaitingForPlacement ||
                item.Placements.Count > 0)
            {
                return await FormErrorAsync(
                    "Bu ürün yerleştirme beklemiyor. "
                    + "Stok tekrar eklenmedi.",
                    placement: model);
            }

            if (!StampMatches(
                item.ConcurrencyStamp, model.ConcurrencyStamp))
            {
                return await FormErrorAsync(
                    "Kontrol kaydı değişti. "
                    + "Sayfayı yenileyip kontrol edin.",
                    placement: model);
            }

            if (item.ShipmentItem.Shipment.Status
                is not (ShipmentStatus.Confirmed or ShipmentStatus.Shipped or ShipmentStatus.Delivered) ||
                item.UninspectedUnits != 0 ||
                item.InspectedGoodUnits <= 0)
            {
                return await FormErrorAsync(
                    "Sevkiyat veya kontrol bilgisi "
                    + "yerleştirme için uygun değil.",
                    placement: model);
            }

            var productId = item.ShipmentItem.PalletItem.ProductId;

            if (!await db.Products.AnyAsync(
                p => p.Id == productId && p.IsActive))
            {
                return await FormErrorAsync(
                    "Ürün pasif. Stoğa dönüş için ürünü aktif edin.",
                    placement: model);
            }

            var palletIds = model.Rows
                .Select(r => r.PalletId)
                .Distinct()
                .ToList();

            var activePallets = await db.Pallets
                .Where(p =>
                    palletIds.Contains(p.Id) &&
                    p.IsActive &&
                    !p.IsCancelled)
                .Select(p => p.Id)
                .ToListAsync();

            long placedTotal = 0;

            for (var index = 0; index < model.Rows.Count; index++)
            {
                var row = model.Rows[index];
                var prefix = $"PlacementForm.Rows[{index}]";

                if (!activePallets.Contains(row.PalletId))
                {
                    ModelState.AddModelError(
                        $"{prefix}.PalletId",
                        "Aktif bir hedef palet seçin.");
                }

                if (row.BoxCount > 0 &&
                    row.UnitsPerBox is not > 0)
                {
                    ModelState.AddModelError(
                        $"{prefix}.UnitsPerBox",
                        "Kapalı koli için koli içi adedi girin.");
                }

                var rowTotal =
                    (long)row.BoxCount * (row.UnitsPerBox ?? 0)
                    + row.LooseUnitCount;

                if (rowTotal <= 0 || rowTotal > int.MaxValue)
                {
                    ModelState.AddModelError(
                        $"{prefix}.LooseUnitCount",
                        "Pozitif ve geçerli yerleştirme miktarı girin.");
                }

                placedTotal = checked(placedTotal + rowTotal);
            }

            if (placedTotal != item.InspectedGoodUnits)
            {
                ModelState.AddModelError(
                    "PlacementForm.Rows",
                    $"Yerleştirme toplamı {item.InspectedGoodUnits} "
                    + "sağlam adede eşit olmalı.");
            }

           if (model.Rows is null || !ModelState.IsValid)
            {
                return await FormErrorAsync(placement: model);
            }

            var now = DateTime.UtcNow;
            var notes = model.Notes?.Trim();

            for (var index = 0; index < model.Rows.Count; index++)
            {
                var row = model.Rows[index];

                var stock = new PalletItem
                {
                    ProductId = productId,
                    PalletId = row.PalletId,
                    BoxCount = row.BoxCount,
                    UnitsPerBox = row.BoxCount > 0
                        ? row.UnitsPerBox
                        : null,
                    LooseUnitCount = row.LooseUnitCount,
                    OpenBoxUnitCount = 0,
                    OpenBoxCapacity = null,
                    ConcurrencyStamp =
                        Guid.NewGuid().ToString("N")
                };

                db.PalletItems.Add(stock);

                db.ReturnPlacements.Add(new ReturnPlacement
                {
                    RequestId = index == 0
                        ? model.RequestId
                        : Guid.NewGuid().ToString("N"),
                    ReturnInspectionId = item.Id,
                    PalletId = row.PalletId,
                    PalletItem = stock,
                    BoxCount = row.BoxCount,
                    UnitsPerBox = stock.UnitsPerBox,
                    LooseUnitCount = row.LooseUnitCount,
                    CreatedByUserId = userId,
                    CreatedAtUtc = now,
                    Notes = notes
                });

                db.StockMovements.Add(new StockMovement
                {
                    PalletItem = stock,
                    RelatedShipmentId =
                        item.ShipmentItem.ShipmentId,
                    Type = StockMovementType.Return,
                    BoxCount = row.BoxCount,
                    LooseUnitCount = row.LooseUnitCount,
                    UnitsPerBox = stock.UnitsPerBox,
                    CreatedByUserId = userId,
                    CreatedAtUtc = now,
                    Notes = LimitNote(
                        $"İade #{item.Id}: Kontrolden geçen "
                        + $"sağlam ürünler yerleştirildi. {notes}")
                });
            }

            // Eski kayıtların sonuç gösterimi için de korunur.
            item.GoodBoxCount = checked(
                (int)model.Rows.Sum(r => (long)r.BoxCount));

            item.GoodUnitCount = checked(
                (int)model.Rows.Sum(r => (long)r.LooseUnitCount));

            var boxCapacities = model.Rows
                .Where(r => r.BoxCount > 0)
                .Select(r => r.UnitsPerBox)
                .Distinct()
                .ToList();

            // Eski kayıtlar farklı kapasitelere yerleştirilirse
            // eski toplam alanını gerçek ürün adediyle tutarız.
            if (!item.ReturnReceiptId.HasValue)
            {
                item.GoodBoxCount = 0;
                item.GoodUnitCount = item.InspectedGoodUnits;
            }

            item.DestinationPalletId =
                palletIds.Count == 1 ? palletIds[0] : null;

            item.Status = ReturnInspectionStatus.Completed;
            item.ConcurrencyStamp =
                Guid.NewGuid().ToString("N");

            item.ShipmentItem.Shipment.ConcurrencyStamp =
                Guid.NewGuid().ToString("N");

            await db.SaveChangesAsync();

            if (item.ReturnReceiptId.HasValue)
            {
                await RefreshReceiptStatusAsync(
                    item.ReturnReceiptId.Value, userId, now);

                await db.SaveChangesAsync();
            }

            await transaction.CommitAsync();

            return Success(
                $"{placedTotal} sağlam adet paletlere yerleştirildi "
                + "ve kullanılabilir stoğa eklendi. "
                + "Hasarlı iadeler stoktan tekrar düşülmedi.");
        }
        catch (Exception exception)
        {
            LogFailure(exception, "İade yerleştirmesi kaydedilemedi.");

            return await FormErrorAsync(
                "Yerleştirme kaydedilemedi. "
                + "Stok değişiklikleri geri alındı.",
                placement: model);
        }
    }

    // Stok yerleştirmesi yapılmamış iade başlığı iptal edilebilir.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissions.ReturnsCancel)]
    public async Task<IActionResult> CancelReceipt(
        CancelReturnReceiptViewModel model)
    {
        var userId = CurrentUserId;

        if (string.IsNullOrWhiteSpace(userId))
            return Challenge();

        if (!ModelState.IsValid ||
            string.IsNullOrWhiteSpace(model.Reason))
        {
            return Failure("İptal gerekçesini girin.");
        }

        try
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync();

            var receipt = await db.ReturnReceipts
                .Include(r => r.Shipment)
                .Include(r => r.Items)
                    .ThenInclude(i => i.Placements)
                .SingleOrDefaultAsync(r => r.Id == model.Id);

            if (receipt is null)
                return NotFound();

            if (receipt.Status == ReturnReceiptStatus.Cancelled)
                return Success("Bu iade zaten iptal edilmiş.");

            if (!StampMatches(
                receipt.ConcurrencyStamp, model.ConcurrencyStamp))
            {
                return Failure(
                    "İade değişti. Sayfayı yenileyip kontrol edin.");
            }

            if (receipt.Status == ReturnReceiptStatus.Completed ||
                receipt.Items.Any(i => i.Placements.Count > 0))
            {
                return Failure(
                    "Tamamlanmış veya stoğa yerleştirilmiş iade "
                    + "bu işlemle iptal edilemez.");
            }

            var now = DateTime.UtcNow;

            receipt.Status = ReturnReceiptStatus.Cancelled;
            receipt.CancelledByUserId = userId;
            receipt.CancelledAtUtc = now;
            receipt.CancellationReason = model.Reason.Trim();
            receipt.ConcurrencyStamp =
                Guid.NewGuid().ToString("N");

            foreach (var item in receipt.Items)
            {
                item.Status = ReturnInspectionStatus.Cancelled;
                item.ConcurrencyStamp =
                    Guid.NewGuid().ToString("N");
            }

            receipt.Shipment.ConcurrencyStamp =
                Guid.NewGuid().ToString("N");

            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            return Success("İade iptal edildi. Stok değişmedi.");
        }
        catch (Exception exception)
        {
            LogFailure(exception, "İade iptal edilemedi.");
            db.ChangeTracker.Clear();

            return Failure(
                "İade iptal edilemedi. İşlem geri alındı.");
        }
    }

    // Depoda oluşan hasar mevcut stoktan düşülür.
    // Müşteriden hasarlı gelen iadeler bu metoda gönderilmez.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissions.ReturnsDamage)]
    public async Task<IActionResult> Damage(
        [Bind(Prefix = "DamageForm")]
        WarehouseDamageViewModel model)
    {
        var userId = CurrentUserId;

        if (string.IsNullOrWhiteSpace(userId))
            return Challenge();

        if (!ValidRequestId(model.RequestId) ||
            string.IsNullOrWhiteSpace(model.Reason))
        {
            ModelState.AddModelError(
                "DamageForm.Reason",
                "Geçerli form ve hasar nedeni gerekli.");
        }

        if (!ModelState.IsValid)
            return await FormErrorAsync(damage: model);

        var marker = $"[HASAR:{model.RequestId}]";

        try
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync();

            if (await db.StockMovements.AnyAsync(m =>
                m.Type == StockMovementType.Damage &&
                m.Notes != null &&
                m.Notes.StartsWith(marker)))
            {
                return Success(
                    "Bu hasar daha önce kaydedilmiş. "
                    + "Stok tekrar düşülmedi.");
            }

            var stock = await db.PalletItems
                .Include(i => i.Product)
                .Include(i => i.Pallet)
                .SingleOrDefaultAsync(
                    i => i.Id == model.PalletItemId);

            if (stock is null ||
                !stock.Product.IsActive ||
                !stock.Pallet.IsActive ||
                stock.Pallet.IsCancelled)
            {
                return await FormErrorAsync(
                    "Aktif bir stok kaydı seçin.",
                    damage: model);
            }

            if (!TryRemoveUnits(stock, model.Quantity, out var error))
            {
                return await FormErrorAsync(error, damage: model);
            }

            stock.ConcurrencyStamp =
                Guid.NewGuid().ToString("N");

            db.StockMovements.Add(new StockMovement
            {
                PalletItemId = stock.Id,
                Type = StockMovementType.Damage,
                BoxCount = 0,
                LooseUnitCount = model.Quantity,
                UnitsPerBox = stock.UnitsPerBox,
                CreatedByUserId = userId,
                CreatedAtUtc = DateTime.UtcNow,
                Notes = LimitNote($"{marker} {model.Reason.Trim()}")
            });

            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            return Success(
                $"{model.Quantity} hasarlı adet depodaki "
                + "kullanılabilir stoktan düşüldü.");
        }
        catch (Exception exception)
        {
            LogFailure(exception, "Depo hasarı kaydedilemedi.");

            return await FormErrorAsync(
                "Hasar kaydedilemedi. Stok değişikliği geri alındı.",
                damage: model);
        }
    }

    private async Task RefreshReceiptStatusAsync(
        int receiptId,
        string userId,
        DateTime now)
    {
        var receipt = await db.ReturnReceipts
            .Include(r => r.Items)
            .SingleAsync(r => r.Id == receiptId);

        if (receipt.Status == ReturnReceiptStatus.Cancelled)
            return;

        if (receipt.Items.All(
            i => i.Status == ReturnInspectionStatus.Completed))
        {
            receipt.Status = ReturnReceiptStatus.Completed;
            receipt.CompletedByUserId = userId;
            receipt.CompletedAtUtc = now;
        }
        else if (receipt.Items.Any(i =>
            i.Status == ReturnInspectionStatus.Waiting ||
            i.Status == ReturnInspectionStatus.Inspecting))
        {
            receipt.Status = receipt.Items.All(i =>
                i.Status == ReturnInspectionStatus.Waiting)
                    ? ReturnReceiptStatus.WaitingForInspection
                    : ReturnReceiptStatus.Inspecting;

            receipt.CompletedByUserId = null;
            receipt.CompletedAtUtc = null;
        }
        else
        {
            receipt.Status = ReturnReceiptStatus.WaitingForPlacement;
            receipt.CompletedByUserId = null;
            receipt.CompletedAtUtc = null;
        }

        receipt.ConcurrencyStamp =
            Guid.NewGuid().ToString("N");
    }

    private static bool TryRemoveUnits(
        PalletItem stock,
        int quantity,
        out string error)
    {
        error = string.Empty;

        if (quantity <= 0 ||
            stock.BoxCount < 0 ||
            stock.LooseUnitCount < 0 ||
            stock.OpenBoxUnitCount < 0)
        {
            error = "Stok miktarlarını kontrol edin.";
            return false;
        }

        if (stock.OpenBoxUnitCount > 0 &&
            (stock.OpenBoxCapacity is not > 0 ||
             stock.OpenBoxUnitCount >= stock.OpenBoxCapacity.Value))
        {
            error = "Açık koli bilgisi tutarsız. Stoku kontrol edin.";
            return false;
        }

        var boxes = stock.BoxCount;
        var open = stock.OpenBoxUnitCount;
        var loose = stock.LooseUnitCount;
        var capacity = stock.OpenBoxCapacity;
        long needed = quantity;

        var fromOpen = (int)Math.Min(needed, open);
        open -= fromOpen;
        needed -= fromOpen;

        if (open == 0)
            capacity = null;

        var fromLoose = (int)Math.Min(needed, loose);
        loose -= fromLoose;
        needed -= fromLoose;

        if (needed > 0)
        {
            if (stock.UnitsPerBox is not > 0)
            {
                error = "Kapalı kolilerin iç adedi bilinmiyor.";
                return false;
            }

            var unitsPerBox = stock.UnitsPerBox.Value;
            var boxesToOpen =
                (needed + unitsPerBox - 1) / unitsPerBox;

            if (boxesToOpen > boxes)
            {
                error = "Hasar miktarı mevcut stoktan fazla.";
                return false;
            }

            boxes -= (int)boxesToOpen;
            open = (int)(boxesToOpen * unitsPerBox - needed);
            capacity = open > 0 ? unitsPerBox : null;
        }

        stock.BoxCount = boxes;
        stock.OpenBoxUnitCount = open;
        stock.OpenBoxCapacity = capacity;
        stock.LooseUnitCount = loose;

        return true;
    }

    private async Task<ReturnsIndexViewModel> BuildPageAsync(
        string? search = null,
        ReturnReceiptStatus? statusFilter = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        int page = 1)
    {
        var shipments = await db.Shipments
            .AsNoTracking()
            .Include(s => s.Items)
                .ThenInclude(i => i.PalletItem)
            .Where(s =>
                s.Status == ShipmentStatus.Confirmed ||
                s.Status == ShipmentStatus.Shipped ||
                s.Status == ShipmentStatus.Delivered)
            .OrderByDescending(s => s.CreatedAtUtc)
            .ToListAsync();

        var returned = await db.ReturnInspections
            .AsNoTracking()
            .Where(i => i.Status != ReturnInspectionStatus.Cancelled)
            .Select(i => new { i.ShipmentItemId, i.ReceivedUnits })
            .ToListAsync();

        var returnedByLine = returned
            .GroupBy(i => i.ShipmentItemId)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(i => (long)i.ReceivedUnits));

        var available = shipments.Select(s =>
            new ReturnShipmentOptionViewModel
            {
                Id = s.Id,
                Number = s.Number,
                RecipientName = s.RecipientName,
                ConfirmedAtUtc = s.ConfirmedAtUtc,

                Items = s.Items.Select(i =>
                {
                    returnedByLine.TryGetValue(i.Id, out var previous);

                    return new ReturnShipmentLineViewModel
                    {
                        ShipmentItemId = i.Id,
                        ProductId = i.PalletItem.ProductId,
                        ProductName = i.ProductName,
                        ProductCode = i.ProductCode,
                        UnitsPerBox = i.UnitsPerBox,
                        ShippedUnits =
                            i.BoxCount > 0 && i.UnitsPerBox is not > 0
                                ? null
                                : (long)i.BoxCount
                                    * (i.UnitsPerBox ?? 0)
                                    + i.LooseUnitCount,
                        AlreadyReturnedUnits = previous
                    };
                }).ToList()
            }).ToList();

        var pallets = await db.Pallets
            .AsNoTracking()
            .Where(p => p.IsActive && !p.IsCancelled)
            .OrderBy(p => p.Location)
            .ToListAsync();

        var stocks = await db.PalletItems
            .AsNoTracking()
            .Include(i => i.Product)
            .Include(i => i.Pallet)
            .Where(i =>
                i.Product.IsActive &&
                i.Pallet.IsActive &&
                !i.Pallet.IsCancelled &&
                (i.BoxCount > 0 ||
                 i.OpenBoxUnitCount > 0 ||
                 i.LooseUnitCount > 0))
            .OrderBy(i => i.Product.Name)
            .ThenBy(i => i.Id)
            .ToListAsync();

        var query = db.ReturnReceipts
            .AsNoTracking()
            .AsQueryable();

        search = search?.Trim();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(r =>
                r.Shipment.RecipientName.Contains(search) ||
                r.Shipment.Number.Contains(search) ||
                r.Items.Any(i =>
                    i.ShipmentItem.ProductName.Contains(search) ||
                    i.ShipmentItem.ProductCode.Contains(search)));
        }

        if (statusFilter.HasValue)
            query = query.Where(r => r.Status == statusFilter.Value);

        if (startDate.HasValue)
        {
            var from = DateTime.SpecifyKind(
                startDate.Value.ToDateTime(TimeOnly.MinValue)
                    .AddHours(-3),
                DateTimeKind.Utc);

            query = query.Where(r => r.CreatedAtUtc >= from);
        }

        if (endDate.HasValue && endDate.Value < DateOnly.MaxValue)
        {
            var until = DateTime.SpecifyKind(
                endDate.Value.AddDays(1)
                    .ToDateTime(TimeOnly.MinValue)
                    .AddHours(-3),
                DateTimeKind.Utc);

            query = query.Where(r => r.CreatedAtUtc < until);
        }

        const int pageSize = 20;
        var total = await query.CountAsync();
        var pageCount = Math.Max(1, (total + pageSize - 1) / pageSize);
        page = Math.Clamp(page, 1, pageCount);

        var receipts = await query
            .Include(r => r.Shipment)
            .Include(r => r.Items)
                .ThenInclude(i => i.ShipmentItem)
            .Include(r => r.Items)
                .ThenInclude(i => i.Placements)
                    .ThenInclude(p => p.Pallet)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var legacy = await db.ReturnInspections
            .AsNoTracking()
            .Where(i => i.ReturnReceiptId == null)
            .Include(i => i.ShipmentItem)
                .ThenInclude(i => i.Shipment)
            .Include(i => i.DestinationPallet)
            .Include(i => i.Placements)
                .ThenInclude(p => p.Pallet)
            .OrderByDescending(i => i.CreatedAtUtc)
            .ToListAsync();

        
              return new ReturnsIndexViewModel
        {
            CanInspect = CanInspect,

            CanManage =
                MasterChefTakip.Services.OperationAuthorization.Can(
                    User,
                    AppPermissions.ReturnsCancel),

            CanReceive =
                MasterChefTakip.Services.OperationAuthorization.Can(
                    User,
                    AppPermissions.ReturnsReceive),

            CanRecordDamage =
                MasterChefTakip.Services.OperationAuthorization.Can(
                    User,
                    AppPermissions.ReturnsDamage),

            AvailableShipments = available,
            Receipts = receipts,
            Returns = legacy,

            Search = search,
            StatusFilter = statusFilter,
            StartDate = startDate,
            EndDate = endDate,

            Page = page,
            PageSize = pageSize,
            TotalCount = total,

            ShipmentOptions = available.Select(s =>
                new SelectListItem(
                    $"{s.RecipientName} — {s.Number}",
                    s.Id.ToString())).ToList(),

            PalletOptions = pallets.Select(p =>
                new SelectListItem(
                    $"{p.Location ?? "Konum yok"} — {p.Barcode}",
                    p.Id.ToString())).ToList(),

            StockOptions = stocks.Select(i =>
                new SelectListItem(
                    $"{i.Product.Name} [{i.Product.Code}] — "
                    + $"{i.Pallet.Location ?? "Konum yok"} — "
                    + $"Stok #{i.Id}: {i.BoxCount} kapalı koli, "
                    + $"{i.OpenBoxUnitCount} açık kolide adet, "
                    + $"{i.LooseUnitCount} ayrı adet",
                    i.Id.ToString())).ToList()
        };
    }
    private async Task<IActionResult> FormErrorAsync(
        string? message = null,
        CreateReturnReceiptViewModel? receipt = null,
        InspectReturnItemViewModel? inspection = null,
        PlaceReturnItemsViewModel? placement = null,
        WarehouseDamageViewModel? damage = null)
    {
        db.ChangeTracker.Clear();

        if (!string.IsNullOrWhiteSpace(message))
            ModelState.AddModelError(string.Empty, message);

        var page = await BuildPageAsync();

        if (receipt is not null)
            page.ReceiptForm = receipt;

        if (inspection is not null)
        {
            page.InspectionForm = inspection;
            await EnsureVisibleItemAsync(page, inspection.Id);
        }

        if (placement is not null)
        {
            page.PlacementForm = placement;
            await EnsureVisibleItemAsync(
                page, placement.ReturnInspectionId);
        }

        if (damage is not null)
            page.DamageForm = damage;

        return View("Index", page);
    }

    // Hatalı formun ait olduğu kayıt ilk sayfada değilse de
    // formu yeniden gösterebiliriz.
    private async Task EnsureVisibleItemAsync(
        ReturnsIndexViewModel page,
        int itemId)
    {
        var receiptId = await db.ReturnInspections
            .AsNoTracking()
            .Where(i => i.Id == itemId)
            .Select(i => i.ReturnReceiptId)
            .SingleOrDefaultAsync();

        if (!receiptId.HasValue ||
            page.Receipts.Any(r => r.Id == receiptId.Value))
            return;

        var receipt = await db.ReturnReceipts
            .AsNoTracking()
            .Include(r => r.Shipment)
            .Include(r => r.Items)
                .ThenInclude(i => i.ShipmentItem)
            .Include(r => r.Items)
                .ThenInclude(i => i.Placements)
                    .ThenInclude(p => p.Pallet)
            .SingleOrDefaultAsync(r => r.Id == receiptId.Value);

        if (receipt is not null)
            page.Receipts.Insert(0, receipt);
    }

    private void LogFailure(Exception exception, string message)
    {
        if (exception is DbUpdateConcurrencyException)
            logger.LogWarning(exception, message);
        else
            logger.LogError(exception, message);
    }

    private static bool ValidRequestId(string? value) =>
        Guid.TryParseExact(value, "N", out _);

    private static bool StampMatches(string saved, string submitted) =>
        string.Equals(saved, submitted, StringComparison.Ordinal);

    private static string LimitNote(string value) =>
        value.Length <= 500 ? value : value[..500];

    private IActionResult Success(string message)
    {
        TempData["ReturnSuccess"] = message;
        return RedirectToAction(nameof(Index));
    }

    private IActionResult Failure(string message)
    {
        TempData["ReturnError"] = message;
        return RedirectToAction(nameof(Index));
    }
}