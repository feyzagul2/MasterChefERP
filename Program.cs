using MasterChefTakip.Data;
using MasterChefTakip.Models;
using MasterChefTakip.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "DefaultConnection bağlantı ayarı bulunamadı.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services
    .AddDefaultIdentity<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.User.RequireUniqueEmail = true;

        options.Password.RequiredLength = 10;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;

        options.Lockout.DefaultLockoutTimeSpan =
            TimeSpan.FromMinutes(15);

        options.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

// Rol ve yetki değişiklikleri sonraki istekte yenilenir.
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.Zero;
});

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "MasterChefTakip.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;

    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;

    options.Events.OnValidatePrincipal = async context =>
    {
        if (context.Principal is null)
        {
            context.RejectPrincipal();
            return;
        }

        var manager = context.HttpContext.RequestServices
            .GetRequiredService<UserManager<ApplicationUser>>();

        var account = await manager.GetUserAsync(context.Principal);

        if (account is null || !account.IsActive)
        {
            context.RejectPrincipal();

            await context.HttpContext.SignOutAsync(
                IdentityConstants.ApplicationScheme);

            return;
        }

        await SecurityStampValidator.ValidatePrincipalAsync(context);
    };
});

builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();
builder.Services.AddRazorPages();

builder.Services.AddScoped<UserManagementService>();

// Bütün yetkiler tek noktadan tanımlanır.
OperationAuthorization.Configure(builder.Services);

builder.Services.AddScoped<MasterChefTakip.Controllers.MCFinanceErrorFilter>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// Yeni kullanıcıları yönetici oluşturur.
// Herkese açık kayıt sayfaları kapalıdır.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments(
            "/Identity/Account/Register")
        || context.Request.Path.StartsWithSegments(
            "/Identity/Account/RegisterConfirmation"))
    {
        context.Response.StatusCode =
            StatusCodes.Status404NotFound;

        return;
    }

    await next(context);
});

app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages()
    .WithStaticAssets();

    // MC_ATOMIC_SHIPMENT_SQLITE_V1
    // SQLite yeni sevkiyat INSERT islemlerinde
    // atomik stok yeterlilik kontrolu uygular.
    using (var mcShipmentGuardScope = app.Services.CreateScope())
    {
        var mcShipmentGuardDb =
            mcShipmentGuardScope.ServiceProvider
                .GetRequiredService<
                    global::MasterChefTakip.Data.ApplicationDbContext>();

        global::MasterChefTakip.Controllers
            .MCShipmentAtomicSqliteGuard
            .Install(mcShipmentGuardDb);
    }

// MC_CARI_ACCOUNTS_LEGACY_REDIRECT_V1
app.MapGet("/MCCariAccounts", context =>
{
    context.Response.Redirect("/CariAccounts", permanent: false);
    return Task.CompletedTask;
});
app.Run();