using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MasterChefTakip.ViewModels;

public class EditUserViewModel
{
    [Required]
    public string Id { get; set; } = string.Empty;

    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ad ve soyad girin.")]
    [MaxLength(100, ErrorMessage = "Ad ve soyad en fazla 100 karakter olabilir.")]
    public string FullName { get; set; } = string.Empty;

    [MinLength(1, ErrorMessage = "En az bir rol seçin.")]
    public List<string> SelectedRoles { get; set; } = [];

    [BindNever, ValidateNever]
    public string Email { get; set; } = string.Empty;

    [BindNever, ValidateNever]
    public List<SelectListItem> AvailableRoles { get; set; } = [];
}

public class ChangeUserStatusViewModel
{
    [Required]
    public string Id { get; set; } = string.Empty;

    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;

    [Required]
    public bool? IsActive { get; set; }
}