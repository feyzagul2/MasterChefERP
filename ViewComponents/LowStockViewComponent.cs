using MasterChefTakip.Data;
using MasterChefTakip.Models;
using MasterChefTakip.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.ViewComponents;

public class LowStockViewComponent(
    ApplicationDbContext db) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (!UserClaimsPrincipal.IsInRole(AppRoles.Admin))
        {
            return Content(string.Empty);
        }

        var products = await db.Products
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Code,
                p.ConcurrencyStamp,
                p.LowStockThresholdUnits
            })
            .ToListAsync();

        var stocks = await db.PalletItems
            .AsNoTracking()
            .Where(i =>
                i.Pallet.IsActive &&
                !i.Pallet.IsCancelled &&
                i.Product.IsActive)
            .Select(i => new
            {
                i.ProductId,
                i.BoxCount,
                i.LooseUnitCount,
                i.OpenBoxUnitCount,
                i.UnitsPerBox
            })
            .ToListAsync();

        var groupedStocks = stocks.ToLookup(i => i.ProductId);
        var model = new LowStockViewModel();

        foreach (var product in products)
        {
            long total = 0;
            var known = true;

            foreach (var stock in groupedStocks[product.Id])
            {
                if (stock.BoxCount < 0 ||
                    stock.LooseUnitCount < 0 ||
                    stock.OpenBoxUnitCount < 0 ||
                    (stock.BoxCount > 0 &&
                     stock.UnitsPerBox is not > 0))
                {
                    known = false;
                    break;
                }

                total = checked(
                    total
                    + (long)stock.BoxCount
                        * stock.UnitsPerBox.GetValueOrDefault()
                    + stock.LooseUnitCount
                    + stock.OpenBoxUnitCount);
            }

            model.Products.Add(new LowStockProductViewModel
            {
                ProductId = product.Id,
                Name = product.Name,
                Code = product.Code,
                ConcurrencyStamp = product.ConcurrencyStamp,
                Threshold = product.LowStockThresholdUnits,
                TotalUnits = known ? total : null
            });
        }

        return View(model);
    }
}