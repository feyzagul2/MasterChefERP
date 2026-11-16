
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MasterChefTakip.Models;
using MasterChefTakip.Data;
using System.Text.RegularExpressions;

namespace MasterChefTakip.Controllers;

[Authorize(Roles = AppRoles.Admin)]
public class CariDetailsController : Controller
{
    private readonly ApplicationDbContext _db;

    public CariDetailsController(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index(int cariId, string section = "Bank")
    {
        var cari = await _db.Set<CariAccount>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == cariId);

        if (cari == null)
            return NotFound();

        if (section is not ("Bank" or "Contact" or "Address"))
            return BadRequest();

        ViewBag.Cari = cari;
        ViewBag.Section = section;

        var rows = new List<CariRecordRow>();

        if (section == "Bank")
        {
            rows = await _db.Set<CariBankAccount>()
                .AsNoTracking()
                .Where(x => x.CariAccountId == cariId)
                .OrderBy(x => x.Id)
                .Select(x => new CariRecordRow
                {
                    Id = x.Id,
                    Title = x.BankName + (x.IsDefault ? " (Varsayilan)" : ""),
                    Detail = x.AccountHolder + " - " + x.Iban,
                    IsActive = x.IsActive
                }).ToListAsync();
        }

        if (section == "Contact")
        {
            rows = await _db.Set<CariContact>()
                .AsNoTracking()
                .Where(x => x.CariAccountId == cariId)
                .OrderBy(x => x.Id)
                .Select(x => new CariRecordRow
                {
                    Id = x.Id,
                    Title = x.FullName + (x.IsPrimary ? " (Birincil)" : ""),
                    Detail = (x.JobTitle ?? "") + " - " + (x.Phone ?? ""),
                    IsActive = x.IsActive
                }).ToListAsync();
        }

        if (section == "Address")
        {
            rows = await _db.Set<CariAddress>()
                .AsNoTracking()
                .Where(x => x.CariAccountId == cariId)
                .OrderBy(x => x.Id)
                .Select(x => new CariRecordRow
                {
                    Id = x.Id,
                    Title = x.Title + (x.IsDefault ? " (Varsayilan)" : ""),
                    Detail = x.AddressType + " - " + x.AddressLine,
                    IsActive = x.IsActive
                }).ToListAsync();
        }

        return View(rows);
    }

    public async Task<IActionResult> Form(
        int cariId, string section = "Bank", int id = 0)
    {
        var cari = await _db.Set<CariAccount>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == cariId);

        if (cari == null)
            return NotFound();

        var model = new CariRecordInput
        {
            CariAccountId = cariId,
            Section = section,
            Id = id
        };

        if (section == "Bank")
        {
            var x = id == 0 ? null
                : await _db.Set<CariBankAccount>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.Id == id && x.CariAccountId == cariId);

            if (id > 0 && x == null) return NotFound();

