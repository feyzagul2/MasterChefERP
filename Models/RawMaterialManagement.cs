
using System.ComponentModel.DataAnnotations;

namespace MasterChefTakip.Models;

public class RawMaterial
{
    public int Id { get; set; }

    [Required, StringLength(50)]
    public string Code { get; set; } = "";

    [Required, StringLength(200)]
    public string Name { get; set; } = "";

    [Required, StringLength(10)]
    public string Unit { get; set; } = "Kg";

    public decimal MinimumStock { get; set; }

    [StringLength(100)]
    public string? Category { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class RawMaterialWarehouse
{
    public int Id { get; set; }

    [Required, StringLength(40)]
    public string Code { get; set; } = "";

    [Required, StringLength(150)]
    public string Name { get; set; } = "";

    [StringLength(250)]
    public string? Location { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class RawMaterialStockMovement
{
    public int Id { get; set; }

    public int RawMaterialId { get; set; }

    public RawMaterial RawMaterial { get; set; } = null!;

    public int WarehouseId { get; set; }

    public RawMaterialWarehouse Warehouse { get; set; } = null!;

    [Required, StringLength(15)]
    public string MovementType { get; set; } = "IN";

    public decimal Quantity { get; set; }

    [Required, StringLength(10)]
    public string Unit { get; set; } = "Kg";

    [Required, StringLength(60)]
    public string OperationNumber { get; set; } = "";

    [StringLength(100)]
    public string? ReferenceNumber { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    [StringLength(450)]
    public string CreatedByUserId { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
