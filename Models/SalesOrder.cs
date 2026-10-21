using System.ComponentModel.DataAnnotations;

namespace MasterChefTakip.Models;

public class SalesOrder
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Number { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string CustomerName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Phone { get; set; }

    [Required, MaxLength(500)]
    public string DeliveryAddress { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Notes { get; set; }

    [Required, MaxLength(450)]
    public string CreatedByUserId { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [ConcurrencyCheck, MaxLength(32)]
    public string ConcurrencyStamp { get; set; }
        = Guid.NewGuid().ToString("N");

    public List<SalesOrderItem> Items { get; set; } = [];
}

public class SalesOrderItem
{
    public int Id { get; set; }

    public int SalesOrderId { get; set; }
    public SalesOrder SalesOrder { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    // Sevkiyat kontrolünün kullandığı toplam ürün adedi.
    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }

    [Required, MaxLength(200)]
    public string ProductName { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string ProductCode { get; set; } = string.Empty;

    // Aynı ürünün farklı koli büyüklükleri burada korunur.
    public List<SalesOrderQuantityEntry> QuantityEntries
        { get; set; } = [];
}

public class SalesOrderQuantityEntry
{
    public int Id { get; set; }

    public int SalesOrderItemId { get; set; }
    public SalesOrderItem SalesOrderItem { get; set; } = null!;

    // 1: Adet, 2: Koli.
    [Range(1, 2)]
    public int QuantityType { get; set; }

    // Kullanıcının girdiği adet veya koli sayısı.
    [Range(1, int.MaxValue)]
    public int EnteredQuantity { get; set; }

    // Sipariş anındaki koli içi adet.
    // Ürünün varsayılanı değişince bu değer değişmez.
    [Range(1, int.MaxValue)]
    public int? UnitsPerBox { get; set; }

    // Bu girişin gerçek toplam ürün adedi.
    [Range(1, int.MaxValue)]
    public int TotalUnits { get; set; }
}