
using System.ComponentModel.DataAnnotations;

namespace MasterChefTakip.Models;

public class ProductionRecipe
{
    public int Id { get; set; }

    [Required, StringLength(50)]
    public string Code { get; set; } = "";

    [Required, StringLength(200)]
    public string Name { get; set; } = "";

    public int? ProductId { get; set; }
    public Product? Product { get; set; }

    public decimal BatchQuantityKg { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<ProductionRecipeLine> Lines { get; set; } = new();
}

public class ProductionRecipeLine
{
    public int Id { get; set; }

    public int ProductionRecipeId { get; set; }
    public ProductionRecipe Recipe { get; set; } = null!;

    public int RawMaterialId { get; set; }
    public RawMaterial RawMaterial { get; set; } = null!;

    public decimal Quantity { get; set; }
}

public class ProductionOrder
{
    public int Id { get; set; }

    [Required, StringLength(50)]
    public string OrderNumber { get; set; } = "";

    public int ProductionRecipeId { get; set; }
    public ProductionRecipe Recipe { get; set; } = null!;

    public int WarehouseId { get; set; }
    public RawMaterialWarehouse Warehouse { get; set; } = null!;

    public decimal PlannedQuantityKg { get; set; }

    [Required, StringLength(20)]
    public string Status { get; set; } = "Planned";

    [StringLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAtUtc { get; set; }

    [StringLength(450)]
    public string? CompletedByUserId { get; set; }

    public List<ProductionConsumption> Consumptions { get; set; } = new();
}

public class ProductionConsumption
{
    public int Id { get; set; }

    public int ProductionOrderId { get; set; }
    public ProductionOrder ProductionOrder { get; set; } = null!;

    public int RawMaterialId { get; set; }
    public RawMaterial RawMaterial { get; set; } = null!;

    public int RawMaterialStockMovementId { get; set; }
    public RawMaterialStockMovement Movement { get; set; } = null!;

    public decimal Quantity { get; set; }

    [Required, StringLength(10)]
    public string Unit { get; set; } = "";
}
