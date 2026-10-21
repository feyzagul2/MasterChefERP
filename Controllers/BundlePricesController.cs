using System.Globalization;
using MasterChefTakip.Data;
using MasterChefTakip.Models;
using MasterChefTakip.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

[Authorize]
public class BundlePricesController : Controller
{
    private readonly ApplicationDbContext _db;

    public BundlePricesController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(
        int id,
        string? salesName,
        string? price,
        string? vatRate)
    {
        if (!OperationAuthorization.Can(
            User,
            AppPermissions.ProductsPrices))
        {
            return Forbid();
        }

        var bundle = await _db.SalesBundles
            .FirstOrDefaultAsync(x => x.Id == id);

        if (bundle == null)
        {
            return NotFound();
        }

        salesName = salesName?.Trim();

        if (string.IsNullOrWhiteSpace(salesName) ||
            salesName.Length > 200 ||
            !TryDecimal(price, out var included) ||
            !TryDecimal(vatRate, out var vat) ||
            included < 0 ||
            included > 999999999m ||
            vat < 0 ||
            vat > 100)
        {
            TempData["ProductError"] =
                "Paket adı, fiyat veya KDV oranı geçersiz.";

            return RedirectToAction("Index", "Products");
        }

        included = decimal.Round(
            included,
            2,
            MidpointRounding.AwayFromZero);

        vat = decimal.Round(
            vat,
            2,
            MidpointRounding.AwayFromZero);

        var excluded = decimal.Round(
            included / (1 + vat / 100m),
            2,
            MidpointRounding.AwayFromZero);

        // Yalnizca secilen paketin adi ve fiyatlari guncellenir.
        // Satis kodu, paket icerigi ve stok verileri degismez.
        bundle.SalesName = salesName;
        bundle.SalesPriceVatIncluded = included;
        bundle.SalesPriceVatExcluded = excluded;
        bundle.VatRatePercent = vat;

        if (string.IsNullOrWhiteSpace(bundle.Currency))
        {
            bundle.Currency = "TL";
        }

        await _db.SaveChangesAsync();

        TempData["ProductSuccess"] =
            $"{bundle.SalesCode} satış paketinin bilgileri güncellendi.";

        return RedirectToAction("Index", "Products");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(int id, bool isActive)
    {
        if (!OperationAuthorization.Can(
            User, AppPermissions.ProductsPrices))
        {
            return Forbid();
        }

        var bundle = await _db.SalesBundles
            .FirstOrDefaultAsync(x => x.Id == id);

        if (bundle == null)
        {
            return NotFound();
        }

        bundle.IsActive = isActive;

        await _db.SaveChangesAsync();

        TempData["ProductSuccess"] =
            $"{bundle.SalesCode} satış paketi " +
            (isActive ? "aktif yapıldı." : "pasife alındı.");

        return RedirectToAction("Index", "Products");
    }
    private static bool TryDecimal(
        string? value,
        out decimal result)
    {
        result = 0;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        value = value.Trim().Replace(" ", "");

        if (value.Contains('.') && value.Contains(','))
        {
            return false;
        }

        return decimal.TryParse(
            value.Replace(',', '.'),
            NumberStyles.AllowDecimalPoint |
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out result);
    }
}