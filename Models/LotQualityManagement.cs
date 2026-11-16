
using System.ComponentModel.DataAnnotations;

namespace MasterChefTakip.Models;

public class ProductLot
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public Product Product { get; set; } = null!;

    [Required, StringLength(70)]
    public string LotNumber { get; set; } = "";

    public DateTime ProductionDate { get; set; }

    public DateTime ExpiryDate { get; set; }

    [Required, StringLength(20)]
    public string Status { get; set; } = "Pending";

    [StringLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [StringLength(450)]
    public string CreatedByUserId { get; set; } = "";

    public List<ProductQualityInspection> Inspections
        { get; set; } = new();
}

public class ProductQualityInspection
{
    public int Id { get; set; }

    public int ProductLotId { get; set; }

    public ProductLot ProductLot { get; set; } = null!;

    [Required, StringLength(20)]
    public string Result { get; set; } = "Pending";

    [Required, StringLength(200)]
    public string InspectionName { get; set; } = "";

    [StringLength(1000)]
    public string? Notes { get; set; }

    public DateTime InspectedAtUtc { get; set; } = DateTime.UtcNow;

    [StringLength(450)]
    public string InspectedByUserId { get; set; } = "";
}
