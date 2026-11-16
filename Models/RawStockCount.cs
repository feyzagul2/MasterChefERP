
using System.ComponentModel.DataAnnotations;

namespace MasterChefTakip.Models;

public class RawStockCount
{
    public int Id { get; set; }

    [Required, StringLength(50)]
    public string CountNumber { get; set; } = "";

    public int RawMaterialId { get; set; }
    public RawMaterial RawMaterial { get; set; } = null!;

    public int WarehouseId { get; set; }
    public RawMaterialWarehouse Warehouse { get; set; } = null!;

    public decimal SystemQuantity { get; set; }
    public decimal CountedQuantity { get; set; }
    public decimal Difference { get; set; }

    [Required, StringLength(10)]
    public string Unit { get; set; } = "";

    [StringLength(500)]
    public string? Notes { get; set; }

    [Required, StringLength(450)]
    public string CreatedByUserId { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
