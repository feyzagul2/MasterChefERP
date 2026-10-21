using System.ComponentModel.DataAnnotations;

namespace MasterChefTakip.ViewModels;

public class CorrectPalletReceiptViewModel
{
    [Range(1, int.MaxValue)]
    public int MovementId { get; set; }
    [Display(Name = "Doğru ürün")]
    [Range(1, int.MaxValue,
    ErrorMessage = "Doğru ürünü seçin.")]
    public int ProductId { get; set; }

    [Display(Name = "Koli içi adet")]
    [Range(1, int.MaxValue,
    ErrorMessage = "Koli içi adet en az 1 olmalıdır.")]
    public int? UnitsPerBox { get; set; }

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