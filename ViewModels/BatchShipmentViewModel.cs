using System.ComponentModel.DataAnnotations;

namespace MasterChefTakip.ViewModels;

public class BatchShipmentViewModel
{
    [Range(1, int.MaxValue)]
    public int ShipmentId { get; set; }

    [Required]
    [MaxLength(32)]
    public string ConcurrencyStamp { get; set; } = string.Empty;

    public List<BatchShipmentRowViewModel> Rows { get; set; } = [];
}

public class BatchShipmentRowViewModel
{
    // Ürünün hangi gönderim kolisine konulduğu.
    [Range(1, 1000, ErrorMessage = "Geçerli bir koli numarası seçin.")]
    public int PackageNumber { get; set; } = 1;

    // Ürünün alınacağı palet stok kaydı.
    [Range(1, int.MaxValue)]
    public int PalletItemId { get; set; }

    // Mevcut ekranla uyumluluk için korunuyor.
    // Yeni karışık koli ekranında ürünler adet olarak girilecek.
    [Range(1, 2)]
    public int QuantityType { get; set; } = 1;

    // Bu koliye konulan gerçek ürün adedi.
    [Range(
        1,
        int.MaxValue,
        ErrorMessage = "Ürün adedi en az 1 olmalıdır.")]
    public int Quantity { get; set; }
    
    // Secilen gercek satis stok kodu.
    [System.ComponentModel.DataAnnotations.StringLength(50)]
    public string? SalesCode { get; set; }
}