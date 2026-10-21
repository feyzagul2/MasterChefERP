using Microsoft.AspNetCore.Authorization;
using MasterChefTakip.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

public partial class ShipmentsController
{
    [HttpGet]
    [Authorize(Policy = AppPermissions.ShipmentsLabels)]
    public async Task<IActionResult> Labels(int[]? ids)
    {
        if (!ModelState.IsValid ||
            ids is null ||
            ids.Length == 0 ||
            ids.Length > 100 ||
            ids.Any(id => id <= 0))
        {
            TempData["ShipmentError"] =
                "Yazdırmak için 1 ile 100 arasında sevkiyat seçin.";

            return RedirectToAction(nameof(Index));
        }

        var selectedIds = ids.Distinct().ToArray();

        var shipments = await db.Shipments
        .AsNoTracking()
        .Include(s => s.Items)
        .Include(s => s.Packages)
        .ThenInclude(package => package.Items)
        .ThenInclude(item => item.ShipmentItem)
        .AsSplitQuery()
        .Where(s => selectedIds.Contains(s.Id))
        .ToListAsync();

        if (shipments.Count != selectedIds.Length ||
            shipments.Any(s => s.Status is not (
                ShipmentStatus.Confirmed or
                ShipmentStatus.Shipped or
                ShipmentStatus.Delivered)))
        {
            TempData["ShipmentError"] =
                "Seçilen kayıtlardan biri bulunamadı veya "
                + "etiket yazdırmaya uygun değil. Listeyi yenileyin.";

            return RedirectToAction(nameof(Index));
        }

        var productCodes = shipments
            .SelectMany(s => s.Items)
            .Select(i => i.ProductCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var products = await db.Products
            .AsNoTracking()
            .Where(p => productCodes.Contains(p.Code))
            .ToListAsync();

        ViewData["LabelProducts"] = products.ToDictionary(
            p => p.Code,
            p => p,
            StringComparer.OrdinalIgnoreCase);

        var byId = shipments.ToDictionary(s => s.Id);

        var ordered = selectedIds
            .Select(id => byId[id])
            .ToList();

        return View("Labels", ordered);
    }
}