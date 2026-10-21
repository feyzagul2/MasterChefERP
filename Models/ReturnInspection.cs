using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Models;

public enum ReturnInspectionStatus
{
    // Eski değerleri koruyoruz.
    Waiting = 1,
    Completed = 2,

    Inspecting = 3,
    WaitingForPlacement = 4,
    Cancelled = 5
}

[Index(nameof(RequestId), IsUnique = true)]
public class ReturnInspection
{
    public int Id { get; set; }

    [Required, MaxLength(32)]
    public string RequestId { get; set; }
        = Guid.NewGuid().ToString("N");

    // Yeni kayıtlarda ürün satırının bağlı olduğu iade başlığı.
    // Eski kayıtlarda boş kalabilir.
    public int? ReturnReceiptId { get; set; }

    public ReturnReceipt? ReturnReceipt { get; set; }

    // Gerçekte gönderilen sevkiyat satırı.
    public int ShipmentItemId { get; set; }

    public ShipmentItem ShipmentItem { get; set; } = null!;

    // Ürünün geri gelen gerçek toplam adedi.
    [Range(1, int.MaxValue)]
    public int ReceivedUnits { get; set; }

    // Sevkiyat anındaki koli içi adet bilgisi.
    // Yerleştirme kapasitesi bundan ayrı tutulur.
    [Range(1, int.MaxValue)]
    public int? UnitsPerBox { get; set; }

    // Giriş ekranında standart tam koli olarak girilen miktar.
    [Range(0, int.MaxValue)]
    public int ReceivedBoxCount { get; set; }

    [Range(1, int.MaxValue)]
    public int? ReceivedUnitsPerBox { get; set; }

    // Tam kolilerin dışında girilen ürün miktarı.
    // Karışık veya eksik kolilerde gerçek ürün sayısı buraya yazılır.
    [Range(0, int.MaxValue)]
    public int ReceivedAdditionalUnitCount { get; set; }

    [Required, MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    public ReturnInspectionStatus Status { get; set; }
        = ReturnInspectionStatus.Waiting;

    [Required, MaxLength(450)]
    public string CreatedByUserId { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
        = DateTime.UtcNow;

    // Yeni kontrolde sağlam bulunan toplam ürün adedi.
    // Koli veya yerleştirme biçiminden bağımsızdır.
    [Range(0, int.MaxValue)]
    public int InspectedGoodUnits { get; set; }

    // Kontrolde hasarlı olduğu açıkça belirtilen ürün adedi.
    [Range(0, int.MaxValue)]
    public int DamagedUnitCount { get; set; }

    // Eski kayıtların tamamlanmış sonuçlarını koruyan alanlar.
    [Range(0, int.MaxValue)]
    public int GoodBoxCount { get; set; }

    [Range(0, int.MaxValue)]
    public int GoodUnitCount { get; set; }

    public int? DestinationPalletId { get; set; }

    public Pallet? DestinationPallet { get; set; }

    [MaxLength(500)]
    public string? InspectionNotes { get; set; }

    [MaxLength(450)]
    public string? InspectedByUserId { get; set; }

    public DateTime? InspectedAtUtc { get; set; }

    [ConcurrencyCheck, MaxLength(32)]
    public string ConcurrencyStamp { get; set; }
        = Guid.NewGuid().ToString("N");

    public List<ReturnPlacement> Placements { get; set; } = [];

    // Eski tamamlanmış kayıtların sağlam miktarı,
    // eski koli ve adet alanlarından hesaplanır.
    [NotMapped]
    public long GoodTotalUnits =>
        ReturnReceiptId is null
        && Status == ReturnInspectionStatus.Completed
            ? (long)GoodBoxCount * (UnitsPerBox ?? 0)
                + GoodUnitCount
            : InspectedGoodUnits;

    // Kalan ürünler otomatik hasarlı sayılmaz.
    [NotMapped]
    public long UninspectedUnits =>
        (long)ReceivedUnits
        - GoodTotalUnits
        - DamagedUnitCount;
}