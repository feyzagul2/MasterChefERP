using System.Data;
using System.Net;
using MasterChefTakip.Data;
using MasterChefTakip.Models;
using MasterChefTakip.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

[AllowAnonymous]
[Route("Kurulum")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class SetupController(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IWebHostEnvironment environment,
    ILogger<SetupController> logger) : Controller
{
    private static readonly SemaphoreSlim SetupLock = new(1, 1);

    private bool IsLocalSetup()
    {
        var address = HttpContext.Connection.RemoteIpAddress;

        if (address?.IsIPv4MappedToIPv6 == true)
        {
            address = address.MapToIPv4();
        }

        return environment.IsDevelopment()
            && Request.IsHttps
            && address is not null
            && IPAddress.IsLoopback(address);
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (!IsLocalSetup() || await db.Users.AnyAsync())
        {
            return NotFound();
        }

        return View(new SetupViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(SetupViewModel model)
    {
        if (!IsLocalSetup())
        {
            return NotFound();
        }

        await SetupLock.WaitAsync();

        try
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable);

            if (await db.Users.AnyAsync())
            {
                return NotFound();
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            foreach (var roleName in AppRoles.All)
            {
                if (await roleManager.RoleExistsAsync(roleName))
                {
                    continue;
                }

                var roleResult = await roleManager.CreateAsync(
                    new IdentityRole(roleName));

                if (!roleResult.Succeeded)
                {
                    ModelState.AddModelError(
                        string.Empty,
                        "Yetkiler oluşturulamadı. İşlem kaydedilmedi.");

                    return View(model);
                }
            }

            var email = model.Email.Trim();

            var user = new ApplicationUser
            {
                FullName = model.FullName.Trim(),
                UserName = email,
                Email = email,
                EmailConfirmed = false,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            };

            var createResult =
                await userManager.CreateAsync(user, model.Password);

            if (!createResult.Succeeded)
            {
                foreach (var error in createResult.Errors)
                {
                    ModelState.AddModelError(
                        string.Empty, error.Description);
                }

                return View(model);
            }

            var assignResult =
                await userManager.AddToRoleAsync(user, AppRoles.Admin);

            if (!assignResult.Succeeded)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Yönetici yetkisi atanamadı. İşlem kaydedilmedi.");

                return View(model);
            }

            await transaction.CommitAsync();

            return RedirectToPage(
                "/Account/Login",
                new { area = "Identity" });
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception, "İlk yönetici kurulumu tamamlanamadı.");

            ModelState.AddModelError(
                string.Empty,
                "Kurulum tamamlanamadı. Lütfen tekrar deneyin.");

            return View(model);
        }
        finally
        {
            SetupLock.Release();
        }
    }
}