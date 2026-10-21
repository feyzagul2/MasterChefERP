using System.Globalization;
using MasterChefTakip.Data;
using MasterChefTakip.Models;
using MasterChefTakip.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

[Authorize(Policy = AppPermissions.ProductsView)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ProductsController(
    ApplicationDbContext db,
    IAuthorizationService authorizationService,
    ILogger<ProductsController> logger) : Controller
{
        [HttpGet]
    [Authorize(Policy = AppPermissions.PalletsView)]
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
    public async Task<IActionResult> Index(
        string? form = null,
        int? editId = null,
        string? barcode = null)
    {
        if (editId is int id)
        {
            if (!await CanManageAsync())
                return Forbid();

            var product = await db.Products.AsNoTracking()
                .SingleOrDefaultAsync(p => p.Id == id);

            if (product is null)
                return NotFound();

            var model = new CreateProductViewModel
            {
                Id = product.Id,
                ConcurrencyStamp = product.ConcurrencyStamp,
                Code = product.Code,
                Barcode = product.Barcode,
                Name = product.Name,
                PackagingType = product.PackagingType,
                DefaultUnitsPerBox = product.DefaultUnitsPerBox,
                DefaultUnitNetWeightGrams =
                    product.DefaultUnitNetWeightGrams?
                        .ToString("0.###", CultureInfo.InvariantCulture),
                UnitPrice = product.UnitPrice?
                    .ToString("0.##", CultureInfo.InvariantCulture),
                PriceBasis = product.PriceBasis
            };

            return View(await BuildPageAsync(model, true));
        }

        var newForm = new CreateProductViewModel();

        if (form == "new" && !string.IsNullOrWhiteSpace(barcode))
        {
            if (barcode.Trim().Length > 128)
                return BadRequest("Barkod en fazla 128 karakter olabilir.");

            newForm.Barcode = barcode.Trim();
        }

        return View(await BuildPageAsync(newForm, form == "new"));
    }

    [HttpGet]
    public async Task<IActionResult> FindByBarcode(string? barcode)
    {
        barcode = barcode?.Trim();

        if (string.IsNullOrWhiteSpace(barcode) || barcode.Length > 128)
            return BadRequest(new { message = "Geçerli bir barkod girin." });

        var matches = await db.Products.AsNoTracking()
            .Where(p => p.Barcode == barcode)
            .Take(2)
            .ToListAsync();

        if (matches.Count > 1)
        {
            return Conflict(new
            {
                message = "Bu barkod birden fazla üründe kayıtlı. Kayıtları kontrol edin."
            });
        }

        var product = matches.FirstOrDefault();

        if (product is null)
            return Json(new { found = false, barcode });

        return Json(new
        {
            found = true,
            name = product.Name,
            active = product.IsActive,
            url = Url.Action(nameof(Index), "Products")
                + "#product-" + product.Id
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissions.ProductsCreate)]
    public Task<IActionResult> Create(
        [Bind(Prefix = "Form")] CreateProductViewModel model)
    {
        model.Id = 0;
        ModelState.Remove("Form.Id");

        return SaveAsync(model, false);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissions.ProductsManage)]
    public Task<IActionResult> Edit(
        [Bind(Prefix = "Form")] CreateProductViewModel model)
    {
        if (model.Id <= 0)
            ModelState.AddModelError(string.Empty, "Geçerli bir ürün seçin.");

        return SaveAsync(model, true);
    }

    private async Task<IActionResult> SaveAsync(
        CreateProductViewModel model,
        bool editing)
    {
        var weight = ReadDecimal(
            model.DefaultUnitNetWeightGrams,
            "Form.DefaultUnitNetWeightGrams",
            0.001m,
            "Net ağırlık 0,001 ile 1.000.000.000 gram arasında olmalıdır.");

        var price = ReadDecimal(
            model.UnitPrice,
            "Form.UnitPrice",
            0m,
            "Fiyat 0 ile 1.000.000.000 TL arasında olmalıdır.");

        if (model.PriceBasis is null ||
            !Enum.IsDefined(typeof(ProductPriceBasis), model.PriceBasis.Value))
        {
            ModelState.AddModelError(
                "Form.PriceBasis",
                "Adet fiyatı veya koli fiyatını seçin.");
        }

        if (!ModelState.IsValid)
            return View("Index", await BuildPageAsync(model, true));

        try
        {
            Product product;

            if (editing)
            {
                var existing = await db.Products
                    .SingleOrDefaultAsync(p => p.Id == model.Id);

                if (existing is null)
                    return NotFound();

                product = existing;

                if (!StampMatches(product, model.ConcurrencyStamp))
                {
                    ModelState.AddModelError(
                        string.Empty,
                        "Ürün başka bir işlemde değiştirildi. Listeye dönüp düzenlemeyi yeniden açın.");

                    return View("Index", await BuildPageAsync(model, true));
                }
            }
            else
            {
                product = new Product
                {
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow
                };
            }

            var code = string.IsNullOrWhiteSpace(model.Code)
                ? editing
                    ? product.Code
                    : $"MC-{Guid.NewGuid():N}".ToUpperInvariant()
                : model.Code.Trim();

            var barcode = string.IsNullOrWhiteSpace(model.Barcode)
                ? null
                : model.Barcode.Trim();

            if (await db.Products.AnyAsync(
                p => p.Code == code && p.Id != product.Id))
            {
                ModelState.AddModelError(
                    "Form.Code", "Bu ürün kodu zaten kayıtlı.");
            }

            if (barcode is not null &&
                await db.Products.AnyAsync(
                    p => p.Barcode == barcode && p.Id != product.Id))
            {
                ModelState.AddModelError(
                    "Form.Barcode", "Bu barkod başka bir ürüne kayıtlı.");
            }

            if (!ModelState.IsValid)
                return View("Index", await BuildPageAsync(model, true));

            product.Code = code;
            product.Barcode = barcode;
            product.Name = model.Name.Trim();
            product.PackagingType =
                model.PackagingType?.Trim() ?? string.Empty;
            product.DefaultUnitsPerBox = model.DefaultUnitsPerBox;
            product.DefaultUnitNetWeightGrams = weight;
            product.UnitPrice = price;
            product.PriceBasis = model.PriceBasis.GetValueOrDefault();
            product.ConcurrencyStamp = Guid.NewGuid().ToString("N");

            if (!editing)
                db.Products.Add(product);

            await db.SaveChangesAsync();

            TempData["ProductSuccess"] = editing
                ? "Ürün bilgileri güncellendi."
                : "Ürün başarıyla kaydedildi.";

            return RedirectToAction(nameof(Index));
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();

            ModelState.AddModelError(
                string.Empty,
                "Ürün başka bir işlemde değiştirildi veya silindi. Listeyi yenileyin.");
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is SqliteException
                { SqliteExtendedErrorCode: 2067 })
        {
            db.ChangeTracker.Clear();

            ModelState.AddModelError(
                string.Empty,
                "Ürün kodu veya barkod başka bir kayıtta kullanılıyor.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ürün kaydedilemedi.");
            db.ChangeTracker.Clear();

            ModelState.AddModelError(
                string.Empty,
                "Ürün kaydedilemedi. Lütfen tekrar deneyin.");
        }

        return View("Index", await BuildPageAsync(model, true));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissions.ProductsManage)]
    public async Task<IActionResult> SetActive(
        int id, string? concurrencyStamp, bool? isActive)
    {
        if (!ModelState.IsValid || id <= 0 || isActive is not bool desired)
            return ActionError("İşlem bilgileri geçersiz.");

        try
        {
            var product = await db.Products
                .SingleOrDefaultAsync(p => p.Id == id);

            if (product is null)
                return NotFound();

            if (!StampMatches(product, concurrencyStamp))
                return ActionError("Ürün değiştirildi. Listeyi yenileyin.");

            product.IsActive = desired;
            product.ConcurrencyStamp = Guid.NewGuid().ToString("N");

            await db.SaveChangesAsync();

            TempData["ProductSuccess"] = desired
                ? "Ürün aktif hale getirildi."
                : "Ürün pasife alındı.";

            return RedirectToAction(nameof(Index));
        }
        catch (DbUpdateConcurrencyException)
        {
            return ActionError("Ürün değiştirildi veya silindi. Listeyi yenileyin.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ürün durumu değiştirilemedi.");
            return ActionError("Durum değiştirilemedi. Lütfen tekrar deneyin.");
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissions.ProductsManage)]
    public async Task<IActionResult> Delete(int id, string? concurrencyStamp)
    {
        if (!ModelState.IsValid || id <= 0)
            return ActionError("İşlem bilgileri geçersiz.");

        try
        {
            var product = await db.Products
                .SingleOrDefaultAsync(p => p.Id == id);

            if (product is null)
                return NotFound();

            if (!StampMatches(product, concurrencyStamp))
                return ActionError("Ürün değiştirildi. Listeyi yenileyin.");

            db.Products.Remove(product);
            await db.SaveChangesAsync();

            TempData["ProductSuccess"] = "Ürün silindi.";

            return RedirectToAction(nameof(Index));
        }
        catch (DbUpdateConcurrencyException)
        {
            return ActionError("Ürün değiştirildi veya silindi. Listeyi yenileyin.");
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is SqliteException
                { SqliteExtendedErrorCode: 787 })
        {
            return ActionError(
                "Başka kayıtlarda kullanılan ürün silinemez. Ürünü pasife alın.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ürün silinemedi.");
            return ActionError("Ürün silinemedi. Lütfen tekrar deneyin.");
        }
    }

    private static bool StampMatches(Product product, string? stamp) =>
        string.Equals(
            product.ConcurrencyStamp,
            stamp ?? string.Empty,
            StringComparison.Ordinal);

    private IActionResult ActionError(string message)
    {
        db.ChangeTracker.Clear();
        TempData["ProductError"] = message;

        return RedirectToAction(nameof(Index));
    }

    private decimal? ReadDecimal(
        string? input, string key, decimal minimum, string message)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        if (!decimal.TryParse(
                input.Trim().Replace(',', '.'),
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var value)
            || value < minimum || value > 1_000_000_000m)
        {
            ModelState.AddModelError(key, message);
            return null;
        }

        return value;
    }

    private async Task<bool> CanManageAsync() =>
        (await authorizationService.AuthorizeAsync(
            User, null, AppPermissions.ProductsManage)).Succeeded;

    private async Task<ProductIndexViewModel> BuildPageAsync(
        CreateProductViewModel? form = null,
        bool showForm = false)
    {
        var create = await authorizationService.AuthorizeAsync(
            User, null, AppPermissions.ProductsCreate);

        var manage = await CanManageAsync();

        var products = await db.Products.AsNoTracking()
            .OrderBy(p => p.Name)
            .ThenBy(p => p.Code)
            .ToListAsync();

        if (!MasterChefTakip.Services.OperationAuthorization.Can(
            User, AppPermissions.ProductsPrices))
        {
            foreach (var product in products)
            {
                product.UnitPrice = null;
            }
        }

        return new ProductIndexViewModel
        {
            Products = products,

            Form = form ?? new CreateProductViewModel(),
            CanCreate = create.Succeeded,
            CanManage = manage,
            ShowForm = showForm &&
                (form?.Id > 0 ? manage : create.Succeeded)
        };
    }
}