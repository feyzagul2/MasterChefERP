using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MasterChefTakip.Models;

public enum ProductPriceBasis
{
    Unit = 0,
    Box = 1
}

public class Product
{
    // Boşsa bu ürün için düşük stok uyarısı kapalıdır.
    [Range(0, int.MaxValue)]
    public int? LowStockThresholdUnits { get; set; }
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Code { get; set; } = string.Empty;
    [MaxLength(128)]
    public string? Barcode { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string PackagingType { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int? DefaultUnitsPerBox { get; set; }

    [Range(typeof(decimal), "0.001", "1000000000", ParseLimitsInInvariantCulture = true)]
    public decimal? DefaultUnitNetWeightGrams { get; set; }

    // Girilen tutar; hangi fiyata ait olduğu PriceBasis ile saklanır.
    [Range(typeof(decimal), "0", "1000000000", ParseLimitsInInvariantCulture = true)]
    public decimal? UnitPrice { get; set; }

    public ProductPriceBasis PriceBasis { get; set; } = ProductPriceBasis.Unit;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [ConcurrencyCheck, MaxLength(32)]
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");

    [NotMapped]
    public decimal? CalculatedUnitPrice => PriceBasis == ProductPriceBasis.Unit
        ? UnitPrice
        : DefaultUnitsPerBox is int units && units > 0 && UnitPrice is decimal price
            ? price / units
            : null;

    [NotMapped]
    public decimal? CalculatedBoxPrice => PriceBasis == ProductPriceBasis.Box
        ? UnitPrice
        : DefaultUnitsPerBox is int units && units > 0 && UnitPrice is decimal price
            ? price * units
            : null;
}


// Aynı fiziksel ürünün farklı satış seçenekleri.
public class ProductSalesOption
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public Product Product { get; set; } = null!;

    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.MaxLength(50)]
    public string SalesCode { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.MaxLength(200)]
    public string SalesName { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)]
    public int UnitsPerSale { get; set; }

    // Original selling prices from the Excel sales record.
    public decimal? SalesPriceVatIncluded { get; set; }
    public decimal? SalesPriceVatExcluded { get; set; }
    public decimal? VatRatePercent { get; set; }

    [System.ComponentModel.DataAnnotations.MaxLength(10)]
    public string? Currency { get; set; }

    public bool IsActive { get; set; } = true;
}