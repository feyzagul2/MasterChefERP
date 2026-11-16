
using System.ComponentModel.DataAnnotations;

namespace MasterChefTakip.Models;

public class PurchaseDispatchNote
{
    public int Id { get; set; }

    [Required, StringLength(40)]
    public string DocumentNumber { get; set; } = "";

    [StringLength(60)]
    public string? SupplierDocumentNumber { get; set; }

    public int? PurchaseOrderId { get; set; }

    public PurchaseOrder? PurchaseOrder { get; set; }

    public int SupplierId { get; set; }

    public CariAccount Supplier { get; set; } = null!;

    public DateTime DocumentDate { get; set; } = DateTime.UtcNow.Date;

    [Required, StringLength(20)]
    public string Status { get; set; } = "Draft";

    [StringLength(1000)]
    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [StringLength(200)]
    public string? CreatedBy { get; set; }

    public List<PurchaseDispatchLine> Lines { get; set; } = new();
}

public class PurchaseDispatchLine
{
    public int Id { get; set; }

    public int PurchaseDispatchNoteId { get; set; }

    public PurchaseDispatchNote PurchaseDispatchNote { get; set; } = null!;

    public int? PurchaseOrderLineId { get; set; }

    public PurchaseOrderLine? PurchaseOrderLine { get; set; }

    public int LineNumber { get; set; }

    [Required, StringLength(250)]
    public string ProductDescription { get; set; } = "";

    [Required, StringLength(20)]
    public string Unit { get; set; } = "Adet";

    [Range(typeof(decimal), "0.0001", "999999999", ParseLimitsInInvariantCulture = true)]
    public decimal Quantity { get; set; }
}
