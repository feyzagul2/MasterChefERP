using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Models;

public enum ReturnReceiptStatus
{
    WaitingForInspection = 1,
    Inspecting = 2,
    WaitingForPlacement = 3,
    Completed = 4,
    Cancelled = 5
}

[Index(nameof(RequestId), IsUnique = true)]
public class ReturnReceipt
{
    public int Id { get; set; }

    // Tekrar gönderilen form ikinci bir iade oluşturmaz.
    [Required, MaxLength(32)]
    public string RequestId { get; set; }
        = Guid.NewGuid().ToString("N");

    public int ShipmentId { get; set; }

    public Shipment Shipment { get; set; } = null!;

    // Gelen dış paket sayısıdır.
    // Ürünlerin stok miktarını hesaplamak için kullanılmaz.
    // Kolisiz iadelerde 0 olabilir.
    [Range(0, int.MaxValue)]
    public int PackageCount { get; set; }

    [Required, MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    public ReturnReceiptStatus Status { get; set; }
        = ReturnReceiptStatus.WaitingForInspection;

    [Required, MaxLength(450)]
    public string CreatedByUserId { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
        = DateTime.UtcNow;

    [MaxLength(450)]
    public string? CompletedByUserId { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    [MaxLength(450)]
    public string? CancelledByUserId { get; set; }

    public DateTime? CancelledAtUtc { get; set; }

    [MaxLength(500)]
    public string? CancellationReason { get; set; }

    [ConcurrencyCheck, MaxLength(32)]
    public string ConcurrencyStamp { get; set; }
        = Guid.NewGuid().ToString("N");

    public List<ReturnInspection> Items { get; set; } = [];
}