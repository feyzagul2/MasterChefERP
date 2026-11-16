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
    [Authorize(Policy = AppPermissions.ShipmentsCancel)]
    public async Task<IActionResult> Cancel(
        int id,
        string? concurrencyStamp,
        string? reason)
    {
        var userId = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        reason = reason?.Trim();

        if (!ModelState.IsValid ||
            string.IsNullOrWhiteSpace(reason) ||
            reason.Length > 500)
        {
            return Failure(
                id,
                "İptal gerekçesi 1–500 karakter olmalıdır.");
        }

        try
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync();

            var shipment = await db.Shipments
                .Include(s => s.Items)
                .SingleOrDefaultAsync(s => s.Id == id);

            if (shipment is null)
            {
                return NotFound();
            }

            if (shipment.Status == ShipmentStatus.Cancelled)
            {
                return Failure(
                    id,
                    "Bu sevkiyat zaten iptal edilmiş. Stok tekrar eklenmedi.");
            }

                        // MC_SHIPMENT_CANCELLATION_DUPLICATE_GUARD_V1
            // Daha once iade hareketi olusturulmus bir sevkiyata
            // ikinci kez stok eklenmesini engeller.
            if (await db.StockMovements.AnyAsync(m =>
                m.RelatedShipmentId == shipment.Id &&
                m.Type == StockMovementType.ShipmentCancellation))
            {
                return Failure(
                    id,
                    "Bu sevkiyata ait stok iade hareketi zaten var. "
                    + "Ikinci kez stok iadesi engellendi.");
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

            if (shipment.Status != ShipmentStatus.Draft &&
                shipment.Status != ShipmentStatus.Confirmed)
            {
                return Failure(id, "Sevkiyatın durumu geçersiz.");
            }
            // İade kaydı bulunan sevkiyatın tekrar stok eklemesini engeller.
            if (await db.ReturnInspections.AnyAsync(item =>
                item.ShipmentItem.ShipmentId == shipment.Id))
            {
                return Failure(
                    id,
                    "Bu sevkiyata bağlı iade kaydı bulunuyor. "
                    + "Ürünlerin tekrar stoğa eklenmesini önlemek için "
                    + "sevkiyat iptal edilemez. İade bölümünden devam edin.");
            }
            var now = DateTime.UtcNow;
            var exactRestores = 0;
            var additiveRestores = 0;

                if (shipment.Status == ShipmentStatus.Confirmed)
            {
                var ids = shipment.Items
                    .Select(i => i.PalletItemId)
                    .Distinct()
                    .ToList();

                var stocks = await db.PalletItems
                    .Include(i => i.Pallet)
                    .Where(i => ids.Contains(i.Id))
                    .ToDictionaryAsync(i => i.Id);

                var outgoingMovements = await db.StockMovements
                    .Where(m =>
                        m.RelatedShipmentId == shipment.Id &&
                        m.Type == StockMovementType.Shipment)
                    .ToListAsync();

                foreach (var line in shipment.Items)
                {
                    if (!stocks.TryGetValue(
                        line.PalletItemId,
                        out var stock))
                    {
                        return Failure(
                            id,
                            "Çıkış yapılan palet içeriği bulunamadı. İptal uygulanmadı.");
                    }

                    if (stock.Pallet.IsCancelled)
                    {
                        return Failure(
                            id,
                            "Çıkış paleti iptal edilmiş. Önce palet kaydını kontrol edin.");
                    }

                    if (stock.BoxCount < 0 ||
                        stock.LooseUnitCount < 0 ||
                        stock.OpenBoxUnitCount < 0 ||
                        line.BoxCount < 0 ||
                        line.LooseUnitCount < 0)
                    {
                        return Failure(
                            id,
                            "Stok veya sevkiyat miktarı geçersiz.");
                    }

                    var canRestoreExactly =
                        line.HasStockSnapshot &&
                        !string.IsNullOrWhiteSpace(line.AfterStockStamp) &&
                        string.Equals(
                            stock.ConcurrencyStamp,
                            line.AfterStockStamp,
                            StringComparison.Ordinal);

                    string restoreNote;

                    if (canRestoreExactly)
                    {
                        stock.BoxCount = line.BeforeBoxCount;
                        stock.LooseUnitCount =
                            line.BeforeLooseUnitCount;
                        stock.OpenBoxUnitCount =
                            line.BeforeOpenBoxUnitCount;
                        stock.OpenBoxCapacity =
                            line.BeforeOpenBoxCapacity;
                        stock.UnitsPerBox = line.UnitsPerBox;

                        exactRestores++;

                        restoreNote =
                            "Onay sonrası stok değişmedi; "
                            + "önceki koli dağılımı geri alındı.";
                    }
                    else
                    {
                        // Sonraki hareketleri geri yazmayız.
                        // Kapalı gönderilen koliler kapalı olarak,
                        // adet gönderilen ürünler tekli olarak döner.
                        if (line.BoxCount > 0 &&
                            stock.BoxCount > 0 &&
                            stock.UnitsPerBox != line.UnitsPerBox)
                        {
                            return Failure(
                                id,
                                $"{line.ProductName}: Mevcut kolilerin "
                                + "koli içi adedi sevkiyattan farklı. "
                                + "İptal uygulanmadı.");
                        }

                        var restoredBoxes = checked(
                            stock.BoxCount + line.BoxCount);

                        var restoredLoose = checked(
                            stock.LooseUnitCount + line.LooseUnitCount);

                        if (line.BoxCount > 0 &&
                            stock.BoxCount == 0)
                        {
                            stock.UnitsPerBox = line.UnitsPerBox;
                        }

                        stock.BoxCount = restoredBoxes;
                        stock.LooseUnitCount = restoredLoose;

                        // Açık koli miktarı ve kapasitesi korunur.
                        additiveRestores++;

                        restoreNote =
                            "Sonraki stok hareketleri korundu; "
                            + "gönderilen kapalı koliler kapalı stoka, "
                            + "adetler bağımsız tekli stoka eklendi.";
                    }

                    stock.ConcurrencyStamp =
                        Guid.NewGuid().ToString("N");

                    var original = outgoingMovements
                        .SingleOrDefault(m =>
                            m.PalletItemId == line.PalletItemId);

                    var note =
                        $"Sevkiyat iptali: {shipment.Number}. "
                        + restoreNote
                        + $" Gerekçe: {reason}";

                    db.StockMovements.Add(new StockMovement
                    {
                        RelatedShipmentId = shipment.Id,
                        PalletItemId = stock.Id,
                        Type = StockMovementType.ShipmentCancellation,
                        BoxCount = line.BoxCount,
                        LooseUnitCount = line.LooseUnitCount,
                        UnitsPerBox = line.UnitsPerBox,
                        CreatedByUserId = userId,
                        CreatedAtUtc = now,

                        // Yeni kayıtların çıkış hareketine bağlanır.
                        // Eski sevkiyatlarda bu bilgi bulunmayabilir.
                        ReversesMovementId = original?.Id,

                        Notes = note.Length <= 500
                            ? note
                            : note[..500]
                    });
                }
            }

            shipment.Status = ShipmentStatus.Cancelled;
            shipment.CancelledByUserId = userId;
            shipment.CancelledAtUtc = now;
            shipment.CancellationReason = reason;
            shipment.ConcurrencyStamp =
                Guid.NewGuid().ToString("N");

            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["ShipmentSuccess"] =
                exactRestores + additiveRestores == 0
                    ? "Taslak iptal edildi. Stok değişmedi."
                    : $"Sevkiyat iptal edildi. "
                        + $"{exactRestores} satırın önceki dağılımı geri alındı; "
                        + $"{additiveRestores} satır mevcut stoka eklendi.";

            return RedirectToAction(
                nameof(Index),
                new { shipmentId = id });
        }
        catch (OverflowException)
        {
            db.ChangeTracker.Clear();

            return Failure(
                id,
                "Geri eklenecek stok miktarı sistem sınırını aşıyor. İptal uygulanmadı.");
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogWarning(
                exception,
                "Sevkiyat iptali sırasında stok değişti.");

            db.ChangeTracker.Clear();

            return Failure(
                id,
                "Stok başka bir işlemde değişti. Sayfayı yenileyin. İptal uygulanmadı.");
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Sevkiyat iptal edilemedi.");

            db.ChangeTracker.Clear();

            return Failure(
                id,
                "Sevkiyat iptal edilemedi. Stok değişiklikleri geri alındı.");
        }
    }

    [HttpGet]
    [Authorize(Policy = AppPermissions.ShipmentsLabels)]
    public async Task<IActionResult> Label(int id)
    {
        var shipment = await db.Shipments
            .AsNoTracking()
            .Include(s => s.Items)
            .SingleOrDefaultAsync(s => s.Id == id);

        if (shipment is null)
        {
            return NotFound();
        }

        if (shipment.Status != ShipmentStatus.Confirmed)
        {
            return Failure(
                id,
                "Etiket yalnızca onaylanmış sevkiyat için yazdırılır.");
        }

        return View("Label", shipment);
    }
}