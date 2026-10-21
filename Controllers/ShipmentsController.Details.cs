
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MasterChefTakip.Models;

namespace MasterChefTakip.Controllers;

public partial class ShipmentsController
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissions.ShipmentsManage)]
    public async Task<IActionResult> UpdateDetails(
        int shipmentId,
        string? recipientName,
        string? recipientPhone,
        string? deliveryAddress,
        string? cargoCompany,
        string? trackingNumber,
        string? notes,
        string? concurrencyStamp)
    {
        var shipment = await db.Shipments
            .FirstOrDefaultAsync(s => s.Id == shipmentId);

        if (shipment is null) return NotFound();

        if (shipment.Status != ShipmentStatus.Draft)
        {
            TempData["ShipmentError"] = "Yalnizca taslak sevkiyat duzenlenebilir.";
            return RedirectToAction(nameof(Index), new { shipmentId });
        }

        if (!string.Equals(shipment.ConcurrencyStamp,
            concurrencyStamp, StringComparison.Ordinal))
        {
            TempData["ShipmentError"] = "Kayit degisti. Sayfayi yenileyin.";
            return RedirectToAction(nameof(Index), new { shipmentId });
        }

        recipientName = recipientName?.Trim();

        if (string.IsNullOrWhiteSpace(recipientName) ||
            recipientName.Length > 200 ||
            recipientPhone?.Trim().Length > 50 ||
            deliveryAddress?.Trim().Length > 500 ||
            cargoCompany?.Trim().Length > 100 ||
            trackingNumber?.Trim().Length > 100 ||
            notes?.Trim().Length > 500)
        {
            TempData["ShipmentError"] = "Bilgileri kontrol edin.";
            return RedirectToAction(nameof(Index), new { shipmentId });
        }

        static string? Normalize(string? value)
        {
            var result = value?.Trim();
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }

        shipment.RecipientName = recipientName;
        shipment.RecipientPhone = Normalize(recipientPhone);
        shipment.DeliveryAddress = Normalize(deliveryAddress);
        shipment.CargoCompany = Normalize(cargoCompany);
        shipment.TrackingNumber = Normalize(trackingNumber);
        shipment.Notes = Normalize(notes);
        shipment.ConcurrencyStamp = Guid.NewGuid().ToString("N");

        try
        {
            await db.SaveChangesAsync();
            TempData["ShipmentSuccess"] = "Musteri ve teslimat bilgileri guncellendi.";
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["ShipmentError"] = "Kayit degisti. Tekrar deneyin.";
        }

        return RedirectToAction(nameof(Index), new { shipmentId });
    }
}
