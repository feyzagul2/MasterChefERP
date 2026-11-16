using System;
using System.Linq;
using System.Threading.Tasks;
using MasterChefTakip.Data;
using MasterChefTakip.Models;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

// MC_BATCH_SHIPMENT_GUARD_V3
public static class MCShipmentStockGuard
{
    private static long Quantity(StockMovement movement)
    {
        if (!movement.UnitsPerBox.HasValue ||
            movement.UnitsPerBox.Value <= 0 ||
            movement.BoxCount < 0 ||
            movement.LooseUnitCount < 0)
        {
            throw new InvalidOperationException(
                "Gecersiz koli veya adet bilgisi.");
        }

        return checked(
            (long)movement.BoxCount *
            movement.UnitsPerBox.Value +
            movement.LooseUnitCount);
    }

    private static int Direction(StockMovementType type)
    {
        return type switch
        {
            StockMovementType.Receipt => 1,
            StockMovementType.Return => 1,
            StockMovementType.ShipmentCancellation => 1,
            StockMovementType.Shipment => -1,
            StockMovementType.Damage => -1,
            StockMovementType.ReceiptCancellation => -1,
            _ => throw new InvalidOperationException(
                "Taninmayan stok hareket turu.")
        };
    }

    public static async Task ValidateAsync(ApplicationDbContext db)
    {
        var pending = db.ChangeTracker
            .Entries<StockMovement>()
            .Where(x =>
                x.State == EntityState.Added &&
                x.Entity.Type == StockMovementType.Shipment)
            .Select(x => x.Entity)
            .ToList();

        if (pending.Count == 0)
            return;

        var palletIds = pending
            .Select(x => x.PalletItemId)
            .Distinct()
            .ToList();

        var existing = await db.Set<StockMovement>()
            .AsNoTracking()
            .Where(x => palletIds.Contains(x.PalletItemId))
            .ToListAsync();

        foreach (var palletId in palletIds)
        {
            var history = existing
                .Where(x => x.PalletItemId == palletId)
                .ToList();

            var shipments = pending
                .Where(x => x.PalletItemId == palletId)
                .ToList();

            if (history.Count == 0)
            {
                throw new InvalidOperationException(
                    "Palet " + palletId +
                    ": Stok hareketi bulunamadi.");
            }

            var all = history.Concat(shipments).ToList();

            if (all.Any(x =>
                !x.UnitsPerBox.HasValue ||
                x.UnitsPerBox.Value <= 0 ||
                x.BoxCount < 0 ||
                x.LooseUnitCount < 0))
            {
                throw new InvalidOperationException(
                    "Palet " + palletId +
                    ": Gecersiz koli veya adet bilgisi.");
            }

            if (all.Select(x => x.UnitsPerBox!.Value)
                   .Distinct().Count() != 1)
            {
                throw new InvalidOperationException(
                    "Palet " + palletId +
                    ": Koli ici adet bilgileri uyusmuyor.");
            }

            long available = 0;

            foreach (var movement in history)
            {
                available = checked(
                    available +
                    Direction(movement.Type) *
                    Quantity(movement));
            }

            long requested = 0;

            foreach (var shipment in shipments)
            {
                requested = checked(
                    requested + Quantity(shipment));
            }

            if (requested <= 0)
            {
                throw new InvalidOperationException(
                    "Sevkiyat miktari sifir olamaz.");
            }

            if (available < 0)
            {
                throw new InvalidOperationException(
                    "Palet " + palletId +
                    ": Mevcut stok bakiyesi negatif.");
            }

            if (requested > available)
            {
                throw new InvalidOperationException(
                    "YETERSIZ STOK - Palet " + palletId +
                    " | Mevcut: " + available +
                    " adet | Istenen: " + requested + " adet.");
            }
        }
    }
}