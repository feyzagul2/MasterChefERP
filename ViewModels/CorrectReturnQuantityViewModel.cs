using System.ComponentModel.DataAnnotations;

namespace MasterChefTakip.ViewModels;

public class CorrectReturnQuantityViewModel
{
    [Range(1, int.MaxValue)]
    public int Id { get; set; }

    [Required, StringLength(32, MinimumLength = 32)]
    public string ConcurrencyStamp { get; set; } = string.Empty;

    // 1: Gerçek ürün adedi.
    // 2: Aynı üründen oluşan standart tam koli.
    [Range(1, 2, ErrorMessage = "Adet veya koli seçin.")]
    public int QuantityType { get; set; } = 1;

    [Required(ErrorMessage = "Geri gelen miktarı girin.")]
    [Range(
        1,
        int.MaxValue,
        ErrorMessage = "Miktar en az 1 olmalıdır.")]
    public int? Quantity { get; set; }

    [Required(ErrorMessage = "Düzeltme nedenini girin.")]
    [MaxLength(250)]
    public string Reason { get; set; } = string.Empty;
}