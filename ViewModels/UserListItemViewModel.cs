namespace MasterChefTakip.ViewModels;

public class UserListItemViewModel
{
    public string Id { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public List<string> Roles { get; set; } = [];

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}