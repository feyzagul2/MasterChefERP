using System.Security.Claims;
using MasterChefTakip.Data;
using MasterChefTakip.Models;
using MasterChefTakip.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

[Authorize(Policy = AppPermissions.PackagingManage)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class PackagingController(
    ApplicationDbContext db,
    ILogger<PackagingController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        return View(await BuildPageAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(
        [Bind(Prefix = "Form")] PackagingFormViewModel form)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var rows = form.Rows ?? [];
                for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];

            if (row.BoxCount is not > 0)
            {
                ModelState.AddModelError(
                    $"Form.Rows[{index}].BoxCount",
                    $"{index + 1}. satır: Koli sayısı en az 1 olmalıdır.");
            }

            if (row.UnitsPerBox is not > 0)
            {
                ModelState.AddModelError(
                    $"Form.Rows[{index}].UnitsPerBox",
                    $"{index + 1}. satır: Her kolideki adet en az 1 olmalıdır.");
            }
        }

        if (rows.Count == 0 || rows.Count > 100)
        {
            ModelState.AddModelError(
                string.Empty,
                "Bir kayıtta 1 ile 100 arasında ürün satırı girin.");
        }

        if (!Guid.TryParseExact(form.RequestId, "N", out _))
        {
            ModelState.AddModelError(
                string.Empty,
                "Form bilgisi geçersiz. Sayfayı yeniden açın.");
        }

        if (!ModelState.IsValid)
        {
            return View("Index", await BuildPageAsync(form));
        }

        try
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync();

            // Aynı form tekrar gönderilmişse stok ikinci kez eklenmez.
            if (await db.PackagingBatches
                .AnyAsync(batch => batch.Id == form.RequestId))
            {
                TempData["PackagingSuccess"] =
                    "Bu paketleme daha önce kaydedilmiş. Stok tekrar eklenmedi.";

                return RedirectToAction(nameof(Index));
            }

            var productIds = rows.Select(row => row.ProductId)
                .Distinct()
                .ToList();

            var palletIds = rows.Select(row => row.PalletId)
                .Distinct()
                .ToList();

            var products = await db.Products
                .Where(product => productIds.Contains(product.Id))
                .ToDictionaryAsync(product => product.Id);

            var pallets = await db.Pallets
                .Where(pallet => palletIds.Contains(pallet.Id))
                .ToDictionaryAsync(pallet => pallet.Id);

            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];

                if (!products.TryGetValue(row.ProductId, out var product)
                    || !product.IsActive)
                {
                    ModelState.AddModelError(
                        $"Form.Rows[{index}].ProductId",
                        $"{index + 1}. satır: Aktif bir ürün seçin.");
                }

                if (!pallets.TryGetValue(row.PalletId, out var pallet)
                    || !pallet.IsActive
                    || pallet.IsCancelled)
                {
                    ModelState.AddModelError(
                        $"Form.Rows[{index}].PalletId",
                        $"{index + 1}. satır: Aktif bir palet seçin.");
                }
            }

            if (!ModelState.IsValid)
            {
                await transaction.RollbackAsync();
                db.ChangeTracker.Clear();

                return View("Index", await BuildPageAsync(form));
            }

            var now = DateTime.UtcNow;

            db.PackagingBatches.Add(new PackagingBatch
            {
                Id = form.RequestId,
                CreatedByUserId = userId,
                CreatedAtUtc = now
            });

            long totalBoxes = 0;
            long totalUnits = 0;

            foreach (var row in rows)
            {
                var boxes = row.BoxCount.GetValueOrDefault();
                var capacity = row.UnitsPerBox.GetValueOrDefault();

                // Her giriş ayrı tutulur; eski kolinin kapasitesi değiştirilmez.
                var item = new PalletItem
                {
                    ProductId = row.ProductId,
                    PalletId = row.PalletId,
                    BoxCount = boxes,
                    UnitsPerBox = capacity,
                    LooseUnitCount = 0,
                    OpenBoxUnitCount = 0,
                    OpenBoxCapacity = null
                };

                db.PalletItems.Add(item);

                db.StockMovements.Add(new StockMovement
                {
                    PalletItem = item,
                    PackagingBatchId = form.RequestId,
                    Type = StockMovementType.Receipt,
                    BoxCount = boxes,
                    LooseUnitCount = 0,
                    UnitsPerBox = capacity,
                    CreatedByUserId = userId,
                    CreatedAtUtc = now,
                    Notes = string.IsNullOrWhiteSpace(form.Notes)
                        ? null
                        : form.Notes.Trim()
                });

                totalBoxes = checked(totalBoxes + boxes);
                totalUnits = checked(
                    totalUnits + (long)boxes * capacity);
            }

            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["PackagingSuccess"] =
                $"{totalBoxes} koli, toplam {totalUnits} adet ürün "
                + "hazırlandı ve stoğa eklendi.";

            return RedirectToAction(nameof(Index));
        }
        catch (DbUpdateException exception)
        {
            db.ChangeTracker.Clear();

            // Eş zamanlı ikinci kaydetme isteğinin sonucu kontrol edilir.
            if (await db.PackagingBatches
                .AnyAsync(batch => batch.Id == form.RequestId))
            {
                TempData["PackagingSuccess"] =
                    "Paketleme kaydedilmiş. Stok ikinci kez eklenmedi.";

                return RedirectToAction(nameof(Index));
            }

            logger.LogError(exception, "Paketleme kaydedilemedi.");

            ModelState.AddModelError(
                string.Empty,
                "Paketleme kaydedilemedi. Hiçbir satır stoğa eklenmedi.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Paketleme kaydedilemedi.");

            db.ChangeTracker.Clear();

            ModelState.AddModelError(
                string.Empty,
                "Paketleme kaydedilemedi. İşlem geri alındı.");
        }

        return View("Index", await BuildPageAsync(form));
    }

    private async Task<PackagingIndexViewModel> BuildPageAsync(
        PackagingFormViewModel? form = null)
    {
        return new PackagingIndexViewModel
        {
            Form = form ?? new PackagingFormViewModel(),

            Products = await db.Products
                .AsNoTracking()
                .Where(product => product.IsActive)
                .OrderBy(product => product.Name)
                .ThenBy(product => product.Code)
                .ToListAsync(),

            Pallets = await db.Pallets
                .AsNoTracking()
                .Where(pallet => pallet.IsActive && !pallet.IsCancelled)
                .OrderBy(pallet => pallet.Location)
                .ThenBy(pallet => pallet.Barcode)
                .ToListAsync()
        };
    }
}