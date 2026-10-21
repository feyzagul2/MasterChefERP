using System.ComponentModel.DataAnnotations;

namespace MasterChefTakip.Models;

// Birden fazla fiziksel urunden olusan satis paketi.
public class SalesBundle
{
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string SalesCode { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string SalesName { get; set; } = string.Empty;

    // Original selling prices from the Excel sales record.
    public decimal? SalesPriceVatIncluded { get; set; }
    public decimal? SalesPriceVatExcluded { get; set; }
    public decimal? VatRatePercent { get; set; }

    [System.ComponentModel.DataAnnotations.MaxLength(10)]
    public string? Currency { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<SalesBundleComponent> Components { get; set; }
        = new List<SalesBundleComponent>();
}

// Paketin hangi fiziksel urunden kac adet icerdigi.
public class SalesBundleComponent
{
    public int Id { get; set; }

    public int SalesBundleId { get; set; }

    public SalesBundle SalesBundle { get; set; } = null!;

    public int ProductId { get; set; }

    public Product Product { get; set; } = null!;

    [Range(1, int.MaxValue)]
    public int UnitsPerBundle { get; set; }
}