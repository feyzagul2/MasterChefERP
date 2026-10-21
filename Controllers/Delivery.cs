using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using MasterChefTakip.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

public partial class ShipmentsController
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissions.ShipmentsManage)]
    public async Task<IActionResult> MarkShipped(
        int id,
        string? concurrencyStamp)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
            return Challenge();

        if (!ModelState.IsValid)
            return Failure(id, "Gönderim bilgileri geçersiz.");

        try
        {
            var shipment = await db.Shipments
                .SingleOrDefaultAsync(s => s.Id == id);

            if (shipment is null)
                return NotFound();

            if (shipment.Status is ShipmentStatus.Shipped
                or ShipmentStatus.Delivered)
            {
                return DeliverySuccess(
                    id,
                    "Gönderim daha önce kaydedilmiş. Stok tekrar düşülmedi.");
            }

            if (shipment.Status != ShipmentStatus.Confirmed)
            {
                return Failure(
                    id,
                    "Önce sevkiyatı onaylayıp gönderime hazır hale getirin.");
            }

            if (!string.Equals(
                shipment.ConcurrencyStamp,
                concurrencyStamp,
                StringComparison.Ordinal))
            {
                return Failure(
                    id,
                    "Sevkiyat değişti. Sayfayı yenileyip tekrar kontrol edin.");
            }

            shipment.Status = ShipmentStatus.Shipped;
            shipment.ShippedByUserId = userId;
            shipment.ShippedAtUtc = DateTime.UtcNow;
            shipment.ConcurrencyStamp = Guid.NewGuid().ToString("N");

            // Stok onay sırasında düşüldü; burada tekrar değiştirilmez.
            await db.SaveChangesAsync();

            return DeliverySuccess(
                id,
                "Sevkiyat gönderildi olarak kaydedildi. Stok tekrar düşülmedi.");
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogWarning(
                exception,
                "Sevkiyat gönderim durumu başka bir işlemde değişti.");

            db.ChangeTracker.Clear();

            return Failure(id, "Sevkiyat değişti. Sayfayı yenileyin.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Gönderim durumu kaydedilemedi.");
            db.ChangeTracker.Clear();

            return Failure(id, "Gönderim durumu kaydedilemedi.");
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissions.ShipmentsManage)]
    public async Task<IActionResult> MarkDelivered(
        int id,
        string? concurrencyStamp,
        string? deliveredToName,
        string? deliveryNotes)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
            return Challenge();

        deliveredToName = deliveredToName?.Trim();
        deliveryNotes = deliveryNotes?.Trim();

        if (!ModelState.IsValid ||
            string.IsNullOrWhiteSpace(deliveredToName) ||
            deliveredToName.Length > 200 ||
            deliveryNotes?.Length > 500)
        {
            return Failure(
                id,
                "Teslim alanın adı 1–200 karakter, teslim notu en fazla "
                + "500 karakter olmalıdır.");
        }

        try
        {
            var shipment = await db.Shipments
                .SingleOrDefaultAsync(s => s.Id == id);

            if (shipment is null)
                return NotFound();

            if (shipment.Status == ShipmentStatus.Delivered)
            {
                return DeliverySuccess(
                    id,
                    "Bu sevkiyatın teslimi daha önce kaydedilmiş.");
            }

            if (shipment.Status != ShipmentStatus.Shipped)
            {
                return Failure(
                    id,
                    "Teslim kaydından önce sevkiyatı gönderildi olarak işaretleyin.");
            }

            if (!string.Equals(
                shipment.ConcurrencyStamp,
                concurrencyStamp,
                StringComparison.Ordinal))
            {
                return Failure(
                    id,
                    "Sevkiyat değişti. Sayfayı yenileyip tekrar kontrol edin.");
            }

            shipment.Status = ShipmentStatus.Delivered;
            shipment.DeliveredByUserId = userId;
            shipment.DeliveredAtUtc = DateTime.UtcNow;
            shipment.DeliveredToName = deliveredToName;
            shipment.DeliveryNotes = string.IsNullOrWhiteSpace(deliveryNotes)
                ? null
                : deliveryNotes;

            shipment.ConcurrencyStamp = Guid.NewGuid().ToString("N");

            // Teslim kaydı stok miktarını değiştirmez.
            await db.SaveChangesAsync();

            return DeliverySuccess(
                id,
                "Sevkiyat teslim edildi olarak kaydedildi.");
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogWarning(
                exception,
                "Sevkiyat teslim durumu başka bir işlemde değişti.");

            db.ChangeTracker.Clear();

            return Failure(id, "Sevkiyat değişti. Sayfayı yenileyin.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Teslim kaydı oluşturulamadı.");
            db.ChangeTracker.Clear();

            return Failure(id, "Teslim kaydı oluşturulamadı.");
        }
    }

    private IActionResult DeliverySuccess(int id, string message)
    {
        TempData["ShipmentSuccess"] = message;

        return RedirectToAction(
            nameof(Index),
            new { shipmentId = id });
    }
}