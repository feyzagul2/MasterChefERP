using System.Security.Claims;
using MasterChefTakip.Models;
using Microsoft.Extensions.DependencyInjection;

namespace MasterChefTakip.Services;

public static class OperationAuthorization
{
    public static void Configure(IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            foreach (var permission in AppPermissions.All)
            {
                options.AddPolicy(permission.Value, policy =>
                {
                    policy.RequireAuthenticatedUser();

                    policy.RequireAssertion(context =>
                        Can(context.User, permission.Value));
                });
            }
        });
    }

    public static bool Can(
        ClaimsPrincipal user,
        string permission)
    {
        if (user.Identity?.IsAuthenticated != true)
            return false;

        if (!AppPermissions.All.Any(p => p.Value == permission))
            return false;

        if (user.IsInRole(AppRoles.Admin))
            return true;

        // Kullanıcı ve rol yönetimi yalnızca yöneticiye açık.
        if (AppPermissions.IsAdministration(permission))
            return false;

        if (user.HasClaim(AppPermissions.ClaimType, permission))
            return true;

        // İşlem izni, gerekli görüntüleme erişimini de sağlar.
        // Başka bir işlem yapma izni vermez.
        return permission switch
        {
            AppPermissions.HistoryView => HasAny(
                user,
                AppPermissions.PalletsTransfer,
                AppPermissions.HistoryAll,
                AppPermissions.PackagingManage,
                AppPermissions.PalletsLocation,
                AppPermissions.PalletsManage,
                AppPermissions.StockManage,
                AppPermissions.ShipmentsManage,
                AppPermissions.ReturnsReceive,
                AppPermissions.ReturnsInspect,
                AppPermissions.ReturnsDamage,
                AppPermissions.ReturnsCancel),

            AppPermissions.ProductsView => HasAny(
                user,
                AppPermissions.ProductsCreate,
                AppPermissions.ProductsManage,
                AppPermissions.PackagingManage),

            AppPermissions.ProductsPrices => HasAny(
                user,
                AppPermissions.ProductsCreate,
                AppPermissions.ProductsManage),

            AppPermissions.PalletsView => HasAny(user,
                AppPermissions.PalletsTransfer,
                AppPermissions.PalletsLocation,
                AppPermissions.PalletsManage,
                AppPermissions.StockManage,
                AppPermissions.PackagingManage),

            AppPermissions.ShipmentsView => HasAny(
                user,
                AppPermissions.ShipmentsManage,
                AppPermissions.ShipmentsLabels,
                AppPermissions.ShipmentsCancel),

            AppPermissions.ReturnsView => HasAny(
                user,
                AppPermissions.ReturnsReceive,
                AppPermissions.ReturnsInspect,
                AppPermissions.ReturnsDamage,
                AppPermissions.ReturnsCancel),

            _ => false
        };
    }

    private static bool HasAny(
        ClaimsPrincipal user,
        params string[] permissions)
    {
        return permissions.Any(permission =>
            user.HasClaim(AppPermissions.ClaimType, permission));
    }
}