using System.ComponentModel.DataAnnotations;

namespace MasterChefTakip.Models;

public enum ShipmentStatus
{
    Draft = 1,
    Confirmed = 2,
    Cancelled = 3,
    Shipped = 4,
    Delivered = 5
}

public class Shipment
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Number { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string RecipientName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? RecipientPhone { get; set; }

    [MaxLength(500)]
    public string? DeliveryAddress { get; set; }

    [MaxLength(100)]
    public string? CargoCompany { get; set; }

    [MaxLength(100)]
    public string? TrackingNumber { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public ShipmentStatus Status { get; set; }
        = ShipmentStatus.Draft;

    [Required, MaxLength(450)]
    public string CreatedByUserId { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
        = DateTime.UtcNow;

    // Stok yalnızca sevkiyat onaylanırken düşülür.
    [MaxLength(450)]
    public string? ConfirmedByUserId { get; set; }

    public DateTime? ConfirmedAtUtc { get; set; }

    // Gönderim durumuna geçerken stok tekrar düşülmez.
    [MaxLength(450)]
    public string? ShippedByUserId { get; set; }

    public DateTime? ShippedAtUtc { get; set; }

    [MaxLength(450)]
    public string? DeliveredByUserId { get; set; }

    public DateTime? DeliveredAtUtc { get; set; }

    [MaxLength(200)]
    public string? DeliveredToName { get; set; }

    [MaxLength(500)]
    public string? DeliveryNotes { get; set; }

    [MaxLength(450)]
    public string? CancelledByUserId { get; set; }

    public DateTime? CancelledAtUtc { get; set; }

    [MaxLength(500)]
    public string? CancellationReason { get; set; }

    [ConcurrencyCheck, MaxLength(32)]
    public string ConcurrencyStamp { get; set; }
        = Guid.NewGuid().ToString("N");

    public List<ShipmentItem> Items { get; set; } = [];
    // Gönderim kolileri ve her kolinin içindeki ürünler.
    public List<ShipmentPackage> Packages { get; set; } = [];
}