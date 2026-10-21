using MasterChefTakip.Models;
using MasterChefTakip.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

public partial class ReturnsController
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissions.ReturnsInspect)]
    public async Task<IActionResult> CorrectQuantity(
        [Bind(Prefix = "CorrectionForm")]
        CorrectReturnQuantityViewModel model)
    {
        var userId = CurrentUserId;

        if (string.IsNullOrWhiteSpace(userId))
            return Challenge();

        if (string.IsNullOrWhiteSpace(model.Reason))
        {
            ModelState.AddModelError(
                "CorrectionForm.Reason",
                "Düzeltme nedenini girin.");
        }

        if (!ModelState.IsValid)
            return await CorrectionErrorAsync(model);

        try
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync();

            var item = await db.ReturnInspections
                .Include(i => i.Placements)
                .Include(i => i.ReturnReceipt)
                .Include(i => i.ShipmentItem)
                    .ThenInclude(s => s.Shipment)
                .SingleOrDefaultAsync(i => i.Id == model.Id);

            if (item is null)
                return NotFound();

            var editable =
                item.Status == ReturnInspectionStatus.Waiting ||
                item.Status == ReturnInspectionStatus.Inspecting ||
                item.Status == ReturnInspectionStatus.WaitingForPlacement;

            if (!editable ||
                item.Placements.Count > 0 ||
                item.ReturnReceipt?.Status == ReturnReceiptStatus.Cancelled ||
                item.ReturnReceipt?.Status == ReturnReceiptStatus.Completed)
            {
                return await CorrectionErrorAsync(
                    model,
                    "Yalnızca stoğa alınmamış, bekleyen "
                    + "iade ürünlerinin miktarı düzeltilebilir.");
            }

            if (!StampMatches(
                item.ConcurrencyStamp,
                model.ConcurrencyStamp))
            {
                return await CorrectionErrorAsync(
                    model,
                    "İade başka bir işlemde değişti. "
                    + "Sayfayı yenileyip miktarı kontrol edin.");
            }

            var shipmentItem = item.ShipmentItem;
            var shipment = shipmentItem.Shipment;

            if (shipment.Status is not (
                ShipmentStatus.Confirmed or
                ShipmentStatus.Shipped or
                ShipmentStatus.Delivered))
            {
                return await CorrectionErrorAsync(
                    model,
                    "İadenin bağlı olduğu sevkiyat "
                    + "iade işlemi için uygun değil.");
            }

            if (shipmentItem.BoxCount > 0 &&
                shipmentItem.UnitsPerBox is not > 0)
            {
                return await CorrectionErrorAsync(
                    model,
                    "Sevkiyatın koli içi adedi bilinmiyor. "
                    + "Gönderilen miktar doğrulanmadan "
                    + "iade miktarı değiştirilemez.");
            }

            var capacity = shipmentItem.UnitsPerBox;
            long newTotal = model.Quantity!.Value;

            if (model.QuantityType == 2)
            {
                if (capacity is not > 0)
                {
                    ModelState.AddModelError(
                        "CorrectionForm.QuantityType",
                        "Bu sevkiyatın koli içi adedi bilinmiyor. "
                        + "Geri gelen ürünleri sayıp Adet seçin.");

                    return await CorrectionErrorAsync(model);
                }

                newTotal *= capacity.Value;
            }

            if (newTotal is < 1 or > int.MaxValue)
            {
                ModelState.AddModelError(
                    "CorrectionForm.Quantity",
                    "Toplam ürün adedi desteklenen sınırı aşıyor.");

                return await CorrectionErrorAsync(model);
            }

            var shippedTotal =
                (long)shipmentItem.BoxCount *
                (shipmentItem.UnitsPerBox ?? 0) +
                shipmentItem.LooseUnitCount;

            // Düzeltilen satır dışındaki geçerli iadeler.
            // Bekleyen iadeler de miktar sınırına dahildir.
            var otherAmounts = await db.ReturnInspections
                .Where(i =>
                    i.ShipmentItemId == item.ShipmentItemId &&
                    i.Id != item.Id &&
                    i.Status != ReturnInspectionStatus.Cancelled)
                .Select(i => i.ReceivedUnits)
                .ToListAsync();

            var otherTotal = otherAmounts.Sum(i => (long)i);

            var maximum = Math.Max(
                0L,
                shippedTotal - otherTotal);

            if (newTotal > maximum)
            {
                ModelState.AddModelError(
                    "CorrectionForm.Quantity",
                    $"Bu sevkiyat satırından en fazla "
                    + $"{maximum} ürün iade kaydedilebilir. "
                    + "Diğer iade kayıtları da hesaba katıldı.");

                return await CorrectionErrorAsync(model);
            }

            var oldTotal = item.ReceivedUnits;
            var now = DateTime.UtcNow;

            var correctionNote =
                $"Miktar düzeltildi: {oldTotal} → {newTotal} ürün. "
                + $"Neden: {model.Reason.Trim()}";

            var previousNotes = item.InspectionNotes?.Trim();

            var combinedNotes =
                string.IsNullOrWhiteSpace(previousNotes)
                    ? correctionNote
                    : previousNotes + Environment.NewLine + correctionNote;

            if (combinedNotes.Length > 500)
            {
                ModelState.AddModelError(
                    "CorrectionForm.Reason",
                    "Mevcut notlarla birlikte açıklama 500 karakteri "
                    + "aşıyor. Daha kısa bir düzeltme nedeni girin.");

                return await CorrectionErrorAsync(model);
            }

            item.ReceivedUnits = (int)newTotal;

            if (model.QuantityType == 2)
            {
                item.ReceivedBoxCount = model.Quantity.Value;
                item.ReceivedUnitsPerBox = capacity;
                item.ReceivedAdditionalUnitCount = 0;
            }
            else
            {
                item.ReceivedBoxCount = 0;
                item.ReceivedUnitsPerBox = null;
                item.ReceivedAdditionalUnitCount = (int)newTotal;
            }

            // Miktar değişince önceki kontrol sonuçları sıfırlanır.
            item.InspectedGoodUnits = 0;
            item.DamagedUnitCount = 0;
            item.GoodBoxCount = 0;
            item.GoodUnitCount = 0;
            item.DestinationPalletId = null;
            item.InspectedByUserId = null;
            item.InspectedAtUtc = null;

            item.InspectionNotes = combinedNotes;
            item.Status = ReturnInspectionStatus.Waiting;

            item.ConcurrencyStamp =
                Guid.NewGuid().ToString("N");

            shipment.ConcurrencyStamp =
                Guid.NewGuid().ToString("N");

            await db.SaveChangesAsync();

            if (item.ReturnReceiptId.HasValue)
            {
                await RefreshReceiptStatusAsync(
                    item.ReturnReceiptId.Value,
                    userId,
                    now);

                await db.SaveChangesAsync();
            }

            await transaction.CommitAsync();

            logger.LogInformation(
                "İade {ReturnItemId} miktarı {OldUnits} -> "
                + "{NewUnits} olarak kullanıcı {UserId} tarafından düzeltildi.",
                item.Id,
                oldTotal,
                newTotal,
                userId);

            return Success(
                $"Geri gelen miktar {oldTotal} üründen "
                + $"{newTotal} ürüne düzeltildi. "
                + "Stok değişmedi. Sağlam ve hasarlı adetlerini "
                + "yeni miktara göre girin.");
        }
        catch (DbUpdateConcurrencyException exception)
        {
            LogFailure(
                exception,
                "İade miktarı düzeltilirken kayıt değişti.");

            return await CorrectionErrorAsync(
                model,
                "Kayıt başka bir işlemde değişti. "
                + "Sayfayı yenileyip tekrar kontrol edin.");
        }
        catch (Exception exception)
        {
            LogFailure(
                exception,
                "İade miktarı düzeltilemedi.");

            return await CorrectionErrorAsync(
                model,
                "Miktar düzeltilemedi. Değişiklikler geri alındı.");
        }
    }

    private async Task<IActionResult> CorrectionErrorAsync(
        CorrectReturnQuantityViewModel model,
        string? message = null)
    {
        db.ChangeTracker.Clear();

        if (!string.IsNullOrWhiteSpace(message))
            ModelState.AddModelError(string.Empty, message);

        var page = await BuildPageAsync();

        await EnsureVisibleItemAsync(page, model.Id);

        ViewData["CorrectionForm"] = model;

        return View("Index", page);
    }
}