namespace MasterChefTakip.ViewModels;

public class UserManagementViewModel
{
    public List<UserListItemViewModel> Users { get; set; } = [];

    public List<RoleListItemViewModel> Roles { get; set; } = [];

    public CreateUserViewModel UserForm { get; set; } = new();

    public CreateRoleViewModel RoleForm { get; set; } = new();

    public bool CanViewUsers { get; set; }

    public bool CanCreateUsers { get; set; }

    public bool CanManageRoles { get; set; }

    public bool ShowUserForm { get; set; }

    public bool ShowRoleForm { get; set; }
}

public class RoleListItemViewModel
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public List<string> PermissionLabels { get; set; } = [];

    public int UserCount { get; set; }
}