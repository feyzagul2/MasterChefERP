
using System.ComponentModel.DataAnnotations;

namespace MasterChefTakip.Models;

public class ProductionPackagingLink
{
    public int Id { get; set; }

    [Required, StringLength(32)]
    public string PackagingBatchId { get; set; } = "";

    public PackagingBatch PackagingBatch { get; set; } = null!;

    public int ProductionOrderId { get; set; }

    public ProductionOrder ProductionOrder { get; set; } = null!;

    public int ProductLotId { get; set; }

    public ProductLot ProductLot { get; set; } = null!;

    [Required, StringLength(450)]
    public string CreatedByUserId { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
