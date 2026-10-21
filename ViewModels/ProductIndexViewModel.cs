using System.ComponentModel.DataAnnotations;
using MasterChefTakip.Models;

namespace MasterChefTakip.ViewModels;

public class ProductIndexViewModel
{
    public List<Product> Products { get; set; } = [];
    public CreateProductViewModel Form { get; set; } = new();
    public bool CanCreate { get; set; }
    public bool CanManage { get; set; }
    public bool ShowForm { get; set; }
}

public class CreateProductViewModel
{
    [Range(0, int.MaxValue)]
    public int Id { get; set; }
    public string? ConcurrencyStamp { get; set; }

    [Display(Name = "Ürün kodu (isteğe bağlı)")]
    [MaxLength(50, ErrorMessage = "Ürün kodu en fazla 50 karakter olabilir.")]
    public string? Code { get; set; }
    [Display(Name = "Ürün barkodu")]
    [MaxLength(128, ErrorMessage = "Barkod en fazla 128 karakter olabilir.")]
    public string? Barcode { get; set; }

    [Display(Name = "Ürün adı")]
    [Required(ErrorMessage = "Ürün adını girin.")]
    [MaxLength(200, ErrorMessage = "Ürün adı en fazla 200 karakter olabilir.")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Ambalaj türü (isteğe bağlı)")]
    [MaxLength(100, ErrorMessage = "Ambalaj türü en fazla 100 karakter olabilir.")]
    public string? PackagingType { get; set; }

    [Display(Name = "Varsayılan koli içi adet")]
    [Range(1, int.MaxValue, ErrorMessage = "Koli içi adet en az 1 olmalıdır.")]
    public int? DefaultUnitsPerBox { get; set; }

    [Display(Name = "Bir ürünün net ağırlığı (gram)")]
    [MaxLength(30, ErrorMessage = "Gramaj değeri çok uzun.")]
    [RegularExpression(@"^[0-9]+([.,][0-9]{1,3})?$",
        ErrorMessage = "Gramaj için en fazla 3 ondalık basamak kullanın.")]
    public string? DefaultUnitNetWeightGrams { get; set; }

    [Display(Name = "Fiyat türü")]
    [Required(ErrorMessage = "Adet fiyatı veya koli fiyatını seçin.")]
    [EnumDataType(typeof(ProductPriceBasis), ErrorMessage = "Geçerli bir fiyat türü seçin.")]
    public ProductPriceBasis? PriceBasis { get; set; }

    [Display(Name = "Seçilen türe ait fiyat (TL)")]
    [MaxLength(30, ErrorMessage = "Fiyat değeri çok uzun.")]
    [RegularExpression(@"^[0-9]+([.,][0-9]{1,2})?$",
        ErrorMessage = "Fiyat için en fazla 2 ondalık basamak kullanın.")]
    public string? UnitPrice { get; set; }
}

public class ProductActionViewModel
{
    [Range(1, int.MaxValue)]
    public int Id { get; set; }

    // Eski kayıtlarda ilk güncellemeye kadar boş olabilir.
    public string? ConcurrencyStamp { get; set; }
    public bool? IsActive { get; set; }
}