            if (x != null)
            {
                model.Values = new Dictionary<string, string?>
                {
                    ["BankName"] = x.BankName,
                    ["BranchName"] = x.BranchName,
                    ["AccountHolder"] = x.AccountHolder,
                    ["Iban"] = x.Iban,
                    ["Currency"] = x.Currency,
                    ["Notes"] = x.Notes
                };
                model.MakePrimary = x.IsDefault;
                model.Stamp = x.ConcurrencyStamp;
            }
        }
        else if (section == "Contact")
        {
            var x = id == 0 ? null
                : await _db.Set<CariContact>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.Id == id && x.CariAccountId == cariId);

            if (id > 0 && x == null) return NotFound();

            if (x != null)
            {
                model.Values = new Dictionary<string, string?>
                {
                    ["FullName"] = x.FullName,
                    ["JobTitle"] = x.JobTitle,
                    ["Phone"] = x.Phone,
                    ["Email"] = x.Email,
                    ["Notes"] = x.Notes
                };
                model.MakePrimary = x.IsPrimary;
                model.Stamp = x.ConcurrencyStamp;
            }
        }
        else if (section == "Address")
        {
            var x = id == 0 ? null
                : await _db.Set<CariAddress>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.Id == id && x.CariAccountId == cariId);

            if (id > 0 && x == null) return NotFound();

            if (x != null)
            {
                model.Values = new Dictionary<string, string?>
                {
                    ["AddressType"] = x.AddressType,
                    ["Title"] = x.Title,
                    ["AddressLine"] = x.AddressLine,
                    ["City"] = x.City,
                    ["District"] = x.District,
                    ["PostalCode"] = x.PostalCode,
                    ["Country"] = x.Country,
                    ["ContactPerson"] = x.ContactPerson,
                    ["Phone"] = x.Phone,
                    ["Notes"] = x.Notes
                };
                model.MakePrimary = x.IsDefault;
                model.Stamp = x.ConcurrencyStamp;
            }
        }
        else
        {
            return BadRequest();
        }

        ViewBag.Cari = cari;
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(
        CariRecordInput input)
    {
        if (input.Section is not ("Bank" or "Contact" or "Address"))
            return BadRequest();

        if (!await _db.Set<CariAccount>().AnyAsync(
            x => x.Id == input.CariAccountId))
            return NotFound();

        string V(string key) =>
            input.Values.TryGetValue(key, out var value)
                ? (value ?? "").Trim()
                : "";

        var required = input.Section switch
        {
            "Bank" => new[] { "BankName", "AccountHolder", "Iban" },
            "Contact" => new[] { "FullName" },
            _ => new[] { "AddressType", "Title", "AddressLine" }
        };

        foreach (var key in required)
            if (string.IsNullOrWhiteSpace(V(key)))
                ModelState.AddModelError(key, key + " zorunludur.");

        if (input.Section == "Bank")
        {
            var iban = Regex.Replace(V("Iban").ToUpperInvariant(), @"\s+", "");
            if (!ValidIban(iban))
                ModelState.AddModelError("Iban", "Ge?ersiz IBAN.");

            if (V("Currency") is not ("TRY" or "USD" or "EUR" or "GBP"))
                ModelState.AddModelError("Currency", "Para birimi se?iniz.");
        }

        if (input.Section == "Address" &&
            V("AddressType") is not ("Merkez" or "Fatura" or "Teslimat" or "Diger"))
            ModelState.AddModelError("AddressType", "Adres t?r? se?iniz.");

        if (input.Section == "Contact" &&
            !string.IsNullOrEmpty(V("Email")) &&
            !new System.ComponentModel.DataAnnotations.EmailAddressAttribute()
                .IsValid(V("Email")))
            ModelState.AddModelError("Email", "Ge?erli e-posta giriniz.");

        if (!ModelState.IsValid)
        {
            TempData["CariError"] = string.Join(" ",
                ModelState.Values
                    .SelectMany(x => x.Errors)
                    .Select(x => x.ErrorMessage));

            return RedirectToAction(nameof(Form), new
            {
                cariId = input.CariAccountId,
                section = input.Section,
                id = input.Id
            });
        }


        // MC_CARI_FIELD_VALIDATION_2026
        var maxLengths = new Dictionary<string, int>
        {
            ["BankName"] = 150,
            ["BranchName"] = 150,
            ["AccountHolder"] = 200,
            ["Iban"] = 34,
            ["Currency"] = 3,
            ["FullName"] = 150,
            ["JobTitle"] = 150,
            ["Phone"] = 30,
            ["Email"] = 200,
            ["AddressType"] = 30,
            ["Title"] = 150,
            ["AddressLine"] = 1000,
            ["City"] = 100,
            ["District"] = 100,
            ["PostalCode"] = 20,
            ["Country"] = 100,
            ["ContactPerson"] = 150,
            ["Notes"] = 1000
        };

        foreach (var pair in maxLengths)
        {
            if (input.Values.TryGetValue(pair.Key, out var text)
                && text != null
                && text.Trim().Length > pair.Value)
            {
                return BadRequest(
                    pair.Key + " alani izin verilen uzunlugu asiyor.");
            }
        }

        bool created = input.Id == 0;

        if (input.Section == "Bank")
        {
            var x = created ? null :
                await _db.Set<CariBankAccount>()
                    .FirstOrDefaultAsync(x =>
                        x.Id == input.Id &&
                        x.CariAccountId == input.CariAccountId);

            if (!created && x == null) return NotFound();
            if (!created && x!.ConcurrencyStamp != input.Stamp)
                return Conflict("Kay?t de?i?mi?. Sayfay? yenileyiniz.");

            var iban = Regex.Replace(V("Iban").ToUpperInvariant(), @"\s+", "");

            if (await _db.Set<CariBankAccount>().AnyAsync(x =>
                x.CariAccountId == input.CariAccountId &&
                x.Iban == iban && x.Id != input.Id))
                return Conflict("Bu IBAN bu firmada zaten kay?tl?.");

            if (created)
            {
                x = new CariBankAccount
                {
                    CariAccountId = input.CariAccountId
                };
                _db.Set<CariBankAccount>().Add(x);
            }

            // MC_CARI_ACTIVE_DEFAULT_GUARD_2026
            if (input.MakePrimary && !x!.IsActive)
                return Conflict("Pasif banka hesabi varsayilan secilemez.");

            x!.IsDefault = input.MakePrimary;
            x.BankName = V("BankName");
            x.BranchName = V("BranchName");
            x.AccountHolder = V("AccountHolder");
            x.Iban = iban;
            x.Currency = V("Currency");
            x.Notes = V("Notes");
            x.UpdatedAtUtc = DateTime.UtcNow;
            x.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        }

        if (input.Section == "Contact")
        {
            var x = created ? null :
                await _db.Set<CariContact>()
                    .FirstOrDefaultAsync(x =>
                        x.Id == input.Id &&
                        x.CariAccountId == input.CariAccountId);

            if (!created && x == null) return NotFound();
            if (!created && x!.ConcurrencyStamp != input.Stamp)
                return Conflict("Kay?t de?i?mi?. Sayfay? yenileyiniz.");

            if (created)
            {
                x = new CariContact
                {
                    CariAccountId = input.CariAccountId
                };
                _db.Set<CariContact>().Add(x);
            }

            // MC_CARI_ACTIVE_DEFAULT_GUARD_2026
            if (input.MakePrimary && !x!.IsActive)
                return Conflict("Pasif firma yetkilisi birincil secilemez.");

            x!.IsPrimary = input.MakePrimary;
            x.FullName = V("FullName");
            x.JobTitle = V("JobTitle");
            x.Phone = V("Phone");
            x.Email = V("Email");
            x.Notes = V("Notes");
            x.UpdatedAtUtc = DateTime.UtcNow;
            x.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        }

        if (input.Section == "Address")
        {
            var x = created ? null :
                await _db.Set<CariAddress>()
                    .FirstOrDefaultAsync(x =>
                        x.Id == input.Id &&
                        x.CariAccountId == input.CariAccountId);

            if (!created && x == null) return NotFound();
            if (!created && x!.ConcurrencyStamp != input.Stamp)
                return Conflict("Kay?t de?i?mi?. Sayfay? yenileyiniz.");

            if (created)
            {
                x = new CariAddress
                {
                    CariAccountId = input.CariAccountId
                };
                _db.Set<CariAddress>().Add(x);
            }

            // MC_CARI_ACTIVE_DEFAULT_GUARD_2026
            if (input.MakePrimary && !x!.IsActive)
                return Conflict("Pasif adres varsayilan secilemez.");

            x!.IsDefault = input.MakePrimary;
            x.AddressType = V("AddressType");
            x.Title = V("Title");
            x.AddressLine = V("AddressLine");
            x.City = V("City");
            x.District = V("District");
            x.PostalCode = V("PostalCode");
            x.Country = V("Country");
            x.ContactPerson = V("ContactPerson");
            x.Phone = V("Phone");
            x.Notes = V("Notes");
            x.UpdatedAtUtc = DateTime.UtcNow;
            x.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        }


        // MC_CARI_DEFAULTS_FIXED_2026
        if (input.MakePrimary)
        {
            if (input.Section == "Bank")
            {
                var others = await _db.Set<CariBankAccount>()
                    .Where(x =>
                        x.CariAccountId == input.CariAccountId &&
                        x.Id != input.Id &&
                        x.IsDefault)
                    .ToListAsync();

                foreach (var other in others)
                {
                    other.IsDefault = false;
                    other.UpdatedAtUtc = DateTime.UtcNow;
                    other.ConcurrencyStamp =
                        Guid.NewGuid().ToString("N");
                }
            }
            else if (input.Section == "Contact")
            {
                var others = await _db.Set<CariContact>()
                    .Where(x =>
                        x.CariAccountId == input.CariAccountId &&
                        x.Id != input.Id &&
                        x.IsPrimary)
                    .ToListAsync();

                foreach (var other in others)
                {
                    other.IsPrimary = false;
                    other.UpdatedAtUtc = DateTime.UtcNow;
                    other.ConcurrencyStamp =
                        Guid.NewGuid().ToString("N");
                }
            }
            else if (input.Section == "Address")
            {
                // Yerel V fonksiyonu EF sorgusunda kullanilmaz.
                var selectedAddressType = V("AddressType");

                var others = await _db.Set<CariAddress>()
                    .Where(x =>
                        x.CariAccountId == input.CariAccountId &&
                        x.Id != input.Id &&
                        x.AddressType == selectedAddressType &&
                        x.IsDefault)
                    .ToListAsync();

                foreach (var other in others)
                {
                    other.IsDefault = false;
                    other.UpdatedAtUtc = DateTime.UtcNow;
                    other.ConcurrencyStamp =
                        Guid.NewGuid().ToString("N");
                }
            }
        }

        _db.Set<CariAuditEvent>().Add(new CariAuditEvent
        {
            CariAccountId = input.CariAccountId,
            Action = created ? "Olu?turma" : "D?zenleme",
            Description = input.Section + " bilgisi kaydedildi.",
            ActorName = User.Identity?.Name
        });

        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index), new
        {
            cariId = input.CariAccountId,
            section = input.Section
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStatus(
        int cariId, string section, int id)
    {
        if (section == "Bank")
        {
            var x = await _db.Set<CariBankAccount>()
                .FirstOrDefaultAsync(x =>
                    x.Id == id && x.CariAccountId == cariId);

            if (x == null) return NotFound();
            x.IsActive = !x.IsActive;
            if (!x.IsActive) x.IsDefault = false;
            x.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        }
        else if (section == "Contact")
        {
            var x = await _db.Set<CariContact>()
                .FirstOrDefaultAsync(x =>
                    x.Id == id && x.CariAccountId == cariId);

            if (x == null) return NotFound();
            x.IsActive = !x.IsActive;
            if (!x.IsActive) x.IsPrimary = false;
            x.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        }
        else if (section == "Address")
        {
            var x = await _db.Set<CariAddress>()
                .FirstOrDefaultAsync(x =>
                    x.Id == id && x.CariAccountId == cariId);

            if (x == null) return NotFound();
            x.IsActive = !x.IsActive;
            if (!x.IsActive) x.IsDefault = false;
            x.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        }
        else return BadRequest();

        _db.Set<CariAuditEvent>().Add(new CariAuditEvent
        {
            CariAccountId = cariId,
            Action = "Durum de?i?ikli?i",
            Description = section + " kayd?n?n durumu de?i?tirildi.",
            ActorName = User.Identity?.Name
        });

        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index),
            new { cariId, section });
    }

    public async Task<IActionResult> History(int cariId)
    {
        var cari = await _db.Set<CariAccount>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == cariId);

        if (cari == null) return NotFound();

        ViewBag.Cari = cari;

        return View(await _db.Set<CariAuditEvent>()
            .AsNoTracking()
            .Where(x => x.CariAccountId == cariId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(500)
            .ToListAsync());
    }

    private static bool ValidIban(string iban)
    {
        if (!Regex.IsMatch(iban, @"^[A-Z]{2}[0-9]{2}[A-Z0-9]{11,30}$"))
            return false;

        if (iban.StartsWith("TR") && iban.Length != 26)
            return false;

        string moved = iban[4..] + iban[..4];
        int remainder = 0;

        foreach (char ch in moved)
        {
            string digits = char.IsDigit(ch)
                ? (ch - '0').ToString()
                : (ch - 'A' + 10).ToString();

            foreach (char digit in digits)
                remainder = (remainder * 10 + digit - '0') % 97;
        }

        return remainder == 1;
    }
}

public class CariRecordRow
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public bool IsActive { get; set; }
}

public class CariRecordInput
{
    public int Id { get; set; }
    public int CariAccountId { get; set; }
    public string Section { get; set; } = "Bank";
    public string? Stamp { get; set; }
    public bool MakePrimary { get; set; }
    public Dictionary<string, string?> Values { get; set; } = new();
}
