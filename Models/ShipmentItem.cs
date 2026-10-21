using System.ComponentModel.DataAnnotations;

namespace MasterChefTakip.Models;

public class ShipmentItem
{
    public int Id { get; set; }

    public int ShipmentId { get; set; }

    public Shipment Shipment { get; set; } = null!;

    // Ürünün çıkacağı palet içeriği.
    public int PalletItemId { get; set; }

    public PalletItem PalletItem { get; set; } = null!;

    [Range(0, int.MaxValue)]
    public int BoxCount { get; set; }

    [Range(0, int.MaxValue)]
    public int LooseUnitCount { get; set; }

    // Sevkiyat onaylandığında koli içi adet bilgisi korunur.
    [Range(1, int.MaxValue)]
    public int? UnitsPerBox { get; set; }

    // Ürün adı veya kodu sonradan değişse de
    // onaylanan sevkiyatın bilgileri korunur.
    [MaxLength(200)]
    public string ProductName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string ProductCode { get; set; } = string.Empty;

    [MaxLength(50)]
    public string PalletBarcode { get; set; } = string.Empty;
    // Yeni onaylarda stok dağılımı saklanır.
// Eski sevkiyatlarda bu alan false kalır.
public bool HasStockSnapshot { get; set; }

public int BeforeBoxCount { get; set; }

public int BeforeLooseUnitCount { get; set; }

public int BeforeOpenBoxUnitCount { get; set; }

public int? BeforeOpenBoxCapacity { get; set; }

// Bu sevkiyatın stok üzerinde bıraktığı sürüm.
[MaxLength(32)]
public string? AfterStockStamp { get; set; }
}