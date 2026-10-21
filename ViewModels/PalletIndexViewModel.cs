using System.ComponentModel.DataAnnotations;
using MasterChefTakip.Models;

namespace MasterChefTakip.ViewModels;

public class PalletIndexViewModel
{
    public int ProductId { get; set; }
    public List<Pallet> Pallets { get; set; } = [];

    public CreatePalletViewModel Form { get; set; } = new();

    public bool CanManage { get; set; }

    public bool ShowForm { get; set; }

    // İçeriği görüntülenen palet.
    public Pallet? SelectedPallet { get; set; }

    // Veritabanından doldurulacak ürün seçenekleri.
    public List<Product> AvailableProducts { get; set; } = [];

    // Seçilen paletin mevcut içeriği.
    public List<PalletItem> Items { get; set; } = [];
    public List<PalletMovementViewModel> Movements { get; set; } = [];

    public AddPalletProductViewModel ProductForm { get; set; } = new();
    public EditPalletViewModel EditForm { get; set; } = new();
}

public class CreatePalletViewModel
{
    [Display(Name = "Paletin konumu")]
    [MaxLength(100, ErrorMessage = "Konum en fazla 100 karakter olabilir.")]
    public string? Location { get; set; }

    [Display(Name = "Not")]
    [MaxLength(500, ErrorMessage = "Not en fazla 500 karakter olabilir.")]
    public string? Notes { get; set; }
}

public enum PalletQuantityType
{
    [Display(Name = "Adet")]
    Unit = 1,

    [Display(Name = "Koli")]
    Box = 2
}

public class AddPalletProductViewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Bir palet seçin.")]
    public int PalletId { get; set; }

    [Display(Name = "Ürün")]
    [Range(1, int.MaxValue, ErrorMessage = "Bir ürün seçin.")]
    public int ProductId { get; set; }

    [Display(Name = "Miktar türü")]
    [Required(ErrorMessage = "Adet veya koli seçin.")]
    [EnumDataType(
        typeof(PalletQuantityType),
        ErrorMessage = "Geçerli bir miktar türü seçin.")]
    public PalletQuantityType? QuantityType { get; set; }

    [Display(Name = "Miktar")]
    [Required(ErrorMessage = "Miktarı girin.")]
    [Range(1, int.MaxValue, ErrorMessage = "Miktar en az 1 olmalıdır.")]
    public int? Quantity { get; set; }

    [Display(Name = "Koli içi adet")]
    [Range(1, int.MaxValue, ErrorMessage = "Koli içi adet en az 1 olmalıdır.")]
    public int? UnitsPerBox { get; set; }

    [Display(Name = "İşlem notu")]
    [MaxLength(500, ErrorMessage = "Not en fazla 500 karakter olabilir.")]
    public string? Notes { get; set; }
}
public class EditPalletViewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Geçerli bir palet seçin.")]
    public int Id { get; set; }

    [Display(Name = "Paletin konumu")]
    [MaxLength(100, ErrorMessage = "Konum en fazla 100 karakter olabilir.")]
    public string? Location { get; set; }

    [Display(Name = "Not")]
    [MaxLength(500, ErrorMessage = "Not en fazla 500 karakter olabilir.")]
    public string? Notes { get; set; }
}
public class PalletMovementViewModel
{
    public int Id { get; set; }
       public int ProductId { get; set; }
    public bool CanCorrect { get; set; }

    public string ConcurrencyStamp { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string ProductCode { get; set; } = string.Empty;

    public StockMovementType Type { get; set; }

    public int BoxCount { get; set; }

    public int LooseUnitCount { get; set; }

    public int? UnitsPerBox { get; set; }

    public string CreatedByName { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public string TypeLabel => Type switch
    {
        StockMovementType.Receipt => "Stok girişi",
        StockMovementType.Shipment => "Sevkiyat",
        StockMovementType.Return => "İade",
        StockMovementType.Damage => "Hasar",
        StockMovementType.ReceiptCancellation => "Giriş geri alma",
        _ => "Diğer işlem"
    };
    public class CorrectPalletReceiptViewModel
{
    [Range(1, int.MaxValue)]
    public int MovementId { get; set; }

    [Display(Name = "Doğru koli sayısı")]
    [Required(ErrorMessage = "Koli sayısını girin.")]
    [Range(0, int.MaxValue,
        ErrorMessage = "Koli sayısı negatif olamaz.")]
    public int? BoxCount { get; set; }

    [Display(Name = "Doğru koli dışı adet")]
    [Required(ErrorMessage = "Koli dışı adedi girin.")]
    [Range(0, int.MaxValue,
        ErrorMessage = "Adet negatif olamaz.")]
    public int? LooseUnitCount { get; set; }

    [Display(Name = "Düzeltme nedeni")]
    [Required(ErrorMessage = "Düzeltme nedenini yazın.")]
    [MaxLength(500,
        ErrorMessage = "Düzeltme nedeni en fazla 500 karakter olabilir.")]
    public string Reason { get; set; } = string.Empty;

    [Required]
    [MaxLength(32)]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}
}