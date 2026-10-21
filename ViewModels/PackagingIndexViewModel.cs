using System.ComponentModel.DataAnnotations;
using MasterChefTakip.Models;

namespace MasterChefTakip.ViewModels;

public class PackagingIndexViewModel
{
    public PackagingFormViewModel Form { get; set; } = new();

    public List<Product> Products { get; set; } = [];

    public List<Pallet> Pallets { get; set; } = [];
}

public class PackagingFormViewModel
{
    [Required]
    [StringLength(32, MinimumLength = 32)]
    public string RequestId { get; set; }
        = Guid.NewGuid().ToString("N");

    [MaxLength(500, ErrorMessage = "Not en fazla 500 karakter olabilir.")]
    public string? Notes { get; set; }

    public List<PackagingRowViewModel> Rows { get; set; } =
    [
        new()
    ];
}

public class PackagingRowViewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Ürün seçin.")]
    public int ProductId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Palet seçin.")]
    public int PalletId { get; set; }

    [Required(ErrorMessage = "Koli sayısını girin.")]
    [Range(1, int.MaxValue, ErrorMessage = "Koli sayısı en az 1 olmalıdır.")]
    public int? BoxCount { get; set; }

    [Required(ErrorMessage = "Bir kolide kaç ürün olduğunu girin.")]
    [Range(1, int.MaxValue, ErrorMessage = "Koli içi adet en az 1 olmalıdır.")]
    public int? UnitsPerBox { get; set; }
}