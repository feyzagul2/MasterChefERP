using System.Security.Claims;
using MasterChefTakip.Data;
using MasterChefTakip.Models;
using MasterChefTakip.Services;
using MasterChefTakip.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

[Authorize(Policy = AppPermissions.RolesManage)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public partial class RolesController(
    ApplicationDbContext db,
    RoleManager<IdentityRole> roleManager,
    IAuthorizationService authorizationService,
    UserManagementService managementService,
    ILogger<RolesController> logger) : Controller
{
    [HttpGet]
    public IActionResult Create()
    {
        return RedirectToAction(
            "Index",
            "Users",
            new { form = "role" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateRoleViewModel model)
    {
        var page = await managementService.BuildAsync(
            User,
            roleForm: model);

        var permissions = (model.SelectedPermissions ?? [])
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var knownPermissions = AppPermissions.All
    .Where(permission =>
        !AppPermissions.IsAdministration(permission.Value))
    .Select(permission => permission.Value)
    .ToHashSet(StringComparer.Ordinal);

        if (permissions.Any(permission =>
            !knownPermissions.Contains(permission)))
        {
            ModelState.AddModelError(
                nameof(model.SelectedPermissions),
                "Geçersiz bir yetki seçildi.");
        }

        if (!ModelState.IsValid)
        {
            return View("/Views/Users/Index.cshtml", page);
        }

        model.Name = model.Name.Trim();

        var normalizedName = roleManager.NormalizeKey(model.Name);

        if (normalizedName == roleManager.NormalizeKey(AppRoles.Admin)
            || normalizedName == roleManager.NormalizeKey("Yönetici"))
        {
            ModelState.AddModelError(
                nameof(model.Name),
                "Bu ad mevcut yönetici rolü için ayrılmıştır.");

            return View("/Views/Users/Index.cshtml", page);
        }

        foreach (var permission in permissions)
        {
            var authorization = await authorizationService.AuthorizeAsync(
                User,
                resource: null,
                policyName: permission);

            if (!authorization.Succeeded)
            {
                ModelState.AddModelError(
                    nameof(model.SelectedPermissions),
                    "Kendi erişiminizi aşan bir yetki atayamazsınız.");

                return View("/Views/Users/Index.cshtml", page);
            }
        }

        try
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync();

            if (await roleManager.RoleExistsAsync(model.Name))
            {
                ModelState.AddModelError(
                    nameof(model.Name),
                    "Bu isimde bir rol zaten var.");

                return View("/Views/Users/Index.cshtml", page);
            }

            var role = new IdentityRole(model.Name);

            var createResult = await roleManager.CreateAsync(role);

            if (!createResult.Succeeded)
            {
                AddRoleErrors(createResult);
                return View("/Views/Users/Index.cshtml", page);
            }

            foreach (var permission in permissions)
            {
                var claimResult = await roleManager.AddClaimAsync(
                    role,
                    new Claim(AppPermissions.ClaimType, permission));

                if (!claimResult.Succeeded)
                {
                    AddRoleErrors(claimResult);
                    return View("/Views/Users/Index.cshtml", page);
                }
            }

            await transaction.CommitAsync();

            TempData["RoleSuccessMessage"] =
                "Rol ve seçilen yetkiler başarıyla kaydedildi.";

            return RedirectToAction(
                "Index",
                "Users",
                new { form = "role" });
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Rol oluşturulurken hata oluştu.");

            ModelState.AddModelError(
                string.Empty,
                "Rol kaydedilemedi. Lütfen tekrar deneyin.");

            return View("/Views/Users/Index.cshtml", page);
        }
    }

    private void AddRoleErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            if (error.Code == "DuplicateRoleName")
            {
                ModelState.AddModelError(
                    nameof(CreateRoleViewModel.Name),
                    "Bu isimde bir rol zaten var.");
            }
            else if (error.Code == "InvalidRoleName")
            {
                ModelState.AddModelError(
                    nameof(CreateRoleViewModel.Name),
                    "Geçerli bir rol adı girin.");
            }
            else
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Rol veya yetkiler kaydedilemedi.");
            }
        }
    }
}