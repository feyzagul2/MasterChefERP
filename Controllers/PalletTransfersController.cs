using System.Security.Claims;
using MasterChefTakip.Data;
using MasterChefTakip.Models;
using MasterChefTakip.Services;
using MasterChefTakip.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

[Authorize(Policy = AppPermissions.PalletsTransfer)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class PalletTransfersController(
    ApplicationDbContext db,
    ILogger<PalletTransfersController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int? sourcePalletId)
    {
        return View(await BuildAsync(new PalletTransferViewModel
        {
            SourcePalletId = sourcePalletId
        }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Transfer(PalletTransferViewModel model)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Challenge();

        if (!Guid.TryParseExact(model.RequestId, "N", out _))
            ModelState.AddModelError("", "Form geçersiz. Sayfayı yeniden açın.");

        if (model.BoxCount == 0 && model.UnitCount == 0)
            ModelState.AddModelError("", "Taşınacak koli veya adedi girin.");

        if (!ModelState.IsValid)
            return await ErrorAsync(model, "Girilen bilgileri kontrol edin.");

        var key = $"pallet-transfer:{model.RequestId}";

        try
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync();

            if (await db.Set<HistoryEntry>().AnyAsync(
                h => h.EventKey == key + ":out"))
                return AlreadySaved(model);

            var source = await db.PalletItems
                .Include(s => s.Pallet)
                .Include(s => s.Product)
                .SingleOrDefaultAsync(s => s.Id == model.SourceStockId);

            var destination = await db.Pallets
                .SingleOrDefaultAsync(p => p.Id == model.DestinationPalletId);

            if (source is null || destination is null)
                return await ErrorAsync(model, "Ürün veya hedef palet bulunamadı.");

            if (!source.Product.IsActive
                || !source.Pallet.IsActive || source.Pallet.IsCancelled
                || !destination.IsActive || destination.IsCancelled)
                return await ErrorAsync(model, "Aktif ürün ve aktif paletler seçin.");

            if (source.PalletId == destination.Id)
                return await ErrorAsync(model, "Kaynak ve hedef palet farklı olmalı.");

            if (!string.Equals(
                source.ConcurrencyStamp, model.ConcurrencyStamp,
                StringComparison.Ordinal))
                return await ErrorAsync(model,
                    "Stok değişmiş. Formu yenileyip miktarı kontrol edin.");

            if (model.BoxCount > source.BoxCount)
                return await ErrorAsync(model, "Kapalı koli miktarı stoktan fazla.");

            var before = StockText(source);
            var boxes = source.BoxCount - model.BoxCount;
            var loose = source.LooseUnitCount;
            var open = source.OpenBoxUnitCount;
            var openCapacity = source.OpenBoxCapacity;
            var remaining = model.UnitCount;

            var take = Math.Min(remaining, loose);
            loose -= take;
            remaining -= take;

            take = Math.Min(remaining, open);
            open -= take;
            remaining -= take;

            if (open == 0)
                openCapacity = null;

            if (remaining > 0)
            {
                if (source.UnitsPerBox is not int capacity || capacity <= 0)
                    return await ErrorAsync(model,
                        "Tekli/açık stok yetersiz. Kapalı koliden adet almak "
                        + "için koli içi adet bilgisi bilinmeli.");

                var needed = (int)(((long)remaining + capacity - 1) / capacity);

                if (needed > boxes)
                    return await ErrorAsync(model, "Toplam miktar stoktan fazla.");

                boxes -= needed;
                open = (int)((long)needed * capacity - remaining);
                openCapacity = open > 0 ? capacity : null;
            }

            source.BoxCount = boxes;
            source.LooseUnitCount = loose;
            source.OpenBoxUnitCount = open;
            source.OpenBoxCapacity = openCapacity;
            source.ConcurrencyStamp = Guid.NewGuid().ToString("N");

            var target = new PalletItem
            {
                PalletId = destination.Id,
                ProductId = source.ProductId,
                BoxCount = model.BoxCount,
                LooseUnitCount = model.UnitCount,
                UnitsPerBox = source.UnitsPerBox,
                OpenBoxUnitCount = 0,
                OpenBoxCapacity = null
            };

            db.PalletItems.Add(target);

            var userName = await db.Users
                .Where(u => u.Id == userId)
                .Select(u => u.FullName)
                .SingleOrDefaultAsync()
                ?? User.Identity?.Name ?? "Personel";

            var now = DateTime.UtcNow;
            var quantity = model.BoxCount > 0
                ? $"{model.BoxCount} kapalı koli + {model.UnitCount} adet"
                : $"{model.UnitCount} adet";

            if (model.BoxCount > 0 && source.UnitsPerBox is > 0)
            {
                var total = (long)model.BoxCount * source.UnitsPerBox.Value
                    + model.UnitCount;
                quantity += $" (toplam {total} ürün)";
            }

            var reference = $"{source.Pallet.Barcode} → {destination.Barcode}";
            var notes =
                $"Kaynak konum: {source.Pallet.Location ?? "—"}\n"
                + $"Hedef konum: {destination.Location ?? "—"}\n"
                + "Toplam stok değişmedi.\n"
                + (model.UnitCount > 0
                    ? "Adetle taşınan ürünler hedefte tekli stok oldu.\n" : "")
                + (model.Notes?.Trim() ?? "");

            HistoryEntry Entry(
                string suffix, string title, int palletId,
                string oldValue, string newValue)
            {
                var entry = new HistoryEntry
                {
                    EventKey = key + suffix,
                    CreatedAtUtc = now,
                    UserId = userId,
                    UserName = userName,
                    Category = "Stok",
                    Title = title,
                    ProductName = source.Product.Name,
                    ProductCode = source.Product.Code,
                    Quantity = quantity,
                    Reference = reference,
                    Notes = notes,
                    BeforeValue = oldValue,
                    AfterValue = newValue,
                    PalletId = palletId
                };

                entry.SearchText = HistoryWriter.Normalize(
                    $"{userName} {entry.Category} {title} "
                    + $"{entry.ProductName} {entry.ProductCode} "
                    + $"{reference} {quantity} {notes}");

                return entry;
            }

            db.Set<HistoryEntry>().AddRange(
                Entry(":out", "Başka palete taşındı · Kaynak çıkışı",
                    source.PalletId, before, StockText(source)),
                Entry(":in", "Başka paletten alındı · Hedef girişi",
                    destination.Id, "Yeni stok kaydı: 0 adet", StockText(target)));

            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["PalletTransferSuccess"] =
                $"{source.Product.Name}: {quantity} taşındı. Toplam stok değişmedi.";

            return TransferResult(model);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogWarning(exception, "Taşıma sırasında stok değişti.");
            return await ErrorAsync(model,
                "Stok değişti. Formu yenileyin. Taşıma yapılmadı.");
        }
        catch (DbUpdateException exception)
        {
            db.ChangeTracker.Clear();

            if (await db.Set<HistoryEntry>().AnyAsync(
                h => h.EventKey == key + ":out"))
                return AlreadySaved(model);

            logger.LogError(exception, "Taşıma kaydedilemedi.");
            return await ErrorAsync(model,
                "Taşıma kaydedilemedi. Stok değişiklikleri geri alındı.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Taşıma başarısız.");
            return await ErrorAsync(model,
                "Taşıma yapılamadı. Stok değişiklikleri geri alındı.");
        }
    }

    private IActionResult TransferResult(PalletTransferViewModel model)
    {
        var url = Url.Action(
            "Index",
            "Pallets",
            new { palletId = model.SourcePalletId })
            + "#pallet-content";

        if (Request.Headers["X-Requested-With"].ToString()
            == "XMLHttpRequest")
        {
            return Json(new
            {
                success = true,
                message = TempData.Peek("PalletTransferSuccess")?.ToString(),
                url
            });
        }

        return Redirect(url);
    }

    private IActionResult AlreadySaved(PalletTransferViewModel model)
    {
        TempData["PalletTransferSuccess"] =
            "Bu taşıma zaten kaydedildi. Stok ikinci kez değiştirilmedi.";
        return TransferResult(model);
    }

    private async Task<IActionResult> ErrorAsync(
        PalletTransferViewModel model, string message)
    {
        db.ChangeTracker.Clear();
        ModelState.AddModelError("", message);

        if (Request.Headers["X-Requested-With"].ToString()
            == "XMLHttpRequest")
        {
            var errors = ModelState.Values
                .SelectMany(value => value.Errors)
                .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                    ? "Girilen bilgileri kontrol edin."
                    : error.ErrorMessage)
                .Distinct()
                .ToArray();

            return BadRequest(new
            {
                success = false,
                message = string.Join(" ", errors)
            });
        }
        return View("Index", await BuildAsync(model));
    }

    private async Task<PalletTransferViewModel> BuildAsync(
        PalletTransferViewModel model)
    {
        var query = db.PalletItems.AsNoTracking()
            .Include(s => s.Product)
            .Include(s => s.Pallet)
            .Where(s => s.Product.IsActive
                && s.Pallet.IsActive && !s.Pallet.IsCancelled
                && (s.BoxCount > 0
                    || s.LooseUnitCount > 0 || s.OpenBoxUnitCount > 0));

        if (model.SourcePalletId is int palletId)
            query = query.Where(s => s.PalletId == palletId);

        var stocks = await query.OrderBy(s => s.Product.Name)
            .ThenBy(s => s.Id).ToListAsync();

        model.Stocks = stocks.Select(s => new PalletTransferStockOption
        {
            Id = s.Id,
            PalletId = s.PalletId,
            Label = $"{s.Product.Name} · {s.Product.Code} — "
                + $"{s.Pallet.Location ?? "Konum yok"} "
                + $"· Palet #{s.PalletId} · Stok #{s.Id}",
            ConcurrencyStamp = s.ConcurrencyStamp,
            BoxCount = s.BoxCount,
            IndividualUnits = (long)s.LooseUnitCount + s.OpenBoxUnitCount,
            UnitsPerBox = s.UnitsPerBox
        }).ToList();

        model.Destinations = await db.Pallets.AsNoTracking()
            .Where(p => p.IsActive && !p.IsCancelled)
            .OrderBy(p => p.Location).ThenBy(p => p.Id).ToListAsync();

        return model;
    }

    private static string StockText(PalletItem stock)
    {
        var text = $"{stock.BoxCount} kapalı koli, "
            + $"{stock.OpenBoxUnitCount} açık koli adedi, "
            + $"{stock.LooseUnitCount} tekli adet";

        if (stock.BoxCount == 0 || stock.UnitsPerBox is > 0)
        {
            var total = (long)stock.BoxCount * (stock.UnitsPerBox ?? 0)
                + stock.OpenBoxUnitCount + stock.LooseUnitCount;
            text += $" · Toplam {total} ürün";
        }

        return text;
    }
}