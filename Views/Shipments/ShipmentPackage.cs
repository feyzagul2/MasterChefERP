using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Models;

// Sevkiyatta gönderilecek fiziksel koli.
// Depodaki ürün kolisi bilgisinden ayrı tutulur.
[Index(nameof(ShipmentId), nameof(Number), IsUnique = true)]
public class ShipmentPackage
{
    public int Id { get; set; }

    public int ShipmentId { get; set; }

    public Shipment Shipment { get; set; } = null!;

    // Sevkiyat içindeki koli numarası: 1, 2, 3...
    [Range(1, int.MaxValue)]
    public int Number { get; set; }

    public List<ShipmentPackageItem> Items { get; set; } = [];
}

// Gönderim kolisinin içindeki bir ürünün gerçek adedi.
[Index(
    nameof(ShipmentPackageId),
    nameof(ShipmentItemId),
    IsUnique = true)]
public class ShipmentPackageItem
{
    public int Id { get; set; }

    public int ShipmentPackageId { get; set; }

    public ShipmentPackage ShipmentPackage { get; set; } = null!;

    // Mevcut sevkiyat ürün satırına bağlanır.
    // Stok çıkışı ve iadeler aynı ürün satırını kullanmaya devam eder.
    public int ShipmentItemId { get; set; }

    public ShipmentItem ShipmentItem { get; set; } = null!;

    // Bu kolinin içine gerçekten konulan ürün miktarı.
    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }
    [Range(1, 2)] public int QuantityType { get; set; } = 1;
    
    // Sevkiyat aninda secilen satis stok kodu.
    [System.ComponentModel.DataAnnotations.MaxLength(50)]
    public string? SalesCode { get; set; }
}