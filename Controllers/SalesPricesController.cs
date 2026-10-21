using System.Globalization;
using MasterChefTakip.Data;
using MasterChefTakip.Models;
using MasterChefTakip.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

[Authorize]
public class SalesPricesController : Controller
{
    private readonly ApplicationDbContext _db;

    public SalesPricesController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(
        int id, string? salesName, string? price, string? vatRate)
    {
        if (!OperationAuthorization.Can(User, AppPermissions.ProductsPrices))
            return Forbid();

        var sale = await _db.ProductSalesOptions
            .FirstOrDefaultAsync(x => x.Id == id);

        if (sale == null)
            return NotFound();

        salesName = salesName?.Trim();

        if (string.IsNullOrWhiteSpace(salesName) ||
            salesName.Length > 200 ||
            !ParseNumber(price, out var included) ||
            !ParseNumber(vatRate, out var vat) ||
            included < 0 || included > 999999999m ||
            vat < 0 || vat > 100)
        {
            TempData["ProductError"] =
                "Satış adı, fiyat veya KDV oranı geçersiz.";
            return RedirectToAction("Index", "Products");
        }

        included = decimal.Round(included, 2, MidpointRounding.AwayFromZero);
        vat = decimal.Round(vat, 2, MidpointRounding.AwayFromZero);

        sale.SalesName = salesName;
        sale.SalesPriceVatIncluded = included;
        sale.VatRatePercent = vat;
        sale.SalesPriceVatExcluded = decimal.Round(
            included / (1 + vat / 100m),
            2,
            MidpointRounding.AwayFromZero);

        if (string.IsNullOrWhiteSpace(sale.Currency))
            sale.Currency = "TL";

        await _db.SaveChangesAsync();

        TempData["ProductSuccess"] =
            $"{sale.SalesCode} satış kaydı güncellendi.";

        return RedirectToAction("Index", "Products");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(int id, bool isActive)
    {
        if (!OperationAuthorization.Can(User, AppPermissions.ProductsPrices))
            return Forbid();

        var sale = await _db.ProductSalesOptions
            .FirstOrDefaultAsync(x => x.Id == id);

        if (sale == null)
            return NotFound();

        sale.IsActive = isActive;

        await _db.SaveChangesAsync();

        TempData["ProductSuccess"] =
            $"{sale.SalesCode} satış kaydı " +
            (isActive ? "aktif yapıldı." : "pasife alındı.");

        return RedirectToAction("Index", "Products");
    }

    private static bool ParseNumber(string? value, out decimal result)
    {
        result = 0;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        value = value.Trim().Replace(" ", "");

        if (value.Contains('.') && value.Contains(','))
            return false;

        return decimal.TryParse(
            value.Replace(',', '.'),
            NumberStyles.AllowDecimalPoint |
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out result);
    }
}