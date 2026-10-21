using System.ComponentModel.DataAnnotations;

namespace MasterChefTakip.ViewModels;

public class SetupViewModel
{
    [Display(Name = "Ad ve soyad")]
    [Required(ErrorMessage = "Adınızı ve soyadınızı girin.")]
    [StringLength(100)]
    public string FullName { get; set; } = string.Empty;

    [Display(Name = "E-posta")]
    [Required(ErrorMessage = "E-posta adresinizi girin.")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi girin.")]
    [StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Display(Name = "Şifre")]
    [Required(ErrorMessage = "Şifre belirleyin.")]
    [StringLength(128, MinimumLength = 10,
        ErrorMessage = "Şifreniz 10–128 karakter arasında olmalı.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Şifre tekrar")]
    [Required(ErrorMessage = "Şifrenizi tekrar girin.")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password),
        ErrorMessage = "Şifreler eşleşmiyor.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
