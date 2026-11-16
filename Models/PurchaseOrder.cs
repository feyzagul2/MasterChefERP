
using System.ComponentModel.DataAnnotations;

namespace MasterChefTakip.Models;

public class PurchaseOrder
{
    public int Id { get; set; }

    [Required, StringLength(40)]
    public string OrderNumber { get; set; } = "";

    public int SupplierId { get; set; }
    public CariAccount Supplier { get; set; } = null!;

    public DateTime OrderDate { get; set; } = DateTime.UtcNow.Date;

    [Required, StringLength(3)]
    public string Currency { get; set; } = "TRY";

    [StringLength(1000)]
    public string? Notes { get; set; }

    [Required, StringLength(20)]
    public string Status { get; set; } = "Draft";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [StringLength(200)]
    public string? CreatedBy { get; set; }

    public List<PurchaseOrderLine> Lines { get; set; } = new();
}

public class PurchaseOrderLine
{
    public int Id { get; set; }
    public int PurchaseOrderId { get; set; }

    public PurchaseOrder PurchaseOrder { get; set; } = null!;

    [Required, StringLength(250)]
    public string ProductDescription { get; set; } = "";

    [Required, StringLength(20)]
    public string Unit { get; set; } = "Adet";

    [Range(typeof(decimal), "0.0001", "999999999", ParseLimitsInInvariantCulture = true)]
    public decimal Quantity { get; set; }

    [Range(typeof(decimal), "0", "999999999", ParseLimitsInInvariantCulture = true)]
    public decimal UnitPrice { get; set; }

    [Range(typeof(decimal), "0", "100", ParseLimitsInInvariantCulture = true)]
    public decimal VatRate { get; set; }

    public int LineNumber { get; set; }
}
