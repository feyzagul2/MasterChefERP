
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MasterChefTakip.Models;
using MasterChefTakip.Data;

namespace MasterChefTakip.Controllers;

[Authorize(Roles = AppRoles.Admin)]
public class CariAccountsController : Controller
{
    private readonly ApplicationDbContext _db;

    public CariAccountsController(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index(string? search, string? type, bool showInactive = false)
    {
        var q = _db.Set<CariAccount>().AsNoTracking().AsQueryable();

        if (!showInactive)
            q = q.Where(x => x.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x =>
                x.Code.Contains(s) ||
                x.CompanyName.Contains(s) ||
                (x.TaxNumber != null && x.TaxNumber.Contains(s)) ||
                (x.Phone != null && x.Phone.Contains(s)));
        }

        if (type == "customer")
            q = q.Where(x => x.IsCustomer);

        if (type == "supplier")
            q = q.Where(x => x.IsSupplier);

        ViewBag.Search = search;
        ViewBag.Type = type;
        ViewBag.ShowInactive = showInactive;

        return View(await q.OrderBy(x => x.CompanyName).Take(500).ToListAsync());
    }

    public async Task<IActionResult> Details(int id)
    {
        var item = await _db.Set<CariAccount>().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);

        return item == null ? NotFound() : View(item);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View("Form", new CariAccount { IsCustomer = true, Currency = "TRY" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind("Code,CompanyName,IsCustomer,IsSupplier,ContactPerson,TaxOffice,TaxNumber,Phone,Email,Address,City,District,Currency,PaymentTermDays,Notes,AccountGroup,PaymentMethod,PriceListName,DefaultDiscountPercent,CreditLimit,RiskStatus,SalesRepresentative,PaymentTerms,EDocumentType,InvoiceEmail,InvoiceAddress,IsVatExempt,VatExemptionReason,DefaultInvoiceNote,DefaultDispatchNote,PreferredCarrier,DeliveryMethod,DefaultWarehouse,DeliveryInstructions")] CariAccount item)
    {
        Normalize(item);
        Validate(item);

        if (await _db.Set<CariAccount>().AnyAsync(x => x.Code == item.Code))
            ModelState.AddModelError(nameof(item.Code), "Cari kodu zaten kullaniliyor.");

        if (!ModelState.IsValid)
            return View("Form", item);

        item.IsActive = true;
        item.CreatedAtUtc = DateTime.UtcNow;
        item.ConcurrencyStamp = Guid.NewGuid().ToString("N");

        _db.Set<CariAccount>().Add(item);

                // MC_CARI_CREATE_AUDIT_2026
        try
        {
            await using var cariTransaction =
                await _db.Database.BeginTransactionAsync();

            await _db.SaveChangesAsync();

            _db.Set<CariAuditEvent>().Add(new CariAuditEvent
            {
                CariAccountId = item.Id,
                Action = "CariCreated",
                Description = "Cari karti olusturuldu.",
                ActorName = User.Identity?.Name,
                CreatedAtUtc = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
            await cariTransaction.CommitAsync();
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError(
                "",
                "Cari karti kaydedilemedi. Bilgileri kontrol edin."
            );

            return View("Form", item);
        }

        TempData["CariMessage"] = "Cari karti olusturuldu.";
        return RedirectToAction(nameof(Details), new { id = item.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var item = await _db.Set<CariAccount>().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);

        return item == null ? NotFound() : View("Form", item);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        int id,
        [Bind("Id,Code,CompanyName,IsCustomer,IsSupplier,ContactPerson,TaxOffice,TaxNumber,Phone,Email,Address,City,District,Currency,PaymentTermDays,Notes,ConcurrencyStamp,AccountGroup,PaymentMethod,PriceListName,DefaultDiscountPercent,CreditLimit,RiskStatus,SalesRepresentative,PaymentTerms,EDocumentType,InvoiceEmail,InvoiceAddress,IsVatExempt,VatExemptionReason,DefaultInvoiceNote,DefaultDispatchNote,PreferredCarrier,DeliveryMethod,DefaultWarehouse,DeliveryInstructions")] CariAccount item)
    {
        if (id != item.Id)
            return BadRequest();

        Normalize(item);
        Validate(item);

        if (await _db.Set<CariAccount>().AnyAsync(x => x.Code == item.Code && x.Id != id))
            ModelState.AddModelError(nameof(item.Code), "Cari kodu zaten kullaniliyor.");

        if (!ModelState.IsValid)
            return View("Form", item);

        var existing = await _db.Set<CariAccount>().FirstOrDefaultAsync(x => x.Id == id);

        if (existing == null)
            return NotFound();

        if (existing.ConcurrencyStamp != item.ConcurrencyStamp)
        {
            ModelState.AddModelError("", "Kayit baska bir kullanici tarafindan degistirilmis. Sayfayi yenileyin.");
            return View("Form", item);
        }

        existing.Code = item.Code;
        existing.CompanyName = item.CompanyName;
        existing.IsCustomer = item.IsCustomer;
        existing.IsSupplier = item.IsSupplier;
        existing.ContactPerson = item.ContactPerson;
        existing.TaxOffice = item.TaxOffice;
        existing.TaxNumber = item.TaxNumber;
        existing.Phone = item.Phone;
        existing.Email = item.Email;
        existing.Address = item.Address;
        existing.City = item.City;
        existing.District = item.District;
        existing.Currency = item.Currency;
        existing.PaymentTermDays = item.PaymentTermDays;
        existing.Notes = item.Notes;
        existing.AccountGroup = item.AccountGroup;
        existing.PaymentMethod = item.PaymentMethod;
        existing.PriceListName = item.PriceListName;
        existing.DefaultDiscountPercent = item.DefaultDiscountPercent;
        existing.CreditLimit = item.CreditLimit;
        existing.RiskStatus = item.RiskStatus;
        existing.SalesRepresentative = item.SalesRepresentative;
        existing.PaymentTerms = item.PaymentTerms;
        existing.EDocumentType = item.EDocumentType;
        existing.InvoiceEmail = item.InvoiceEmail;
        existing.InvoiceAddress = item.InvoiceAddress;
        existing.IsVatExempt = item.IsVatExempt;
        existing.VatExemptionReason = item.VatExemptionReason;
        existing.DefaultInvoiceNote = item.DefaultInvoiceNote;
        existing.DefaultDispatchNote = item.DefaultDispatchNote;
        existing.PreferredCarrier = item.PreferredCarrier;
        existing.DeliveryMethod = item.DeliveryMethod;
        existing.DefaultWarehouse = item.DefaultWarehouse;
        existing.DeliveryInstructions = item.DeliveryInstructions;
        existing.UpdatedAtUtc = DateTime.UtcNow;
        existing.ConcurrencyStamp = Guid.NewGuid().ToString("N");

        try
        {
            
            // MC_CARI_MAIN_AUDIT_2026
            _db.Set<CariAuditEvent>().Add(new CariAuditEvent
            {
                CariAccountId = existing.Id,
                Action = "CariUpdated",
                Description = "Cari karti bilgileri guncellendi.",
                ActorName = User.Identity?.Name,
                CreatedAtUtc = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError("", "Kayit guncellenemedi.");
            return View("Form", item);
        }

        TempData["CariMessage"] = "Cari karti guncellendi.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStatus(int id, string concurrencyStamp)
    {
        var item = await _db.Set<CariAccount>().FindAsync(id);

        if (item == null)
            return NotFound();

        if (item.ConcurrencyStamp != concurrencyStamp)
        {
            TempData["CariMessage"] = "Kayit degismis. Sayfayi yenileyin.";
            return RedirectToAction(nameof(Details), new { id });
        }

        item.IsActive = !item.IsActive;
        item.UpdatedAtUtc = DateTime.UtcNow;
        item.ConcurrencyStamp = Guid.NewGuid().ToString("N");

        
        // MC_CARI_MAIN_AUDIT_2026
        _db.Set<CariAuditEvent>().Add(new CariAuditEvent
        {
            CariAccountId = item.Id,
            Action = item.IsActive
                ? "CariActivated"
                : "CariDeactivated",
            Description = item.IsActive
                ? "Cari karti aktiflestirildi."
                : "Cari karti pasife alindi.",
            ActorName = User.Identity?.Name,
            CreatedAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Details), new { id });
    }

    private void Validate(CariAccount x)
    {
        if (!x.IsCustomer && !x.IsSupplier)
            ModelState.AddModelError("", "Musteri veya tedarikci secin.");

        if (x.TaxNumber != null &&
            !System.Text.RegularExpressions.Regex.IsMatch(x.TaxNumber, @"^(\d{10}|\d{11})$"))
            ModelState.AddModelError(nameof(x.TaxNumber), "Vergi numarasi 10 veya 11 rakam olmali.");

        
        if (x.DefaultDiscountPercent < 0 || x.DefaultDiscountPercent > 100)
            ModelState.AddModelError(nameof(x.DefaultDiscountPercent),
                "Iskonto orani 0 ile 100 arasinda olmalidir.");

        if (x.CreditLimit < 0)
            ModelState.AddModelError(nameof(x.CreditLimit),
                "Kredi limiti negatif olamaz.");

        if (x.Currency is not ("TRY" or "USD" or "EUR" or "GBP"))
            ModelState.AddModelError(nameof(x.Currency), "Gecersiz para birimi.");
    }

    private static void Normalize(CariAccount x)
    {
        x.Code = (x.Code ?? "").Trim().ToUpperInvariant();
        x.CompanyName = (x.CompanyName ?? "").Trim();
        x.Currency = (x.Currency ?? "TRY").Trim().ToUpperInvariant();
        x.TaxNumber = string.IsNullOrWhiteSpace(x.TaxNumber) ? null : x.TaxNumber.Trim();
        x.ContactPerson = x.ContactPerson?.Trim();
        x.TaxOffice = x.TaxOffice?.Trim();
        x.Phone = x.Phone?.Trim();
        x.Email = x.Email?.Trim();
        x.Address = x.Address?.Trim();
        x.City = x.City?.Trim();
        x.District = x.District?.Trim();
        x.Notes = x.Notes?.Trim();

        x.AccountGroup = x.AccountGroup?.Trim();
        x.PaymentMethod = x.PaymentMethod?.Trim();
        x.PriceListName = x.PriceListName?.Trim();
        x.RiskStatus = (x.RiskStatus ?? "Normal").Trim();
        x.SalesRepresentative = x.SalesRepresentative?.Trim();
        x.PaymentTerms = x.PaymentTerms?.Trim();
        x.EDocumentType = (x.EDocumentType ?? "Unknown").Trim();
        x.InvoiceEmail = x.InvoiceEmail?.Trim();
        x.InvoiceAddress = x.InvoiceAddress?.Trim();
        x.VatExemptionReason = x.VatExemptionReason?.Trim();
        x.DefaultInvoiceNote = x.DefaultInvoiceNote?.Trim();
        x.DefaultDispatchNote = x.DefaultDispatchNote?.Trim();
        x.PreferredCarrier = x.PreferredCarrier?.Trim();
        x.DeliveryMethod = x.DeliveryMethod?.Trim();
        x.DefaultWarehouse = x.DefaultWarehouse?.Trim();
        x.DeliveryInstructions = x.DeliveryInstructions?.Trim();

    }
}
