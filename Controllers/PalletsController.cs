// PalletTransferActionGuards
using System.Security.Claims;
using MasterChefTakip.Data;
using MasterChefTakip.Models;
using MasterChefTakip.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

[Authorize(Policy = AppPermissions.PalletsView)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class PalletsController(
    ApplicationDbContext db,
    ILogger<PalletsController> logger) : Controller
{
        [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissions.PalletsManage)]
    public async Task<IActionResult> SetActive(int id, bool isActive)
    {
        var pallet = await db.Pallets.FindAsync(id);

        if (pallet is null)
        {
            return NotFound();
        }

        pallet.IsActive = isActive;

        try
        {
            await db.SaveChangesAsync();

            TempData["PalletSuccess"] = isActive
                ? "Palet yeniden aktifleştirildi."
                : "Palet pasife alındı. Ürünleri ve geçmişi korundu.";
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Paletin durumu değiştirilemedi.");

            TempData["PalletError"] =
                "Paletin durumu değiştirilemedi. Tekrar deneyin.";
        }

        return RedirectToAction(
            nameof(Index),
            new { palletId = id });
    }

        [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync();

            var pallet = await db.Pallets.FindAsync(id);

            if (pallet is null)
            {
                return NotFound();
            }

            var movements = await db.StockMovements
                .Where(m => m.PalletItem.PalletId == id)
                .ToListAsync();

            db.StockMovements.RemoveRange(movements);
            await db.SaveChangesAsync();

            var items = await db.PalletItems
                .Where(item => item.PalletId == id)
                .ToListAsync();

            db.PalletItems.RemoveRange(items);
            await db.SaveChangesAsync();

            db.Pallets.Remove(pallet);
            await db.SaveChangesAsync();

            await transaction.CommitAsync();

            TempData["PalletSuccess"] =
                "Palet, içeriği ve stok hareketleri tamamen silindi.";

            return RedirectToAction(nameof(Index));
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Palet tamamen silinirken hata oluştu.");

            db.ChangeTracker.Clear();

            TempData["PalletError"] =
                "Palet silinemedi. İşlem geri alındı.";

            return RedirectToAction(
                nameof(Index),
                new { palletId = id });
        }
    }
    [HttpGet]
    public async Task<IActionResult> Index(
        string? form = null,
        int? palletId = null)
    {
        if (palletId.HasValue &&
            !await db.Pallets.AnyAsync(p => p.Id == palletId.Value))
        {
            return NotFound();
        }

        return View(await BuildPageAsync(
            showForm: form == "new",
            palletId: palletId));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissions.PalletsManage)]
    public async Task<IActionResult> Create(
        [Bind(Prefix = "Form")] CreatePalletViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View("Index", await BuildPageAsync(model, true));
        }

        var pallet = new Pallet
        {
            Barcode = $"PLT-{Guid.NewGuid():N}".ToUpperInvariant(),
            Location = CleanText(model.Location),
            Notes = CleanText(model.Notes),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        try
        {
            db.Pallets.Add(pallet);
            await db.SaveChangesAsync();

            TempData["PalletSuccess"] =
                "Palet oluşturuldu. Barkodu: " + pallet.Barcode;

            return RedirectToAction(
                nameof(Index),
                new { palletId = pallet.Id });
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Palet oluşturulurken hata oluştu.");

            db.ChangeTracker.Clear();

            ModelState.AddModelError(
                string.Empty,
                "Palet kaydedilemedi. Lütfen tekrar deneyin.");

            return View("Index", await BuildPageAsync(model, true));
        }
    }

  [HttpPost]
