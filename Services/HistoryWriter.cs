using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MasterChefTakip.Data;
using MasterChefTakip.Models;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Services;

public static class HistoryWriter
{
    private static readonly CultureInfo Culture =
        CultureInfo.GetCultureInfo("tr-TR");

    public static string Normalize(string? text)
    {
        var value = (text ?? string.Empty)
            .ToLower(Culture)
            .Normalize(NormalizationForm.FormD);

        value = Regex.Replace(value, @"\p{Mn}", "");

        return value.Replace('ı', 'i');
    }

    private static string Amount(int boxes, int units, int? capacity)
    {
        if (boxes > 0 && capacity is not > 0)
            return $"{boxes:N0} koli + {units:N0} adet";

        var total = (long)boxes * (capacity ?? 0) + units;

        return total.ToString("N0", Culture) + " adet";
    }

    public static async Task<List<HistoryEntry>> BuildAsync(
        ApplicationDbContext db,
        object entity,
        Dictionary<string, object?>? before,
        string? actorId,
        bool added,
        bool deleted,
        CancellationToken cancellationToken = default)
    {
        var records = new List<HistoryEntry>();
        var now = DateTime.UtcNow;

        HistoryEntry New(
            string key,
            string category,
            string title,
            DateTime date,
            string? userId)
        {
            var entry = new HistoryEntry
            {
                EventKey = key,
                Category = category,
                Title = title,
                CreatedAtUtc = DateTime.SpecifyKind(date, DateTimeKind.Utc),
                UserId = userId ?? string.Empty
            };

            records.Add(entry);
            return entry;
        }

        bool Changed(string name) =>
            before is not null && before.ContainsKey(name);

        string Old(string name) =>
            before?.GetValueOrDefault(name)?.ToString() ?? string.Empty;

        switch (entity)
        {
            case StockMovement movement when added:
            {
                var stock = await db.PalletItems
                    .AsNoTracking()
                    .Include(i => i.Product)
                    .Include(i => i.Pallet)
                    .SingleOrDefaultAsync(
                        i => i.Id == movement.PalletItemId,
                        cancellationToken);

                var category = movement.Type switch
                {
                    StockMovementType.Receipt =>
                        movement.CorrectsMovementId.HasValue
                            ? "Düzeltme"
                            : "Paketleme / giriş",

                    StockMovementType.Shipment => "Sevkiyat",
                    StockMovementType.Return => "İade",
                    StockMovementType.Damage => "Hasar",
                    _ => "İptal"
                };

                var title = movement.Type switch
                {
                    StockMovementType.Receipt =>
                        movement.CorrectsMovementId.HasValue
                            ? "Stok girişi düzeltildi"
                            : movement.PackagingBatchId is not null
                                ? "Ürün paketlendi"
                                : "Stok girişi yapıldı",

                    StockMovementType.Shipment =>
                        "Sevkiyat onaylandı / stok çıkışı",

                    StockMovementType.Return =>
                        "Sağlam iade stoğa yerleştirildi",

                    StockMovementType.Damage =>
                        "Depo hasarı stoktan düşüldü",

                    StockMovementType.ReceiptCancellation =>
                        "Stok girişi geri alındı",

                    StockMovementType.ShipmentCancellation =>
                        "Sevkiyat iptali / stok geri eklendi",

                    _ => "Stok hareketi"
                };

                var row = New(
                    $"stock:{movement.Id}",
                    category,
                    title,
                    movement.CreatedAtUtc,
                    movement.CreatedByUserId);

                row.ProductName = stock?.Product.Name ?? "Ürün";
                row.ProductCode = stock?.Product.Code ?? string.Empty;
                row.Quantity = Amount(
                    movement.BoxCount,
                    movement.LooseUnitCount,
                    movement.UnitsPerBox);

                row.PalletId = stock?.PalletId;
                row.ShipmentId = movement.RelatedShipmentId;
                row.Reference = stock?.Pallet.Barcode ?? string.Empty;

                row.Notes =
                    $"{movement.BoxCount} tam koli; "
                    + $"koli içi: {movement.UnitsPerBox?.ToString() ?? "bilinmiyor"}; "
                    + $"{movement.LooseUnitCount} koli dışı adet."
                    + Environment.NewLine
                    + (movement.Notes ?? string.Empty);

                if (movement.CorrectsMovementId is int originalId)
                {
                    var original = await db.StockMovements
                        .AsNoTracking()
                        .SingleOrDefaultAsync(
                            m => m.Id == originalId,
                            cancellationToken);

                    if (original is not null)
                    {
                        row.BeforeValue = Amount(
                            original.BoxCount,
                            original.LooseUnitCount,
                            original.UnitsPerBox);

                        row.AfterValue = row.Quantity;
                    }
                }

                break;
            }

            case Shipment shipment:
            {
                HistoryEntry? draft = null;

                if (added)
                {
                    draft = New(
                        $"shipment:{shipment.Id}:created",
                        "Sevkiyat",
                        "Sevkiyat taslağı oluşturuldu",
                        shipment.CreatedAtUtc,
                        shipment.CreatedByUserId);
                }

                if (!added
                    && !deleted
                    && shipment.Status == ShipmentStatus.Draft
                    && Changed(nameof(Shipment.ConcurrencyStamp)))
                {
                    var saved = New(
                        $"shipment:{shipment.Id}:draft:{shipment.ConcurrencyStamp}",
                        "Sevkiyat",
                        "Sevkiyat taslağı kaydedildi",
                        now,
                        actorId);

                    saved.Notes = "Koli içerikleri kaydedildi. Stok değişmedi.";
                }

if (shipment.ShippedAtUtc is DateTime shippedAt &&
                    (added || Changed(nameof(Shipment.ShippedAtUtc))))
                {
                    New(
                        $"shipment:{shipment.Id}:shipped",
                        "Sevkiyat",
                        "Sevkiyat gönderildi",
                        shippedAt,
                        shipment.ShippedByUserId);
                }

                if (shipment.DeliveredAtUtc is DateTime deliveredAt &&
                    (added || Changed(nameof(Shipment.DeliveredAtUtc))))
                {
                    var delivered = New(
                        $"shipment:{shipment.Id}:delivered",
                        "Sevkiyat",
                        "Müşteriye teslim edildi",
                        deliveredAt,
                        shipment.DeliveredByUserId);

                    delivered.Notes =
                        $"Teslim alan: {shipment.DeliveredToName}"
                        + Environment.NewLine
                        + shipment.DeliveryNotes;
                }

                if (shipment.CancelledAtUtc is DateTime cancelledAt &&
                    (added || Changed(nameof(Shipment.CancelledAtUtc))))
                {
                    var hasStockCancellation = await db.StockMovements
                        .AsNoTracking()
                        .AnyAsync(m =>
                            m.RelatedShipmentId == shipment.Id &&
                            m.Type == StockMovementType.ShipmentCancellation,
                            cancellationToken);

                    // Stoklu iptal zaten stok hareketlerinden gösterilir.
                    if (!hasStockCancellation)
                    {
                        var cancelled = New(
                            $"shipment:{shipment.Id}:cancelled",
                            "İptal",
                            "Sevkiyat taslağı iptal edildi",
                            cancelledAt,
                            shipment.CancelledByUserId);

                        cancelled.Notes =
                            shipment.CancellationReason ?? string.Empty;
                    }
                }

                var items = await db.ShipmentItems
                    .AsNoTracking()
                    .Where(i => i.ShipmentId == shipment.Id)
                    .ToListAsync(cancellationToken);

                var known = items.All(i =>
                    i.BoxCount == 0 || i.UnitsPerBox is > 0);

                var total = items.Sum(i =>
                    (long)i.BoxCount * (i.UnitsPerBox ?? 0)
                    + i.LooseUnitCount);

                foreach (var row in records)
                {
                    row.ShipmentId = shipment.Id;

                    row.Reference =
                        shipment.RecipientName + " · " + shipment.Number;

                    // Taslağa sonradan eklenen ürünleri ilk gün varmış
                    // gibi göstermeyelim.
                    if (row != draft)
                    {
                        row.ProductName = string.Join(", ",
                            items.Select(i => i.ProductName).Distinct());

                        row.Quantity = known
                            ? total.ToString("N0", Culture) + " adet"
                            : "Koli kapasitesi eksik";
                    }
                }

                break;
            }

            case ReturnInspection inspection:
            {
                var item = await db.ShipmentItems
                    .AsNoTracking()
                    .Include(i => i.Shipment)
                    .SingleOrDefaultAsync(
                        i => i.Id == inspection.ShipmentItemId,
                        cancellationToken);

                if (added)
                {
                    var received = New(
                        $"return:{inspection.Id}:received",
                        "İade",
                        "İade kabul edildi",
                        inspection.CreatedAtUtc,
                        inspection.CreatedByUserId);

                    received.Quantity =
                        inspection.ReceivedUnits.ToString("N0", Culture)
                        + " adet";

                    received.Notes = inspection.Reason;
                }

                if (inspection.InspectedAtUtc is DateTime inspectedAt &&
                    (added || Changed(nameof(ReturnInspection.InspectedAtUtc))))
                {
                    var inspected = New(
                        $"return:{inspection.Id}:inspected:{inspectedAt.Ticks}",
                        "İade",
                        "İade kontrolü yapıldı",
                        inspectedAt,
                        inspection.InspectedByUserId);

                    inspected.Quantity =
                        inspection.ReceivedUnits.ToString("N0", Culture)
                        + " adet";

                    inspected.Notes =
                        $"Sağlam: {inspection.GoodTotalUnits:N0} adet; "
                        + $"hasarlı: {inspection.DamagedUnitCount:N0} adet."
                        + Environment.NewLine
                        + inspection.InspectionNotes;
                }

                if (!added &&
                    Changed(nameof(ReturnInspection.ReceivedUnits)))
                {
                    var corrected = New(
                        "return-correction:" + Guid.NewGuid().ToString("N"),
                        "Düzeltme",
                        "İade miktarı düzeltildi",
                        now,
                        actorId);

                    corrected.BeforeValue =
                        Old(nameof(ReturnInspection.ReceivedUnits)) + " adet";

                    corrected.AfterValue =
                        inspection.ReceivedUnits.ToString("N0", Culture)
                        + " adet";

                    corrected.Quantity = corrected.AfterValue;
                    corrected.Notes =
                        inspection.InspectionNotes ?? string.Empty;
                }

                foreach (var row in records)
                {
                    row.ProductName = item?.ProductName ?? "İade ürünü";
                    row.ProductCode = item?.ProductCode ?? string.Empty;
                    row.ShipmentId = item?.ShipmentId;
                    row.ReturnReceiptId = inspection.ReturnReceiptId;

                    row.Reference =
                        $"İade ürün kaydı #{inspection.Id}"
                        + (item is null
                            ? string.Empty
                            : " · " + item.Shipment.RecipientName
                              + " · " + item.Shipment.Number);
                }

                break;
            }

            case ReturnReceipt receipt
                when receipt.CancelledAtUtc is not null:
            {
                if (!added &&
                    !Changed(nameof(ReturnReceipt.CancelledAtUtc)))
                    break;

                var row = New(
                    $"receipt:{receipt.Id}:cancelled",
                    "İptal",
                    "İade kaydı iptal edildi",
                    receipt.CancelledAtUtc.Value,
                    receipt.CancelledByUserId);

                row.ReturnReceiptId = receipt.Id;
                row.ShipmentId = receipt.ShipmentId;
                row.Reference = $"İade kaydı #{receipt.Id}";
                row.Notes = receipt.CancellationReason ?? string.Empty;
                break;
            }

            case Pallet pallet:
            {
                if (added)
                {
                    var row = New(
                        $"pallet:{pallet.Id}:created",
                        "Palet",
                        "Palet tanımlandı",
                        pallet.CreatedAtUtc,
                        actorId);

                    row.AfterValue = pallet.Location ?? "Konum belirtilmedi";
                }
                else if (deleted)
                {
                    var row = New(
                        "pallet-deleted:" + Guid.NewGuid().ToString("N"),
                        "Palet",
                        "Palet kalıcı olarak silindi",
                        now,
                        actorId);

                    row.BeforeValue = pallet.Location ?? string.Empty;
                }
                else
                {
                    if (Changed(nameof(Pallet.Location)) ||
                        Changed(nameof(Pallet.Notes)))
                    {
                        var row = New(
                            "pallet-location:" + Guid.NewGuid().ToString("N"),
                            "Konum",
                            "Paletin konumu / notu düzenlendi",
                            now,
                            actorId);

                        row.BeforeValue =
                            "Konum: "
                            + (Changed(nameof(Pallet.Location))
                                ? Old(nameof(Pallet.Location))
                                : pallet.Location)
                            + Environment.NewLine
                            + "Not: "
                            + (Changed(nameof(Pallet.Notes))
                                ? Old(nameof(Pallet.Notes))
                                : pallet.Notes);

                        row.AfterValue =
                            "Konum: " + pallet.Location
                            + Environment.NewLine
                            + "Not: " + pallet.Notes;
                    }

                    if (Changed(nameof(Pallet.IsActive)))
                    {
                        var row = New(
                            "pallet-status:" + Guid.NewGuid().ToString("N"),
                            "Palet",
                            pallet.IsActive
                                ? "Palet aktifleştirildi"
                                : "Palet pasife alındı",
                            now,
                            actorId);

                        row.BeforeValue = Old(nameof(Pallet.IsActive));
                        row.AfterValue = pallet.IsActive ? "Aktif" : "Pasif";
                    }
                }

                foreach (var row in records)
                {
                    row.PalletId = deleted ? null : pallet.Id;
                    row.Reference = pallet.Barcode;
                }

                break;
            }
        }

        foreach (var row in records)
        {
            var account = string.IsNullOrWhiteSpace(row.UserId)
                ? null
                : await db.Users.AsNoTracking()
                    .SingleOrDefaultAsync(
                        u => u.Id == row.UserId,
                        cancellationToken);

            row.UserName = account is null
                ? "Personel bilgisi bulunmuyor"
                : string.IsNullOrWhiteSpace(account.FullName)
                    ? account.Email ?? "Personel"
                    : account.FullName;

            row.SearchText = Normalize(
                $"{row.UserName} {row.Title} {row.ProductName} "
                + $"{row.ProductCode} {row.Reference} {row.Notes}");
        }

        return records;
    }

