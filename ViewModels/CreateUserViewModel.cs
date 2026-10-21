using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MasterChefTakip.ViewModels;

public class CreateUserViewModel
{
    [Display(Name = "Ad ve soyad")]
    [Required(ErrorMessage = "Ad ve soyad girin.")]
    [StringLength(
        100,
        ErrorMessage = "Ad ve soyad en fazla 100 karakter olabilir.")]
    public string FullName { get; set; } = string.Empty;

    [Display(Name = "E-posta")]
    [Required(ErrorMessage = "E-posta adresi girin.")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi girin.")]
    [StringLength(
        254,
        ErrorMessage = "E-posta en fazla 254 karakter olabilir.")]
    public string Email { get; set; } = string.Empty;

    [Display(Name = "Şifre")]
    [Required(ErrorMessage = "Şifre girin.")]
    [StringLength(
        128,
        MinimumLength = 10,
        ErrorMessage = "Şifre 10 ile 128 karakter arasında olmalıdır.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Şifre tekrar")]
    [Required(ErrorMessage = "Şifreyi tekrar girin.")]
    [DataType(DataType.Password)]
    [Compare(
        nameof(Password),
        ErrorMessage = "Girilen şifreler eşleşmiyor.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Display(Name = "Erişim rolleri")]
    [Required(ErrorMessage = "En az bir rol seçin.")]
    [MinLength(1, ErrorMessage = "En az bir rol seçin.")]
    public List<string> SelectedRoles { get; set; } = [];

    [BindNever]
    [ValidateNever]
    public List<SelectListItem> AvailableRoles { get; set; } = [];
}