using System.Security.Claims;
using MasterChefTakip.Models;
using MasterChefTakip.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MasterChefTakip.Controllers;

public partial class RolesController
{
    [HttpGet]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Permissions(string id)
    {
        var role = await roleManager.FindByIdAsync(id);

        if (role is null)
            return NotFound();

        if (IsProtectedRole(role.Name))
            return Forbid();

        var claims = await roleManager.GetClaimsAsync(role);

        var model = new EditRolePermissionsViewModel
        {
            Id = role.Id,
            Name = role.Name ?? string.Empty,
            ConcurrencyStamp = role.ConcurrencyStamp ?? string.Empty,

            SelectedPermissions = claims
                .Where(claim =>
                    claim.Type == AppPermissions.ClaimType)
                .Select(claim => claim.Value)
                .Distinct()
                .ToList()
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Permissions(
        EditRolePermissionsViewModel model)
    {
        var role = await roleManager.FindByIdAsync(model.Id);

        if (role is null)
            return NotFound();

        if (IsProtectedRole(role.Name))
            return Forbid();

        model.Name = role.Name ?? string.Empty;

        var selected = (model.SelectedPermissions ?? [])
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var allowed = AppPermissions.All
            .Where(permission =>
                !AppPermissions.IsAdministration(permission.Value))
            .Select(permission => permission.Value)
            .ToHashSet(StringComparer.Ordinal);

        if (selected.Any(permission =>
            !allowed.Contains(permission)))
        {
            ModelState.AddModelError(
                string.Empty,
                "Geçersiz bir yetki seçildi.");
        }

        if (role.ConcurrencyStamp != model.ConcurrencyStamp)
        {
            ModelState.AddModelError(
                string.Empty,
                "Rol başka bir işlemde değişti. Sayfayı yenileyin.");

            return View(model);
        }

        if (!ModelState.IsValid)
            return View(model);

        try
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync();

            var update = await roleManager.UpdateAsync(role);

            if (!update.Succeeded)
            {
                throw new InvalidOperationException(
                    "Rol güncellenemedi.");
            }

            var oldClaims = await roleManager.GetClaimsAsync(role);

            foreach (var claim in oldClaims.Where(claim =>
                claim.Type == AppPermissions.ClaimType))
            {
                var result = await roleManager.RemoveClaimAsync(
                    role,
                    claim);

                if (!result.Succeeded)
                {
                    throw new InvalidOperationException(
                        "Eski yetki kaldırılamadı.");
                }
            }

            foreach (var permission in selected)
            {
                var result = await roleManager.AddClaimAsync(
                    role,
                    new Claim(
                        AppPermissions.ClaimType,
                        permission));

                if (!result.Succeeded)
                {
                    throw new InvalidOperationException(
                        "Yetki kaydedilemedi.");
                }
            }

            await transaction.CommitAsync();

            logger.LogInformation(
                "Rol {RoleId} izinleri kullanıcı {UserId} "
                + "tarafından değiştirildi: {Permissions}",
                role.Id,
                User.FindFirstValue(ClaimTypes.NameIdentifier),
                string.Join(",", selected));

            TempData["RoleSuccessMessage"] =
                "Rol yetkileri kaydedildi. Bu role bağlı kullanıcılar "
                + "sonraki isteklerinde yeni izinlerle çalışır.";

            return RedirectToAction(
                "Index",
                "Users",
                new { form = "role" });
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Rol yetkileri kaydedilemedi: {RoleId}",
                model.Id);

            db.ChangeTracker.Clear();

            ModelState.AddModelError(
                string.Empty,
                "Yetkiler kaydedilemedi. "
                + "Sayfayı yenileyip tekrar deneyin.");

            return View(model);
        }
    }

    private static bool IsProtectedRole(string? name)
    {
        return string.Equals(
                   name,
                   AppRoles.Admin,
                   StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                   name,
                   "Yönetici",
                   StringComparison.OrdinalIgnoreCase);
    }
}