    public static async Task AddMissingAsync(
        ApplicationDbContext db,
        List<HistoryEntry> records,
        CancellationToken cancellationToken = default)
    {
        if (records.Count == 0)
            return;

        var keys = records.Select(r => r.EventKey).Distinct().ToArray();

        var existing = await db.HistoryEntries.AsNoTracking()
            .Where(r => keys.Contains(r.EventKey))
            .Select(r => r.EventKey)
            .ToListAsync(cancellationToken);

        var known = existing.ToHashSet(StringComparer.Ordinal);

        foreach (var row in records)
        {
            if (known.Add(row.EventKey))
                db.HistoryEntries.Add(row);
        }
    }

    public static async Task ImportAsync(
        ApplicationDbContext db,
        CancellationToken cancellationToken = default)
    {
        // Küçük gruplarla aktarılır; tüm geçmiş belleğe yüklenmez.
        async Task ImportTable<T>(IQueryable<T> query)
            where T : class
        {
            var offset = 0;

            while (true)
            {
                var batch = await query
                    .Skip(offset)
                    .Take(200)
                    .ToListAsync(cancellationToken);

                if (batch.Count == 0)
                    break;

                var rows = new List<HistoryEntry>();

                foreach (var entity in batch)
                {
                    rows.AddRange(await BuildAsync(
                        db, entity, null, null,
                        added: true,
                        deleted: false,
                        cancellationToken));
                }

                await AddMissingAsync(db, rows, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);

                // Aktarılan geçmiş kayıtları takibi şişirmesin.
                foreach (var entry in db.ChangeTracker
                    .Entries<HistoryEntry>().ToList())
                {
                    entry.State = EntityState.Detached;
                }

                offset += batch.Count;
            }
        }

        await ImportTable(db.StockMovements.AsNoTracking()
            .OrderBy(m => m.Id));

        await ImportTable(db.Shipments.AsNoTracking()
            .OrderBy(s => s.Id));

        await ImportTable(db.ReturnInspections.AsNoTracking()
            .OrderBy(i => i.Id));

        await ImportTable(db.ReturnReceipts.AsNoTracking()
            .Where(r => r.CancelledAtUtc != null)
            .OrderBy(r => r.Id));
    }
}