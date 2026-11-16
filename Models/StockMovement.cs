using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Models;

public enum StockMovementType
{
    Receipt = 1,
    Shipment = 2,
    Return = 3,
    Damage = 4,
    ReceiptCancellation = 5,
    ShipmentCancellation = 6
}
[Index(nameof(ReversesMovementId), IsUnique = true)]
[Index(nameof(CorrectsMovementId), IsUnique = true)]
[StockMovementPositiveQuantity]
public class StockMovement
{
        // Hareketin bağlı olduğu sevkiyat.
    public int? RelatedShipmentId { get; set; }
    // Bu stok girişi hangi paketleme işleminden oluştu?
    [MaxLength(32)]
    public string? PackagingBatchId { get; set; }
    public int Id { get; set; }

    public int PalletItemId { get; set; }
    public PalletItem PalletItem { get; set; } = null!;

    public StockMovementType Type { get; set; }

    [Range(0, int.MaxValue)]
    public int BoxCount { get; set; }

    [Range(0, int.MaxValue)]
    public int LooseUnitCount { get; set; }

    [Range(1, int.MaxValue)]
    public int? UnitsPerBox { get; set; }

    [Required]
    [MaxLength(450)]
    public string CreatedByUserId { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // İptal edilen girişin kayıt numarası.
    // Benzersiz indeks aynı girişin iki kez iptalini engeller.
    public int? ReversesMovementId { get; set; }
    // Yeni girişin düzelttiği eski işlem.
    // Aynı işlem için ikinci bir düzeltme kaydı oluşturulmasını engeller.
    public int? CorrectsMovementId { get; set; }
}