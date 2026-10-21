using System.Security.Claims;
using MasterChefTakip.Data;
using MasterChefTakip.Models;
using MasterChefTakip.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Services;

public class UserManagementService(
    ApplicationDbContext db,
    IAuthorizationService authorizationService)
{
    public async Task<UserManagementViewModel> BuildAsync(
        ClaimsPrincipal currentUser,
        CreateUserViewModel? userForm = null,
        CreateRoleViewModel? roleForm = null,
        bool showUserForm = false,
        bool showRoleForm = false)
    {
        var allowedPermissions =
            new HashSet<string>(StringComparer.Ordinal);

        foreach (var permission in AppPermissions.All)
        {
            var result = await authorizationService.AuthorizeAsync(
                currentUser,
                permission.Value);

            if (result.Succeeded)
            {
                allowedPermissions.Add(permission.Value);
            }
        }

        var model = new UserManagementViewModel
        {
            CanViewUsers =
                allowedPermissions.Contains(AppPermissions.UsersView),

            CanCreateUsers =
                allowedPermissions.Contains(AppPermissions.UsersCreate),

            CanManageRoles =
                allowedPermissions.Contains(AppPermissions.RolesManage),

            UserForm = userForm ?? new CreateUserViewModel(),
            RoleForm = roleForm ?? new CreateRoleViewModel()
        };

        model.UserForm.AvailableRoles = [];

        model.ShowUserForm = model.CanCreateUsers
            && (showUserForm || userForm is not null);

        model.ShowRoleForm = model.CanManageRoles
            && (showRoleForm || roleForm is not null);

        if (!model.CanViewUsers && !model.CanManageRoles)
        {
            return model;
        }

        var roles = await db.Roles
            .AsNoTracking()
            .OrderBy(role => role.Name)
            .ToListAsync();

        var memberships = await db.UserRoles
            .AsNoTracking()
            .ToListAsync();

        var roleClaims = await db.RoleClaims
            .AsNoTracking()
            .Where(claim => claim.ClaimType == AppPermissions.ClaimType)
            .ToListAsync();

        var roleNames = roles.ToDictionary(
            role => role.Id,
            role => string.Equals(
                role.Name,
                AppRoles.Admin,
                StringComparison.OrdinalIgnoreCase)
                    ? "Yönetici"
                    : role.Name ?? string.Empty);

        var permissionLabels = AppPermissions.All.ToDictionary(
            permission => permission.Value,
            permission => permission.Label,
            StringComparer.Ordinal);

        var isAdmin = currentUser.IsInRole(AppRoles.Admin);

        foreach (var role in roles)
        {
            if (string.IsNullOrWhiteSpace(role.Name))
            {
                continue;
            }

            var isAdminRole = string.Equals(
                role.Name,
                AppRoles.Admin,
                StringComparison.OrdinalIgnoreCase);

            var permissions = roleClaims
                .Where(claim => claim.RoleId == role.Id)
                .Select(claim => claim.ClaimValue ?? string.Empty)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (model.CanManageRoles)
            {
                model.Roles.Add(new RoleListItemViewModel
                {
                    Id = role.Id,
                    Name = roleNames[role.Id],
                    UserCount = memberships.Count(
                        membership => membership.RoleId == role.Id),

                    PermissionLabels = isAdminRole
                        ? AppPermissions.All
                            .Select(permission => permission.Label)
                            .ToList()
                        : permissions
                            .Select(permission =>
                                permissionLabels.GetValueOrDefault(
                                    permission,
                                    "Tanımlanmamış yetki"))
                            .ToList()
                });
            }

            var canAssignRole = isAdmin
                || (!isAdminRole && permissions.All(
                    permission => allowedPermissions.Contains(permission)));

            if (model.CanCreateUsers && canAssignRole)
            {
                model.UserForm.AvailableRoles.Add(new SelectListItem
                {
                    Value = role.Name,
                    Text = roleNames[role.Id]
                });
            }
        }

        if (model.CanViewUsers)
        {
            var users = await db.Users
                .AsNoTracking()
                .OrderBy(user => user.FullName)
                .ThenBy(user => user.Email)
                .ToListAsync();

            foreach (var user in users)
            {
                var userRoles = memberships
                    .Where(membership => membership.UserId == user.Id)
                    .Select(membership =>
                        roleNames.GetValueOrDefault(
                            membership.RoleId,
                            string.Empty))
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .OrderBy(name => name)
                    .ToList();

                model.Users.Add(new UserListItemViewModel
                {
                    Id = user.Id,
                    FullName = user.FullName,
                    Email = user.Email ?? string.Empty,
                    Roles = userRoles,
                    IsActive = user.IsActive,
                    CreatedAtUtc = user.CreatedAtUtc
                });
            }
        }

        return model;
    }
}