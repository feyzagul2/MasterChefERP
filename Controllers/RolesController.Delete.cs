using System.Security.Claims;
using MasterChefTakip.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

public partial class RolesController
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Delete(
        string id,
        string concurrencyStamp)
    {
        try
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync();

            var role = await roleManager.FindByIdAsync(id);

            if (role is null)
                return NotFound();

            if (IsProtectedRole(role.Name))
                return Forbid();

            if (string.IsNullOrWhiteSpace(concurrencyStamp) ||
                role.ConcurrencyStamp != concurrencyStamp)
            {
                TempData["RoleDeleteError"] =
                    "Rol başka bir işlemde değişti. Kontrol edip tekrar deneyin.";

                return RedirectToAction(
                    nameof(Permissions),
                    new { id });
            }

            if (await db.UserRoles.AnyAsync(r => r.RoleId == id))
            {
                TempData["RoleDeleteError"] =
                    "Bu role bağlı kullanıcı var. Önce kullanıcıları "
                    + "başka role taşıyıp tekrar deneyin.";

                return RedirectToAction(
                    nameof(Permissions),
                    new { id });
            }

            var name = role.Name;
            var result = await roleManager.DeleteAsync(role);

            if (!result.Succeeded)
            {
                TempData["RoleDeleteError"] =
                    "Rol silinemedi. Sayfayı yenileyip tekrar deneyin.";

                return RedirectToAction(
                    nameof(Permissions),
                    new { id });
            }

            await transaction.CommitAsync();

            logger.LogInformation(
                "Rol {RoleId} ({RoleName}) kullanıcı {UserId} tarafından silindi.",
                id,
                name,
                User.FindFirstValue(ClaimTypes.NameIdentifier));

            TempData["RoleSuccessMessage"] =
                $"“{name}” rolü silindi.";

            return RedirectToAction(
                "Index",
                "Users",
                new { form = "role" });
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Rol silinemedi: {RoleId}",
                id);

            db.ChangeTracker.Clear();

            TempData["RoleDeleteError"] =
                "Rol silinemedi. Değişiklikler geri alındı.";

            return RedirectToAction(
                nameof(Permissions),
                new { id });
        }
    }
}