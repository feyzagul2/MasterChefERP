using System.ComponentModel.DataAnnotations;

namespace MasterChefTakip.ViewModels;

public class CreateRoleViewModel
{
    [Display(Name = "Rol adı")]
    [Required(ErrorMessage = "Rol adını girin.")]
    [StringLength(
        100,
        ErrorMessage = "Rol adı en fazla 100 karakter olabilir.")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Yetkiler")]
    public List<string> SelectedPermissions { get; set; } = [];
}