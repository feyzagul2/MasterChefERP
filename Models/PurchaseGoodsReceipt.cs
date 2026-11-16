
using System.ComponentModel.DataAnnotations;

namespace MasterChefTakip.Models;

public class PurchaseGoodsReceipt
{
    public int Id { get; set; }

    [Required, StringLength(40)]
    public string ReceiptNumber { get; set; } = "";

    public int PurchaseDispatchNoteId { get; set; }
    public PurchaseDispatchNote PurchaseDispatchNote { get; set; } = null!;

    public DateTime ReceiptDate { get; set; } = DateTime.UtcNow.Date;

    [Required, StringLength(20)]
    public string Status { get; set; } = "Recorded";

    [StringLength(1000)]
    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [StringLength(200)]
    public string? CreatedBy { get; set; }

    public List<PurchaseGoodsReceiptLine> Lines { get; set; } = new();
}

public class PurchaseGoodsReceiptLine
{
    public int Id { get; set; }

    public int PurchaseGoodsReceiptId { get; set; }
    public PurchaseGoodsReceipt PurchaseGoodsReceipt { get; set; } = null!;

    public int PurchaseDispatchLineId { get; set; }
    public PurchaseDispatchLine PurchaseDispatchLine { get; set; } = null!;

    public int LineNumber { get; set; }

    [Required, StringLength(250)]
    public string ProductDescription { get; set; } = "";

    [Required, StringLength(20)]
    public string Unit { get; set; } = "Adet";

    public decimal ReceivedQuantity { get; set; }
    public decimal AcceptedQuantity { get; set; }
    public decimal RejectedQuantity { get; set; }
}
