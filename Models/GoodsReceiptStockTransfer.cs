
using System.ComponentModel.DataAnnotations;

namespace MasterChefTakip.Models;

public class GoodsReceiptStockTransfer
{
    public int Id { get; set; }

    public int PurchaseGoodsReceiptLineId { get; set; }

    public PurchaseGoodsReceiptLine PurchaseGoodsReceiptLine
        { get; set; } = null!;

    public int ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public int PalletId { get; set; }

    public Pallet Pallet { get; set; } = null!;

    public int PalletItemId { get; set; }

    public PalletItem PalletItem { get; set; } = null!;

    public int StockMovementId { get; set; }

    public StockMovement StockMovement { get; set; } = null!;

    public int Quantity { get; set; }

    [Required, StringLength(450)]
    public string CreatedByUserId { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