[ValidateAntiForgeryToken]
[Authorize(Policy = AppPermissions.StockManage)]
    public async Task<IActionResult> AddProduct(
    [Bind(Prefix = "ProductForm")] AddPalletProductViewModel model)
{
    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

    if (string.IsNullOrWhiteSpace(userId))
    {
        return Challenge();
    }

    var isBox = model.QuantityType == PalletQuantityType.Box;
    var quantity = model.Quantity.GetValueOrDefault();

    if (quantity <= 0)
    {
        ModelState.AddModelError(
            "ProductForm.Quantity",
            "Pozitif bir miktar girin.");
    }

    if (!isBox && model.UnitsPerBox.HasValue)
    {
        ModelState.AddModelError(
            "ProductForm.UnitsPerBox",
            "Adet girişinde koli içi adet alanını boş bırakın.");
    }

    if (ModelState.IsValid)
    {
        try
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync();

            var pallet = await db.Pallets
                .SingleOrDefaultAsync(p => p.Id == model.PalletId);

            var product = await db.Products
                .SingleOrDefaultAsync(p => p.Id == model.ProductId);

            if (pallet is null || !pallet.IsActive || pallet.IsCancelled)
            {
                ModelState.AddModelError(
                    "ProductForm.PalletId",
                    "Aktif bir palet seçin.");
            }

            if (product is null || !product.IsActive)
            {
                ModelState.AddModelError(
                    "ProductForm.ProductId",
                    "Aktif bir ürün seçin.");
            }

            if (ModelState.IsValid && product is not null)
            {
                int? capacity = isBox
                    ? model.UnitsPerBox ?? product.DefaultUnitsPerBox
                    : null;

                var item = new PalletItem
                {
                    PalletId = model.PalletId,
                    ProductId = model.ProductId,
                    BoxCount = isBox ? quantity : 0,
                    LooseUnitCount = isBox ? 0 : quantity,
                    UnitsPerBox = capacity
                };

                db.PalletItems.Add(item);

                db.StockMovements.Add(new StockMovement
                {
                    PalletItem = item,
                    Type = StockMovementType.Receipt,
                    BoxCount = item.BoxCount,
                    LooseUnitCount = item.LooseUnitCount,
                    UnitsPerBox = capacity,
                    CreatedByUserId = userId,
                    CreatedAtUtc = DateTime.UtcNow,
                    Notes = CleanText(model.Notes)
                });

                await db.SaveChangesAsync();
                await transaction.CommitAsync();

                TempData["PalletSuccess"] =
                    $"{product.Name}: {quantity} "
                    + (isBox ? "koli" : "adet")
                    + " palete eklendi.";

                return RedirectToAction(
                    nameof(Index),
                    new { palletId = model.PalletId });
            }

            await transaction.RollbackAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Palete ürün eklenemedi.");

            ModelState.AddModelError(
                string.Empty,
                "Ürün eklenemedi. İşlem geri alındı.");
        }
    }

    db.ChangeTracker.Clear();

    return View("Index", await BuildPageAsync(
        palletId: model.PalletId,
        productForm: model));
}
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissions.PalletsLocation)]
    public async Task<IActionResult> Edit(
        [Bind(Prefix = "EditForm")] EditPalletViewModel model)
    {
        if (!ModelState.IsValid)
        {
            var page = await BuildPageAsync(palletId: model.Id);
            page.EditForm = model;

            return View("Index", page);
        }

        var pallet = await db.Pallets.FindAsync(model.Id);

        if (pallet is null)
        {
            return NotFound();
        }

        pallet.Location = CleanText(model.Location);
        pallet.Notes = CleanText(model.Notes);

        try
        {
            await db.SaveChangesAsync();

            TempData["PalletSuccess"] =
                "Paletin konumu ve notu güncellendi.";

            return RedirectToAction(
                nameof(Index),
                new { palletId = pallet.Id });
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Palet bilgileri güncellenirken hata oluştu.");

            db.ChangeTracker.Clear();

            ModelState.AddModelError(
                string.Empty,
                "Bilgiler güncellenemedi. Tekrar deneyin.");

            var page = await BuildPageAsync(palletId: model.Id);
            page.EditForm = model;

            return View("Index", page);
        }
    }
    [HttpGet]
    public async Task<IActionResult> LabelData(int id)
    {
        await using var transaction =
            await db.Database.BeginTransactionAsync();

        var pallet = await db.Pallets
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == id);

        if (pallet is null)
        {
            return NotFound();
        }

        var items = await db.PalletItems
            .AsNoTracking()
            .Where(i => i.PalletId == id
                && (i.BoxCount > 0 || i.LooseUnitCount > 0))
            .OrderBy(i => i.Product.Name)
            .Select(i => new
            {
                name = i.Product.Name,
                code = i.Product.Code,
                boxCount = i.BoxCount,
                looseUnitCount = i.LooseUnitCount,
                unitsPerBox = i.UnitsPerBox
            })
            .ToListAsync();

        var capturedAtUtc = DateTimeOffset.UtcNow;

        await transaction.CommitAsync();

        return Json(new
        {
            barcode = pallet.Barcode,
            location = pallet.Location,
            notes = pallet.Notes,
            isActive = pallet.IsActive,
            capturedAtUtc,
            items
        });
    }
    [HttpGet]
