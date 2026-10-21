using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MasterChefTakip.Models;

public class PalletItem
{
    public int Id { get; set; }

    public int PalletId { get; set; }
    public Pallet Pallet { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    // Açılmamış, tam koliler.
    [Range(0, int.MaxValue)]
    public int BoxCount { get; set; }

    // Koliden bağımsız girilen tekli ürünler.
    [Range(0, int.MaxValue)]
    public int LooseUnitCount { get; set; }

    // Açık kolinin içinde kalan ürünler.
    [Range(0, int.MaxValue)]
    public int OpenBoxUnitCount { get; set; }

    // Açılan kolinin başlangıçtaki kapasitesi.
    // Koli bilgisi sonradan değişse de korunur.
    [Range(1, int.MaxValue)]
    public int? OpenBoxCapacity { get; set; }

    // Kapalı kolilerin koli içi adet bilgisi.
    [Range(1, int.MaxValue)]
    public int? UnitsPerBox { get; set; }

    [NotMapped]
    public int OpenBoxCount => OpenBoxUnitCount > 0 ? 1 : 0;

    [NotMapped]
    public string IndividualStockSummary =>
        $"Açık koli: {OpenBoxUnitCount} adet · "
        + $"Bağımsız tekli: {LooseUnitCount} adet";

    [ConcurrencyCheck]
    [MaxLength(32)]
    public string ConcurrencyStamp { get; set; }
        = Guid.NewGuid().ToString("N");
}