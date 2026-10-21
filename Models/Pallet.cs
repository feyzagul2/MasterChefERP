using System.ComponentModel.DataAnnotations;

namespace MasterChefTakip.Models;

public class Pallet
{
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Barcode { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Location { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        [ConcurrencyCheck]
    public bool IsCancelled { get; set; }

    public DateTime? CancelledAtUtc { get; set; }

    [MaxLength(450)]
    public string? CancelledByUserId { get; set; }

    [MaxLength(500)]
    public string? CancellationReason { get; set; }
}