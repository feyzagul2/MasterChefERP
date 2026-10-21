using System.ComponentModel.DataAnnotations;

namespace MasterChefTakip.ViewModels;

public class EditRolePermissionsViewModel
{
    [Required]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;

    public List<string> SelectedPermissions { get; set; } = [];
}