using System.Security.Claims;
using MasterChefTakip.Data;
using MasterChefTakip.Models;
using MasterChefTakip.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

[Authorize(Policy = AppPermissions.ShipmentsView)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public partial class ShipmentsController(
    ApplicationDbContext db,
    ILogger<ShipmentsController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        int? shipmentId = null,
        string? form = null)
    {
        if (shipmentId.HasValue &&
            !await db.Shipments.AnyAsync(
                s => s.Id == shipmentId.Value))
        {
            return NotFound();
        }

        return View(await BuildPageAsync(
            shipmentId,
            showForm: form == "new"));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissions.ShipmentsManage)]
    public async Task<IActionResult> Create(
        [Bind(Prefix = "Form")] CreateShipmentViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.RecipientName))
        {
            ModelState.AddModelError(
                "Form.RecipientName",
                "Alıcı / müşteri adı gereklidir.");
        }

        if (!ModelState.IsValid)
        {
            return View("Index", await BuildPageAsync(
                form: model,
                showForm: true));
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var shipment = new Shipment
        {
            Number = $"SVK-{Guid.NewGuid():N}".ToUpperInvariant(),
            RecipientName = model.RecipientName.Trim(),
            RecipientPhone = Clean(model.RecipientPhone),
            DeliveryAddress = Clean(model.DeliveryAddress),
            CargoCompany = Clean(model.CargoCompany),
            TrackingNumber = Clean(model.TrackingNumber),
            Notes = Clean(model.Notes),
            Status = ShipmentStatus.Draft,
            CreatedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow
        };

        try
        {
            db.Shipments.Add(shipment);
            await db.SaveChangesAsync();

            TempData["ShipmentSuccess"] =
                "Taslak oluşturuldu. Şimdi ürünlerini ekleyin.";

            return RedirectToAction(
                nameof(Index),
                new { shipmentId = shipment.Id });
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Sevkiyat oluşturulamadı.");

            db.ChangeTracker.Clear();

            ModelState.AddModelError(
                string.Empty,
                "Sevkiyat oluşturulamadı. Tekrar deneyin.");

            return View("Index", await BuildPageAsync(
                form: model,
                showForm: true));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissions.ShipmentsManage)]
    public async Task<IActionResult> AddItem(
        [Bind(Prefix = "ItemForm")] AddShipmentItemViewModel model)
    {
        if (model.Quantity is not > 0)
        {
            ModelState.AddModelError(
                "ItemForm.Quantity",
                "Miktar en az 1 olmalıdır.");
        }

        if (model.QuantityType != ShipmentQuantityType.Box &&
            model.QuantityType != ShipmentQuantityType.Unit)
        {
            ModelState.AddModelError(
                "ItemForm.QuantityType",
                "Adet veya koli seçin.");
        }

        if (!ModelState.IsValid)
        {
            return View("Index", await BuildPageAsync(
                model.ShipmentId,
                itemForm: model));
        }

        try
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync();

            var shipment = await db.Shipments
                .SingleOrDefaultAsync(
                    s => s.Id == model.ShipmentId);

            if (shipment is null)
            {
                return NotFound();
            }

            if (shipment.Status != ShipmentStatus.Draft)
            {
                return Failure(
                    shipment.Id,
                    "Onaylanan sevkiyata ürün eklenemez.");
            }

            var stock = await db.PalletItems
                .Include(i => i.Pallet)
                .Include(i => i.Product)
                .SingleOrDefaultAsync(
                    i => i.Id == model.PalletItemId);

            if (stock is null ||
                !stock.Pallet.IsActive ||
                stock.Pallet.IsCancelled ||
                !stock.Product.IsActive)
            {
                return Failure(
                    shipment.Id,
                    "Aktif bir paletten aktif ürün seçin.");
            }

            var line = await db.ShipmentItems
                .SingleOrDefaultAsync(i =>
                    i.ShipmentId == shipment.Id &&
                    i.PalletItemId == stock.Id);

            if (line is not null &&
                (line.BoxCount > 0 || line.LooseUnitCount > 0) &&
                line.UnitsPerBox != stock.UnitsPerBox)
            {
                return Failure(
                    shipment.Id,
                    "Koli içi adet bilgisi değişmiş. "
                    + "Mevcut satırı kaldırıp yeniden ekleyin.");
            }

            var quantity = model.Quantity.GetValueOrDefault();

            var addedBoxes =
                model.QuantityType == ShipmentQuantityType.Box
                    ? quantity
                    : 0;

            var addedUnits =
                model.QuantityType == ShipmentQuantityType.Unit
                    ? quantity
                    : 0;

            long boxes =
                (long)(line?.BoxCount ?? 0) + addedBoxes;

            long units =
                (long)(line?.LooseUnitCount ?? 0) + addedUnits;

            if (boxes > int.MaxValue || units > int.MaxValue)
            {
                return Failure(
                    shipment.Id,
                    "Toplam miktar desteklenen sınırı aşıyor.");
            }

            if (!TryCalculateBalance(
                stock,
                (int)boxes,
                (int)units,
                out _,
                out _,
                out _,
                out _,
                out _,
                out var error))
            {
                return Failure(
                    shipment.Id,
                    $"{stock.Product.Name}: {error}");
            }

            if (line is null)
            {
                line = new ShipmentItem
                {
                    ShipmentId = shipment.Id,
                    PalletItemId = stock.Id
                };

                db.ShipmentItems.Add(line);
            }

            line.BoxCount = (int)boxes;
            line.LooseUnitCount = (int)units;
            line.UnitsPerBox = stock.UnitsPerBox;
            line.ProductName = stock.Product.Name;
            line.ProductCode = stock.Product.Code;
            line.PalletBarcode = stock.Pallet.Barcode;

            shipment.ConcurrencyStamp =
                Guid.NewGuid().ToString("N");

            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["ShipmentSuccess"] =
                "Ürün taslağa eklendi. Stok henüz değişmedi.";

            return RedirectToAction(
                nameof(Index),
                new { shipmentId = shipment.Id });
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Sevkiyata ürün eklenemedi.");

            db.ChangeTracker.Clear();

            return Failure(
                model.ShipmentId,
                "Ürün eklenemedi. Sayfayı yenileyip tekrar deneyin.");
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissions.ShipmentsManage)]
    public async Task<IActionResult> RemoveItem(int id)
    {
        var shipmentId = 0;

        try
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync();

            var line = await db.ShipmentItems
                .Include(i => i.Shipment)
                .SingleOrDefaultAsync(i => i.Id == id);

            if (line is null)
            {
                return NotFound();
            }

            shipmentId = line.ShipmentId;

            if (line.Shipment.Status != ShipmentStatus.Draft)
            {
                return Failure(
                    shipmentId,
                    "Onaylanan sevkiyattan ürün kaldırılamaz.");
            }

            line.Shipment.ConcurrencyStamp =
                Guid.NewGuid().ToString("N");

            db.ShipmentItems.Remove(line);

            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["ShipmentSuccess"] =
                "Ürün taslaktan kaldırıldı.";

            return RedirectToAction(
                nameof(Index),
                new { shipmentId });
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Sevkiyat satırı kaldırılamadı.");

            db.ChangeTracker.Clear();

            return Failure(
                shipmentId,
                "Ürün kaldırılamadı. Sayfayı yenileyip tekrar deneyin.");
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissions.ShipmentsManage)]
    public async Task<IActionResult> Confirm(
        int id,
        string? concurrencyStamp)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        try
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync();

            var shipment = await db.Shipments
                .Include(s => s.Items)
                .SingleOrDefaultAsync(s => s.Id == id);

            if (shipment is null)
            {
                return NotFound();
            }

            if (shipment.Status == ShipmentStatus.Confirmed)
            {
                return Failure(
                    id,
                    "Sevkiyat zaten onaylanmış. Stok yeniden düşülmedi.");
            }

            if (shipment.Status != ShipmentStatus.Draft)
            {
                return Failure(
                    id,
                    "Yalnızca taslak sevkiyat onaylanabilir.");
            }

            if (!string.Equals(
                shipment.ConcurrencyStamp,
                concurrencyStamp,
                StringComparison.Ordinal))
            {
                return Failure(
                    id,
                    "Taslak değişti. Sayfayı yenileyip kontrol edin.");
            }

            if (shipment.Items.Count == 0)
            {
                return Failure(
                    id,
                    "Önce sevkiyata ürün ekleyin.");
            }
            // Eski onay düğmesi kullanılsa da sipariş kontrolü yapılır.
