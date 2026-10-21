using System.ComponentModel.DataAnnotations;
using MasterChefTakip.Models;

namespace MasterChefTakip.ViewModels;

public class PalletTransferViewModel
{
    public int? SourcePalletId { get; set; }

    [Required, MaxLength(32)]
    public string RequestId { get; set; }
        = Guid.NewGuid().ToString("N");

    [Range(1, int.MaxValue, ErrorMessage = "Ürünü seçin.")]
    public int SourceStockId { get; set; }

    [Required, MaxLength(32)]
    public string ConcurrencyStamp { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Hedef paleti seçin.")]
    public int DestinationPalletId { get; set; }

    [Range(0, int.MaxValue)]
    public int BoxCount { get; set; }

    [Range(0, int.MaxValue)]
    public int UnitCount { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public List<PalletTransferStockOption> Stocks { get; set; } = [];
    public List<Pallet> Destinations { get; set; } = [];
}

public class PalletTransferStockOption
{
    public int Id { get; set; }
    public int PalletId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string ConcurrencyStamp { get; set; } = string.Empty;
    public int BoxCount { get; set; }
    public long IndividualUnits { get; set; }
    public int? UnitsPerBox { get; set; }
}