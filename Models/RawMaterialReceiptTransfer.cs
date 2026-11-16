
using System.ComponentModel.DataAnnotations;

namespace MasterChefTakip.Models;

public class RawMaterialReceiptTransfer
{
    public int Id { get; set; }

    public int PurchaseGoodsReceiptLineId { get; set; }

    public PurchaseGoodsReceiptLine PurchaseGoodsReceiptLine
        { get; set; } = null!;

    public int RawMaterialId { get; set; }

    public RawMaterial RawMaterial { get; set; } = null!;

    public int WarehouseId { get; set; }

    public RawMaterialWarehouse Warehouse { get; set; } = null!;

    public int RawMaterialStockMovementId { get; set; }

    public RawMaterialStockMovement RawMaterialStockMovement
        { get; set; } = null!;

    public decimal Quantity { get; set; }

    [Required, StringLength(10)]
    public string Unit { get; set; } = "";

    [Required, StringLength(450)]
    public string CreatedByUserId { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
