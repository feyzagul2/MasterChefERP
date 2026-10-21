using MasterChefTakip.Data;
using MasterChefTakip.Models;
using MasterChefTakip.Services;
using MasterChefTakip.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class UsersController(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    UserManagementService managementService,
    ILogger<UsersController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? form = null, string? editId = null)
    {
        if (!string.IsNullOrWhiteSpace(editId) && !User.IsInRole(AppRoles.Admin))
        {
            return Forbid();
        }

        var page = await BuildPageAsync(form);

        if (!page.CanViewUsers && !page.CanManageRoles)
        {
            return Forbid();
        }

        if (!string.IsNullOrWhiteSpace(editId))
        {
            var account = await userManager.FindByIdAsync(editId);

            if (account is null)
            {
                return NotFound();
            }

            ViewData["EditUserForm"] = new EditUserViewModel
            {
                Id = account.Id,
                ConcurrencyStamp = account.ConcurrencyStamp ?? string.Empty,
                FullName = account.FullName,
                Email = account.Email ?? string.Empty,
                SelectedRoles = (await userManager.GetRolesAsync(account)).ToList(),
                AvailableRoles = page.UserForm.AvailableRoles
            };
        }

        return View(page);
    }

    [HttpGet]
    [Authorize(Policy = AppPermissions.UsersCreate)]
    public IActionResult Create()
    {
        return RedirectToAction(nameof(Index), new { form = "user" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissions.UsersCreate)]
    public async Task<IActionResult> Create(CreateUserViewModel model)
    {
        var page = await managementService.BuildAsync(User, userForm: model);

        var selectedRoles = (model.SelectedRoles ?? [])
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var allowedRoles = model.AvailableRoles
            .Select(role => role.Value)
            .ToHashSet(StringComparer.Ordinal);

        if (selectedRoles.Length == 0)
        {
            ModelState.AddModelError(
                nameof(model.SelectedRoles),
                "En az bir rol seçin.");
        }

        if (selectedRoles.Any(role => !allowedRoles.Contains(role)))
        {
            ModelState.AddModelError(
                nameof(model.SelectedRoles),
                "Seçilen rol bulunamadı veya bu rolü atama yetkiniz yok.");
        }

        if (!ModelState.IsValid)
        {
            await LoadActionDataAsync();
            return View("Index", page);
        }

        try
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync();

            foreach (var role in selectedRoles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    ModelState.AddModelError(
                        nameof(model.SelectedRoles),
                        "Seçilen rol artık sistemde bulunmuyor.");

                    await LoadActionDataAsync();
                    return View("Index", page);
                }
            }

            var email = model.Email.Trim();

            var account = new ApplicationUser
            {
                FullName = model.FullName.Trim(),
                Email = email,
                UserName = email,
                EmailConfirmed = false,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            };

            var createResult = await userManager.CreateAsync(
                account,
                model.Password);

            if (!createResult.Succeeded)
            {
                AddIdentityErrors(createResult);
                await transaction.RollbackAsync();
                db.ChangeTracker.Clear();
                await LoadActionDataAsync();

                return View("Index", page);
            }

            var roleResult = await userManager.AddToRolesAsync(
                account,
                selectedRoles);

            if (!roleResult.Succeeded)
            {
                AddIdentityErrors(roleResult);
                await transaction.RollbackAsync();
                db.ChangeTracker.Clear();
                await LoadActionDataAsync();

                return View("Index", page);
            }

            await transaction.CommitAsync();

            TempData["SuccessMessage"] =
                "Kullanıcı hesabı başarıyla oluşturuldu.";

            return RedirectToAction(nameof(Index));
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Kullanıcı hesabı oluşturulurken hata oluştu.");

            db.ChangeTracker.Clear();

            ModelState.AddModelError(
                string.Empty,
                "Hesap oluşturulamadı. Lütfen tekrar deneyin.");

            await LoadActionDataAsync();

            return View("Index", page);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Edit(
        [Bind(Prefix = "Edit")] EditUserViewModel model)
    {
        var page = await BuildPageAsync();

        model.AvailableRoles = page.UserForm.AvailableRoles;

        var selectedRoles = (model.SelectedRoles ?? [])
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var allowedRoles = model.AvailableRoles
            .Select(role => role.Value)
            .ToHashSet(StringComparer.Ordinal);

        if (selectedRoles.Length == 0)
        {
            ModelState.AddModelError(
                "Edit.SelectedRoles",
                "En az bir rol seçin.");
        }

        if (selectedRoles.Any(role => !allowedRoles.Contains(role)))
        {
            ModelState.AddModelError(
                "Edit.SelectedRoles",
                "Seçilen rol sistemde bulunamadı.");
        }

        if (!ModelState.IsValid)
        {
            return await EditPageAsync(model);
        }

        try
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync();

            var account = await userManager.FindByIdAsync(model.Id);

            if (account is null)
            {
                return NotFound();
            }

            if (!string.Equals(
                    account.ConcurrencyStamp,
                    model.ConcurrencyStamp,
                    StringComparison.Ordinal))
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Bu kullanıcı başka bir işlemde değiştirildi. Sayfayı yenileyin.");

                return await EditPageAsync(model);
            }

            var currentRoles = await userManager.GetRolesAsync(account);

            var removesAdmin =
                currentRoles.Contains(
                    AppRoles.Admin,
                    StringComparer.OrdinalIgnoreCase)
                && !selectedRoles.Contains(
                    AppRoles.Admin,
                    StringComparer.OrdinalIgnoreCase);

            if (removesAdmin && account.Id == userManager.GetUserId(User))
            {
                ModelState.AddModelError(
                    "Edit.SelectedRoles",
                    "Kendi hesabınızın Yönetici rolünü kaldıramazsınız.");

                return await EditPageAsync(model);
            }

            if (removesAdmin
                && account.IsActive
                && !await HasOtherActiveAdminAsync(account.Id))
            {
                ModelState.AddModelError(
                    "Edit.SelectedRoles",
                    "Sistemde en az bir aktif yönetici bulunmalıdır.");

                return await EditPageAsync(model);
            }

            account.FullName = model.FullName.Trim();

            var updateResult = await userManager.UpdateAsync(account);

            if (!updateResult.Succeeded)
            {
                await transaction.RollbackAsync();
                db.ChangeTracker.Clear();
                AddIdentityErrors(updateResult);

                return await EditPageAsync(model);
            }

            var toRemove = currentRoles
                .Except(selectedRoles, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var toAdd = selectedRoles
                .Except(currentRoles, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (toRemove.Length > 0)
            {
                var result = await userManager.RemoveFromRolesAsync(
                    account,
                    toRemove);

                if (!result.Succeeded)
                {
                    await transaction.RollbackAsync();
                    db.ChangeTracker.Clear();
                    AddIdentityErrors(result);

                    return await EditPageAsync(model);
                }
            }

            if (toAdd.Length > 0)
            {
                var result = await userManager.AddToRolesAsync(
                    account,
                    toAdd);

                if (!result.Succeeded)
                {
                    await transaction.RollbackAsync();
                    db.ChangeTracker.Clear();
                    AddIdentityErrors(result);

                    return await EditPageAsync(model);
                }
            }

            if (toAdd.Length > 0 || toRemove.Length > 0)
            {
                var stampResult =
                    await userManager.UpdateSecurityStampAsync(account);

                if (!stampResult.Succeeded)
                {
                    await transaction.RollbackAsync();
                    db.ChangeTracker.Clear();
                    AddIdentityErrors(stampResult);

                    return await EditPageAsync(model);
                }
            }

            await transaction.CommitAsync();

            TempData["SuccessMessage"] =
                "Kullanıcı bilgileri güncellendi.";

            return RedirectToAction(nameof(Index));
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Kullanıcı güncellenirken hata oluştu.");

            db.ChangeTracker.Clear();

            ModelState.AddModelError(
                string.Empty,
                "Bilgiler kaydedilemedi. Sayfayı yenileyip tekrar deneyin.");

            return await EditPageAsync(model);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> SetActive(
        ChangeUserStatusViewModel model)
    {
        if (!ModelState.IsValid || model.IsActive is not bool isActive)
        {
            TempData["ErrorMessage"] =
                "İşlem bilgileri geçersiz. Sayfayı yenileyin.";

            return RedirectToAction(nameof(Index));
        }

        if (model.Id == userManager.GetUserId(User)
            && !isActive)
        {
            TempData["ErrorMessage"] =
                "Kendi hesabınızı pasife alamazsınız.";

            return RedirectToAction(nameof(Index));
        }

        try
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync();

            var account = await userManager.FindByIdAsync(model.Id);

            if (account is null)
            {
                return NotFound();
            }

            if (!string.Equals(
                    account.ConcurrencyStamp,
                    model.ConcurrencyStamp,
                    StringComparison.Ordinal))
            {
                TempData["ErrorMessage"] =
                    "Bu kullanıcı başka bir işlemde değiştirildi. Sayfayı yenileyin.";

                return RedirectToAction(nameof(Index));
            }

            if (account.IsActive == isActive)
            {
                TempData["SuccessMessage"] =
                    "Kullanıcının durumu zaten güncel.";

                return RedirectToAction(nameof(Index));
            }

            if (!isActive
                && account.IsActive
                && await userManager.IsInRoleAsync(account, AppRoles.Admin)
                && !await HasOtherActiveAdminAsync(account.Id))
            {
                TempData["ErrorMessage"] =
                    "Son aktif yönetici pasife alınamaz.";

                return RedirectToAction(nameof(Index));
            }

            account.IsActive = isActive;

            var updateResult = await userManager.UpdateAsync(account);

            if (!updateResult.Succeeded)
            {
                TempData["ErrorMessage"] =
                    "Durum kaydedilemedi. Sayfayı yenileyin.";

                return RedirectToAction(nameof(Index));
            }

            var stampResult =
                await userManager.UpdateSecurityStampAsync(account);

            if (!stampResult.Succeeded)
            {
                TempData["ErrorMessage"] =
                    "Durum kaydedilemedi. Sayfayı yenileyin.";

                return RedirectToAction(nameof(Index));
            }

            await transaction.CommitAsync();

            TempData["SuccessMessage"] = account.IsActive
                ? "Kullanıcı aktif hale getirildi. Kendi hesabıyla giriş yapabilir."
                : "Kullanıcı pasife alındı. Uygulamaya erişimi kapatıldı.";

            return RedirectToAction(nameof(Index));
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Kullanıcı durumu değiştirilirken hata oluştu.");

            TempData["ErrorMessage"] =
                "Durum değiştirilemedi. Lütfen tekrar deneyin.";

            return RedirectToAction(nameof(Index));
        }
    }

    private async Task<UserManagementViewModel> BuildPageAsync(
        string? form = null)
    {
        var page = await managementService.BuildAsync(
            User,
            showUserForm: form == "user",
            showRoleForm: form == "role");

        await LoadActionDataAsync();

        return page;
    }

    private async Task LoadActionDataAsync()
    {
        var isAdmin = User.IsInRole(AppRoles.Admin);

        ViewData["CanEditUsers"] = isAdmin;

        if (isAdmin)
        {
            ViewData["UserStamps"] = await db.Users
                .AsNoTracking()
                .ToDictionaryAsync(
                    account => account.Id,
                    account => account.ConcurrencyStamp ?? string.Empty);
        }
    }

    private async Task<IActionResult> EditPageAsync(
        EditUserViewModel model)
    {
        var page = await BuildPageAsync();

        var account = await userManager.FindByIdAsync(model.Id);

        if (account is null)
        {
            return NotFound();
        }

        model.Email = account.Email ?? string.Empty;
        model.SelectedRoles ??= [];
        model.AvailableRoles = page.UserForm.AvailableRoles;

        ViewData["EditUserForm"] = model;

        return View("Index", page);
    }

    private async Task<bool> HasOtherActiveAdminAsync(
        string excludedUserId)
    {
        var adminRole = await roleManager.FindByNameAsync(AppRoles.Admin);

        if (adminRole is null)
        {
            return false;
        }

        return await (
            from account in db.Users
            join membership in db.UserRoles
                on account.Id equals membership.UserId
            where account.IsActive
                && account.Id != excludedUserId
                && membership.RoleId == adminRole.Id
            select account.Id
        ).AnyAsync();
    }

    private void AddIdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            var message = error.Code switch
            {
                "DuplicateEmail" or "DuplicateUserName" =>
                    "Bu e-posta adresiyle kayıtlı bir hesap zaten var.",

                "InvalidEmail" =>
                    "Geçerli bir e-posta adresi girin.",

                "InvalidUserName" =>
                    "E-posta adresinde desteklenmeyen bir karakter var.",

                "PasswordTooShort" =>
                    "Şifre en az 10 karakter olmalıdır.",

                "PasswordRequiresDigit" =>
                    "Şifre en az bir rakam içermelidir.",

                "PasswordRequiresLower" =>
                    "Şifre en az bir küçük harf içermelidir.",

                "PasswordRequiresUpper" =>
                    "Şifre en az bir büyük harf içermelidir.",

                "PasswordRequiresNonAlphanumeric" =>
                    "Şifre en az bir özel karakter içermelidir.",

                "ConcurrencyFailure" =>
                    "Bilgiler başka bir işlemde değiştirildi. Sayfayı yenileyin.",

                _ => "İşlem tamamlanamadı. Bilgileri kontrol edin."
            };

            ModelState.AddModelError(string.Empty, message);
        }
    }
}