public async Task<IActionResult> FindByBarcode(string? barcode)
{
    if (string.IsNullOrWhiteSpace(barcode))
    {
        return BadRequest(new
        {
            message = "Palet barkodunu girin veya okutun."
        });
    }

    var normalizedBarcode = barcode.Trim().ToUpperInvariant();

    if (normalizedBarcode.Length > 50)
    {
        return BadRequest(new
        {
            message = "Palet barkodu en fazla 50 karakter olabilir."
        });
    }

    var pallet = await db.Pallets
        .AsNoTracking()
        .Where(pallet => pallet.Barcode == normalizedBarcode)
        .Select(pallet => new
        {
            pallet.Id,
            pallet.Barcode
        })
        .SingleOrDefaultAsync();

    if (pallet is null)
    {
        return Json(new
        {
            found = false,
            message = "Bu barkoda ait kayıtlı palet bulunamadı."
        });
    }

    var url = Url.Action(
        nameof(Index),
        "Pallets",
        new { palletId = pallet.Id });

    return Json(new
    {
        found = true,
        barcode = pallet.Barcode,
        url = url + "#pallet-content"
    });
}
[HttpPost]
[ValidateAntiForgeryToken]
[Authorize(Policy = AppPermissions.StockManage)]
    public async Task<IActionResult> CorrectReceipt(
    [Bind(Prefix = "CorrectionForm")]
    CorrectPalletReceiptViewModel model)
{
    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

    if (string.IsNullOrWhiteSpace(userId))
    {
        return Challenge();
    }

    var palletId = await db.StockMovements
        .Where(m => m.Id == model.MovementId)
        .Select(m => (int?)m.PalletItem.PalletId)
        .SingleOrDefaultAsync();

    if (palletId is null)
    {
        return NotFound();
    }

    IActionResult Failure(string message)
    {
        TempData["PalletError"] = message;

        return RedirectToAction(
            nameof(Index),
            new { palletId = palletId.Value });
    }

    if (!ModelState.IsValid || string.IsNullOrWhiteSpace(model.Reason))
    {
        return Failure("Ürün, miktarlar ve düzeltme nedenini kontrol edin.");
    }

    var boxes = model.BoxCount.GetValueOrDefault();
    var singles = model.LooseUnitCount.GetValueOrDefault();

    if (boxes < 0 || singles < 0)
    {
        return Failure("Miktarlar negatif olamaz.");
    }

    try
    {
        await using var transaction =
            await db.Database.BeginTransactionAsync();

        var original = await db.StockMovements
            .Include(m => m.PalletItem)
                .ThenInclude(item => item.Pallet)
            .SingleOrDefaultAsync(m => m.Id == model.MovementId);

        if (original is null)
        {
            return NotFound();
        }

        var source = original.PalletItem;

        if (original.Type != StockMovementType.Receipt)
        {
            return Failure("Yalnızca stok girişleri düzeltilebilir.");
        }

        if (!source.Pallet.IsActive || source.Pallet.IsCancelled)
        {
            return Failure("Aktif bir palet üzerinde düzeltme yapın.");
        }

        if (await db.StockMovements.AnyAsync(m =>
            m.ReversesMovementId == original.Id
            || m.CorrectsMovementId == original.Id))
        {
            return Failure("Bu giriş daha önce düzeltilmiş veya geri alınmış.");
        }

        if (!string.Equals(
            source.ConcurrencyStamp,
            model.ConcurrencyStamp,
            StringComparison.Ordinal))
        {
            return Failure("Stok değişti. Sayfayı yenileyip tekrar deneyin.");
        }

        var product = await db.Products
            .SingleOrDefaultAsync(p => p.Id == model.ProductId);

        if (product is null || !product.IsActive)
        {
            return Failure("Aktif bir ürün seçin.");
        }

        var sameProduct = source.ProductId == product.Id;

        int? capacity = boxes > 0
            ? model.UnitsPerBox
                ?? (sameProduct
                    ? original.UnitsPerBox
                    : product.DefaultUnitsPerBox)
            : null;

        if (sameProduct
            && boxes == original.BoxCount
            && singles == original.LooseUnitCount
            && (boxes == 0 || capacity == original.UnitsPerBox))
        {
            return Failure("Bilgiler aynı; değişiklik yapılmadı.");
        }

        // Eski giriş kapalı koliler ve bağımsız teklilerden geri alınır.
        // Açılmış veya gönderilmiş ürünler sessizce yeniden oluşturulmaz.
        var remainingBoxes = (long)source.BoxCount - original.BoxCount;
        var remainingSingles =
            (long)source.LooseUnitCount - original.LooseUnitCount;

        if (remainingBoxes < 0 || remainingSingles < 0)
        {
            return Failure(
                "Bu girişin ürünlerinden çıkış yapılmış veya koli açılmış. "
                + "Önce ilgili çıkışı kontrol edin; giriş tamamen geri alınamıyor.");
        }

        source.BoxCount = (int)remainingBoxes;
        source.LooseUnitCount = (int)remainingSingles;
        source.ConcurrencyStamp = Guid.NewGuid().ToString("N");

        var now = DateTime.UtcNow;
        var reason = model.Reason.Trim();

        db.StockMovements.Add(new StockMovement
        {
            PalletItemId = source.Id,
            Type = StockMovementType.ReceiptCancellation,
            BoxCount = original.BoxCount,
            LooseUnitCount = original.LooseUnitCount,
            UnitsPerBox = original.UnitsPerBox,
            ReversesMovementId = original.Id,
            CreatedByUserId = userId,
            CreatedAtUtc = now,
            Notes = reason
        });

        // Düzeltilen giriş yeni bir satırdır.
        // Eski stok satırının ürünü veya koli kapasitesi değiştirilmez.
        var target = new PalletItem
        {
            PalletId = source.PalletId,
            ProductId = product.Id,
            BoxCount = boxes,
            LooseUnitCount = singles,
            UnitsPerBox = capacity
        };

        db.PalletItems.Add(target);

        db.StockMovements.Add(new StockMovement
        {
            PalletItem = target,
            PackagingBatchId = original.PackagingBatchId,
            Type = StockMovementType.Receipt,
            BoxCount = boxes,
            LooseUnitCount = singles,
            UnitsPerBox = capacity,
            CorrectsMovementId = original.Id,
            CreatedByUserId = userId,
            CreatedAtUtc = now,
            Notes = reason
        });

        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        TempData["PalletSuccess"] =
            "Giriş düzeltildi. Eski işlem geçmişte korundu.";

        return RedirectToAction(
            nameof(Index),
            new { palletId = palletId.Value });
    }
    catch (DbUpdateConcurrencyException exception)
    {
        logger.LogWarning(exception, "Düzeltme sırasında stok değişti.");

        db.ChangeTracker.Clear();

        return Failure("Stok değişti. Sayfayı yenileyip tekrar deneyin.");
    }
    catch (Exception exception)
    {
        logger.LogError(exception, "Stok girişi düzeltilemedi.");

        db.ChangeTracker.Clear();

        return Failure("Düzeltme kaydedilemedi. İşlem geri alındı.");
    }
}
    private async Task<PalletIndexViewModel> BuildPageAsync(
    CreatePalletViewModel? form = null,
    bool showForm = false,
    int? palletId = null,
    AddPalletProductViewModel? productForm = null)
{
    var model = new PalletIndexViewModel
    {
        Pallets = await db.Pallets
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(),

        Form = form ?? new CreatePalletViewModel(),
        CanManage = User.IsInRole(AppRoles.Admin),
        ShowForm = showForm,

        ProductForm = productForm
            ?? new AddPalletProductViewModel
            {
                PalletId = palletId ?? 0
            }
    };

    if (palletId is > 0)
    {
        var selectedPalletId = palletId.Value;

        model.SelectedPallet = model.Pallets
            .FirstOrDefault(p => p.Id == selectedPalletId);

        if (model.SelectedPallet is not null)
        {
            model.EditForm = new EditPalletViewModel
            {
                Id = model.SelectedPallet.Id,
                Location = model.SelectedPallet.Location,
                Notes = model.SelectedPallet.Notes
            };

            model.AvailableProducts = await db.Products
                .AsNoTracking()
                .Where(p => p.IsActive)
                .OrderBy(p => p.Name)
                .ThenBy(p => p.Code)
                .ToListAsync();

            model.Items = await db.PalletItems
                .AsNoTracking()
                .Include(i => i.Product)
                .Where(i => i.PalletId == selectedPalletId)
                .OrderBy(i => i.Product.Name)
                .ToListAsync();

            model.Movements = await (
                from movement in db.StockMovements.AsNoTracking()
                join user in db.Users.AsNoTracking()
                    on movement.CreatedByUserId equals user.Id
                    into movementUsers
                from user in movementUsers.DefaultIfEmpty()
                where movement.PalletItem.PalletId == selectedPalletId
                orderby movement.CreatedAtUtc descending,
                        movement.Id descending
                select new PalletMovementViewModel
                {
                    Id = movement.Id,
                    ProductId = movement.PalletItem.ProductId,
                    ConcurrencyStamp = movement.PalletItem.ConcurrencyStamp,

                CanCorrect =
                    movement.Type == StockMovementType.Receipt
                    && movement.PalletItem.Pallet.IsActive
                    && !movement.PalletItem.Pallet.IsCancelled
                    && !db.StockMovements.Any(other =>
                    other.ReversesMovementId == movement.Id
                    || other.CorrectsMovementId == movement.Id),

                    ProductName = movement.PalletItem.Product.Name,
                    ProductCode = movement.PalletItem.Product.Code,

                    Type = movement.Type,
                    BoxCount = movement.BoxCount,
                    LooseUnitCount = movement.LooseUnitCount,
                    UnitsPerBox = movement.UnitsPerBox,

                    CreatedByName = user == null
                        ? "Silinmiş kullanıcı"
                        : user.FullName != null && user.FullName != ""
                            ? user.FullName
                            : user.Email ?? "İsimsiz kullanıcı",

                    Notes = movement.Notes,
                    CreatedAtUtc = movement.CreatedAtUtc
                }
            ).ToListAsync();
        }
    }

    if (model.SelectedPallet is not null)
    {
        var historyPalletId = model.SelectedPallet.Id;

        ViewData["PalletTransferHistory"] = await db.Set<HistoryEntry>()
            .AsNoTracking()
            .Where(entry =>
                entry.PalletId == historyPalletId
                && entry.EventKey.StartsWith("pallet-transfer:"))
            .OrderByDescending(entry => entry.CreatedAtUtc)
            .ThenByDescending(entry => entry.Id)
            .ToListAsync();
    }

    return model;
}


    // MC_PALLET_QUICK_SAVE_V2
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissions.PalletsManage)]
    public async Task<IActionResult> QuickCreate(
        [FromForm] string? location,
        [FromForm] string? notes)
    {
        location = location?.Trim();
        notes = notes?.Trim();

        if (location?.Length > 100 || notes?.Length > 500)
        {
            return BadRequest(new
            {
                success = false,
                message = "Konum veya açıklama çok uzun."
            });
        }

        var pallet = new Pallet
        {
            Barcode = $"PLT-{Guid.NewGuid():N}".ToUpperInvariant(),
            Location = location,
            Notes = notes,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        try
        {
            db.Pallets.Add(pallet);
            await db.SaveChangesAsync();

            return Json(new
            {
                success = true,
                id = pallet.Id,
                barcode = pallet.Barcode,
                location = pallet.Location
            });
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Paketleme ekranından palet oluşturulamadı.");

            return StatusCode(500, new
            {
                success = false,
                message = "Palet kaydedilemedi."
            });
        }
    }
    private static string? CleanText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}