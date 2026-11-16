
using System.ComponentModel.DataAnnotations;

namespace MasterChefTakip.Models;

public class RawStockCountApproval
{
    public int Id { get; set; }

    public int RawStockCountId { get; set; }
    public RawStockCount RawStockCount { get; set; } = null!;

    [Required, StringLength(20)]
    public string Decision { get; set; } = "";

    public int? StockMovementId { get; set; }
    public RawMaterialStockMovement? StockMovement { get; set; }

    [Required, StringLength(450)]
    public string ApprovedByUserId { get; set; } = "";

    public DateTime ApprovedAtUtc { get; set; } = DateTime.UtcNow;

    [StringLength(500)]
    public string? Notes { get; set; }
}