var orderError = await ValidateOrderContentsAsync(
    shipment,
    shipment.Items.Select(i => (
        StockId: i.PalletItemId,
        Boxes: (long)i.BoxCount,
        Units: (long)i.LooseUnitCount)));

if (orderError is not null)
{
    return Failure(id, orderError);
}
            var stockIds = shipment.Items
                .Select(i => i.PalletItemId)
                .Distinct()
                .ToList();

            var stocks = await db.PalletItems
                .Include(i => i.Pallet)
                .Include(i => i.Product)
                .Where(i => stockIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id);

            var now = DateTime.UtcNow;

            foreach (var line in shipment.Items)
            {
                if (!stocks.TryGetValue(
                    line.PalletItemId,
                    out var stock))
                {
                    return Failure(
                        id,
                        "Sevkiyattaki bir palet içeriği bulunamadı.");
                }

                if (!stock.Pallet.IsActive ||
                    stock.Pallet.IsCancelled ||
                    !stock.Product.IsActive)
                {
                    return Failure(
                        id,
                        $"{stock.Product.Name}: Ürün veya palet aktif değil.");
                }

                if (line.UnitsPerBox != stock.UnitsPerBox)
                {
                    return Failure(
                        id,
                        $"{stock.Product.Name}: Koli içi adet değişmiş. "
                        + "Satırı kaldırıp yeniden ekleyin.");
                }

                if (!TryCalculateBalance(
                    stock,
                    line.BoxCount,
                    line.LooseUnitCount,
                    out var remainingBoxes,
                    out var remainingUnits,
                    out var remainingOpenUnits,
                    out var remainingOpenCapacity,
                    out var openedBoxes,
                    out var error))
                {
                    return Failure(
                        id,
                        $"{stock.Product.Name}: {error}");
                }
                line.HasStockSnapshot = true;
                line.BeforeBoxCount = stock.BoxCount;
                line.BeforeLooseUnitCount = stock.LooseUnitCount;
                line.BeforeOpenBoxUnitCount = stock.OpenBoxUnitCount;
                line.BeforeOpenBoxCapacity = stock.OpenBoxCapacity;
                stock.BoxCount = remainingBoxes;
                stock.LooseUnitCount = remainingUnits;
                stock.OpenBoxUnitCount = remainingOpenUnits;
                stock.OpenBoxCapacity = remainingOpenCapacity;
                stock.ConcurrencyStamp =
                Guid.NewGuid().ToString("N");
                line.AfterStockStamp = stock.ConcurrencyStamp;

                line.ProductName = stock.Product.Name;
                line.ProductCode = stock.Product.Code;
                line.PalletBarcode = stock.Pallet.Barcode;
                line.UnitsPerBox = stock.UnitsPerBox;

                var movementNote =
                    $"Sevkiyat: {shipment.Number}. "
                    + $"Gönderilen: {line.BoxCount} kapalı koli, "
                    + $"{line.LooseUnitCount} adet. "
                    + $"Bu işlemde açılan koli: {openedBoxes}. "
                    + $"Kalan: {remainingBoxes} kapalı koli, "
                    + $"açık kolide {remainingOpenUnits} adet, "
                    + $"{remainingUnits} bağımsız tekli adet.";

                db.StockMovements.Add(new StockMovement
                {
                    RelatedShipmentId = shipment.Id,
                    PalletItemId = stock.Id,
                    Type = StockMovementType.Shipment,
                    BoxCount = line.BoxCount,
                    LooseUnitCount = line.LooseUnitCount,
                    UnitsPerBox = stock.UnitsPerBox,
                    CreatedByUserId = userId,
                    Notes = movementNote,
                    CreatedAtUtc = now
                });
            }

            shipment.Status = ShipmentStatus.Confirmed;
            shipment.ConfirmedByUserId = userId;
            shipment.ConfirmedAtUtc = now;
            shipment.ConcurrencyStamp =
                Guid.NewGuid().ToString("N");

            // Stok, geçmiş ve sevkiyat aynı işlemde kaydedilir.
            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["ShipmentSuccess"] =
                "Sevkiyat başarıyla onaylandı. Ürünler stoktan düşüldü. Sevkiyat gönderime hazır.";

            return RedirectToAction(
                nameof(Index),
                new { shipmentId = id });
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogWarning(
                exception,
                "Sevkiyat onayı sırasında stok veya taslak değişti.");

            db.ChangeTracker.Clear();

            return Failure(
                id,
                "Stok veya taslak başka bir işlemde değişti. "
                + "Sayfayı yenileyip tekrar kontrol edin.");
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Sevkiyat onaylanamadı.");

            db.ChangeTracker.Clear();

            return Failure(
                id,
                "Sevkiyat onaylanamadı. Stok değişiklikleri geri alındı.");
        }
    }

    private static bool TryCalculateBalance(
        PalletItem stock,
        int outgoingBoxes,
        int outgoingUnits,
        out int remainingBoxes,
        out int remainingUnits,
        out int remainingOpenUnits,
        out int? remainingOpenCapacity,
        out int openedBoxes,
        out string error)
    {
        remainingBoxes = stock.BoxCount;
        remainingUnits = stock.LooseUnitCount;
        remainingOpenUnits = stock.OpenBoxUnitCount;
        remainingOpenCapacity = stock.OpenBoxCapacity;
        openedBoxes = 0;
        error = string.Empty;

        if (stock.BoxCount < 0 ||
            stock.LooseUnitCount < 0 ||
            stock.OpenBoxUnitCount < 0)
        {
            error = "Mevcut stok bilgisi geçersiz.";
            return false;
        }

        if (stock.OpenBoxUnitCount > 0 &&
            (stock.OpenBoxCapacity is not > 0 ||
             stock.OpenBoxUnitCount >= stock.OpenBoxCapacity.Value))
        {
            error =
                "Açık kolinin miktar veya kapasite bilgisi geçersiz.";
            return false;
        }

        if (outgoingBoxes < 0 ||
            outgoingUnits < 0 ||
            (outgoingBoxes == 0 && outgoingUnits == 0))
        {
            error = "Geçerli bir çıkış miktarı girin.";
            return false;
        }

        if (outgoingBoxes > stock.BoxCount)
        {
            error =
                $"İstenen: {outgoingBoxes} koli. Mevcut: {stock.BoxCount} koli. Eksik: {outgoingBoxes - stock.BoxCount} koli.";
            return false;
        }

        // Tam koli çıkışları önce ayrılır.
        long boxesLeft =
            (long)stock.BoxCount - outgoingBoxes;

        long looseLeft = stock.LooseUnitCount;
        long openLeft = stock.OpenBoxUnitCount;

        int? openCapacity = openLeft > 0
            ? stock.OpenBoxCapacity
            : null;

        long unitsNeeded = outgoingUnits;

        // Önce açık kolideki ürünler kullanılır.
        long fromOpen = Math.Min(unitsNeeded, openLeft);

        openLeft -= fromOpen;
        unitsNeeded -= fromOpen;

        if (openLeft == 0)
        {
            openCapacity = null;
        }

        // Sonra bağımsız tekli stok kullanılır.
        long fromLoose = Math.Min(unitsNeeded, looseLeft);

        looseLeft -= fromLoose;
        unitsNeeded -= fromLoose;

        // Yetmezse kapalı koli açılır.
        if (unitsNeeded > 0)
        {
            if (stock.UnitsPerBox is not > 0)
            {
                error =
                    "Koli açabilmek için koli içi adet bilgisi gerekli.";
                return false;
            }

            long capacity = stock.UnitsPerBox.Value;

            long boxesToOpen =
                (unitsNeeded + capacity - 1) / capacity;

            if (boxesToOpen > boxesLeft)
            {
                error =
                    $"İstenen: {outgoingUnits} adet. Kullanılabilir: {openLeft + looseLeft + boxesLeft * capacity + fromOpen + fromLoose} adet. Eksik: {unitsNeeded - boxesLeft * capacity} adet.";
                return false;
            }

            boxesLeft -= boxesToOpen;

            // Sadece son açılan kolinin kalan ürünleri tutulur.
            openLeft =
                boxesToOpen * capacity - unitsNeeded;

            openCapacity = openLeft > 0
                ? stock.UnitsPerBox
                : null;

            openedBoxes = (int)boxesToOpen;
        }

        remainingBoxes = (int)boxesLeft;
        remainingUnits = (int)looseLeft;
        remainingOpenUnits = (int)openLeft;
        remainingOpenCapacity = openCapacity;

        return true;
    }

    private IActionResult Failure(
        int shipmentId,
        string message)
    {
        TempData["ShipmentError"] = message;

        return RedirectToAction(nameof(Index), new
        {
            shipmentId = shipmentId > 0
                ? (int?)shipmentId
                : null
        });
    }

    private async Task<ShipmentIndexViewModel> BuildPageAsync(
        int? shipmentId = null,
        CreateShipmentViewModel? form = null,
        bool showForm = false,
        AddShipmentItemViewModel? itemForm = null)
    {
        var model = new ShipmentIndexViewModel
        {
            Shipments = await db.Shipments
                .AsNoTracking()
                .OrderByDescending(s => s.CreatedAtUtc)
                .ToListAsync(),

            Form = form ?? new CreateShipmentViewModel(),
            ShowForm = showForm,

            ItemForm = itemForm ?? new AddShipmentItemViewModel
            {
                ShipmentId = shipmentId ?? 0
            }
        };

        if (shipmentId is > 0)
        {
           model.SelectedShipment = await db.Shipments
            .AsNoTracking()
            .Include(s => s.Items)
            .Include(s => s.Packages)
            .ThenInclude(package => package.Items)
            .ThenInclude(item => item.ShipmentItem)
            .SingleOrDefaultAsync(
            s => s.Id == shipmentId.Value);

            if (model.SelectedShipment?.Status ==
                ShipmentStatus.Draft)
            {
                var stocks = await db.PalletItems
                    .AsNoTracking()
                    .Include(i => i.Product)
                    .Include(i => i.Pallet)
                    .Where(i =>
                        i.Pallet.IsActive &&
                        !i.Pallet.IsCancelled &&
                        i.Product.IsActive &&
                        (i.BoxCount > 0 ||
                         i.LooseUnitCount > 0 ||
                         i.OpenBoxUnitCount > 0))
                    .OrderBy(i => i.Product.Name)
                    .ThenBy(i => i.Pallet.Barcode)
                    .ToListAsync();

                model.StockOptions = stocks.Select(i =>
                {
                    long individualTotal =
                        (long)i.LooseUnitCount
                        + i.OpenBoxUnitCount;

                    var totalText = i.UnitsPerBox is > 0
                        ? $"Toplam {(long)i.BoxCount * i.UnitsPerBox.Value + individualTotal} adet"
                        : $"Tekli toplam {individualTotal} adet; koli içi adet bilinmiyor";

                   return new ShipmentStockOptionViewModel
                    {
                        Id = i.Id,

                        UnitsPerBox = i.UnitsPerBox,
                        BoxCount = i.BoxCount,
                        LooseUnitCount = i.LooseUnitCount,
                        OpenBoxUnitCount = i.OpenBoxUnitCount,

                        Label =
                        $"{i.Product.Name} [{i.Product.Code}] — "
                        + $"{i.Pallet.Location ?? "Konum belirtilmedi"} — "
                        + $"Stok #{i.Id} — "
                        + $"{i.BoxCount} kapalı koli — "
                        + $"Koli içi "
                        + $"{(i.UnitsPerBox?.ToString() ?? "bilinmiyor")} — "
                        + $"{totalText}"
                    };
                }).ToList();
            }
        }

        return model;
    }

    private static string? Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}