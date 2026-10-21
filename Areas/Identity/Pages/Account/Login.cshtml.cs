using System.ComponentModel.DataAnnotations;
using MasterChefTakip.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MasterChefTakip.Areas.Identity.Pages.Account;

[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class LoginModel(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string ReturnUrl { get; private set; } = "/";

    public class InputModel
    {
        [Display(Name = "E-posta")]
        [Required(ErrorMessage = "E-posta adresinizi girin.")]
        [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi girin.")]
        [StringLength(254,
            ErrorMessage = "E-posta en fazla 254 karakter olabilir.")]
        public string Email { get; set; } = string.Empty;

        [Display(Name = "Şifre")]
        [Required(ErrorMessage = "Şifrenizi girin.")]
        [StringLength(128,
            ErrorMessage = "Şifre en fazla 128 karakter olabilir.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Beni hatırla")]
        public bool RememberMe { get; set; }
    }

    public IActionResult OnGet(string? returnUrl = null)
    {
        ReturnUrl = GetLocalUrl(returnUrl);

        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(ReturnUrl);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(
        string? returnUrl = null)
    {
        ReturnUrl = GetLocalUrl(returnUrl);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await userManager.FindByEmailAsync(
            Input.Email.Trim());

        if (user is null || !user.IsActive)
        {
            ModelState.AddModelError(
                string.Empty,
                "E-posta veya şifre hatalı ya da hesabınız girişe kapalı.");

            return Page();
        }

        var result = await signInManager.PasswordSignInAsync(
            user,
            Input.Password,
            Input.RememberMe,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            return LocalRedirect(ReturnUrl);
        }

        if (result.RequiresTwoFactor)
        {
            return RedirectToPage(
                "./LoginWith2fa",
                new
                {
                    ReturnUrl,
                    RememberMe = Input.RememberMe
                });
        }

        ModelState.AddModelError(
            string.Empty,
            result.IsLockedOut
                ? "Çok fazla hatalı deneme yapıldı. Lütfen daha sonra tekrar deneyin."
                : "E-posta veya şifre hatalı ya da hesabınız girişe kapalı.");

        return Page();
    }

    private string GetLocalUrl(string? returnUrl)
    {
        return Url.IsLocalUrl(returnUrl) ? returnUrl! : "/";
    }
}