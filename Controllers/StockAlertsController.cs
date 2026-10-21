using MasterChefTakip.Data;
using MasterChefTakip.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

[Authorize(Roles = AppRoles.Admin)]
public class StockAlertsController(
    ApplicationDbContext db,
    ILogger<StockAlertsController> logger) : Controller
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveThreshold(
        int productId,
        int? threshold,
        string? concurrencyStamp)
    {
        if (!ModelState.IsValid || threshold is < 0)
        {
            TempData["StockAlertError"] =
                "Sınır, sıfır veya pozitif tam sayı olmalıdır.";

            return RedirectToAction("Index", "Home");
        }

        try
        {
            var product = await db.Products
                .SingleOrDefaultAsync(p => p.Id == productId);

            if (product is null)
            {
                return NotFound();
            }

            if (!string.Equals(
                product.ConcurrencyStamp,
                concurrencyStamp,
                StringComparison.Ordinal))
            {
                TempData["StockAlertError"] =
                    "Ürün değişti. Sayfayı yenileyip tekrar deneyin.";

                return RedirectToAction("Index", "Home");
            }

            product.LowStockThresholdUnits = threshold;
            product.ConcurrencyStamp =
                Guid.NewGuid().ToString("N");

            await db.SaveChangesAsync();

            TempData["StockAlertSuccess"] =
                threshold.HasValue
                    ? $"{product.Name}: düşük stok sınırı kaydedildi."
                    : $"{product.Name}: düşük stok uyarısı kapatıldı.";
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["StockAlertError"] =
                "Ürün başka bir işlemde değişti. Sayfayı yenileyin.";
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Düşük stok sınırı kaydedilemedi.");

            TempData["StockAlertError"] =
                "Düşük stok sınırı kaydedilemedi.";
        }

        return RedirectToAction("Index", "Home");
    }
}