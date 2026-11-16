
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MasterChefTakip.Models;
using MasterChefTakip.Data;
using System.ComponentModel.DataAnnotations;

namespace MasterChefTakip.Controllers;

[Authorize(Roles = AppRoles.Admin)]
public class PurchasingController : Controller
{
    private readonly ApplicationDbContext _db;

    public PurchasingController(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var orders = await _db.Set<PurchaseOrder>()
            .AsNoTracking()
            .Include(x => x.Supplier)
            .OrderByDescending(x => x.Id)
            .Take(200)
            .ToListAsync();

        return View(orders);
    }

    public async Task<IActionResult> Create()
    {
        await LoadSuppliers();

        return View(new PurchaseOrderInput
        {
            OrderDate = DateTime.Today,
            Lines = Enumerable.Range(0, 5)
                .Select(x => new PurchaseLineInput())
                .ToList()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PurchaseOrderInput input)
    {
        input.Lines ??= new List<PurchaseLineInput>();

        var supplier = await _db.Set<CariAccount>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Id == input.SupplierId &&
                x.IsSupplier &&
                x.IsActive);

        if (supplier == null)
            ModelState.AddModelError(
                nameof(input.SupplierId),
                "Gecerli ve aktif bir tedarikci seciniz.");

        if (input.Currency is not ("TRY" or "USD" or "EUR" or "GBP"))
            ModelState.AddModelError(
                nameof(input.Currency),
                "Para birimi gecersiz.");

        if (input.OrderDate == default)
            ModelState.AddModelError(
                nameof(input.OrderDate),
                "Siparis tarihi zorunludur.");

        if (input.Lines.Count > 30)
            ModelState.AddModelError(
                "",
                "Bir sipariste en fazla 30 kalem olabilir.");

        var selectedLines = input.Lines
            .Where(x => !string.IsNullOrWhiteSpace(x.ProductDescription))
            .ToList();

        if (selectedLines.Count == 0)
            ModelState.AddModelError(
                "",
                "En az bir urun kalemi giriniz.");

        foreach (var line in selectedLines)
        {
            if (line.ProductDescription.Trim().Length > 250)
                ModelState.AddModelError("", "Urun adi cok uzun.");

            if (line.Quantity <= 0 || line.Quantity > 999999999)
                ModelState.AddModelError("", "Miktar gecersiz.");

            if (line.UnitPrice < 0 || line.UnitPrice > 999999999)
                ModelState.AddModelError("", "Birim fiyat gecersiz.");

            if (line.VatRate < 0 || line.VatRate > 100)
                ModelState.AddModelError("", "KDV orani gecersiz.");

            if (string.IsNullOrWhiteSpace(line.Unit) ||
                line.Unit.Length > 20)
                ModelState.AddModelError("", "Birim gecersiz.");
        }

        if (!ModelState.IsValid)
        {
            await LoadSuppliers();

            while (input.Lines.Count < 5)
                input.Lines.Add(new PurchaseLineInput());

            return View(input);
        }

        var order = new PurchaseOrder
        {
            OrderNumber = "SA-" +
                Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            SupplierId = input.SupplierId,
            OrderDate = input.OrderDate.Date,
            Currency = input.Currency,
            Notes = input.Notes?.Trim(),
            Status = "Draft",
            CreatedBy = User.Identity?.Name,
            CreatedAtUtc = DateTime.UtcNow
        };

        var lineNumber = 1;

        foreach (var line in selectedLines)
        {
            order.Lines.Add(new PurchaseOrderLine
            {
                LineNumber = lineNumber++,
                ProductDescription = line.ProductDescription.Trim(),
                Unit = line.Unit.Trim(),
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                VatRate = line.VatRate
            });
        }

        _db.Set<PurchaseOrder>().Add(order);

        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Details), new { id = order.Id });
    }

    public async Task<IActionResult> Details(int id)
    {
        var order = await _db.Set<PurchaseOrder>()
            .AsNoTracking()
            .Include(x => x.Supplier)
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (order == null) return NotFound();

        return View(order);
    }

    private async Task LoadSuppliers()
    {
        ViewBag.Suppliers = await _db.Set<CariAccount>()
            .AsNoTracking()
            .Where(x => x.IsSupplier && x.IsActive)
            .OrderBy(x => x.CompanyName)
            .Select(x => new SupplierOption
            {
                Id = x.Id,
                Name = x.CompanyName
            })
            .ToListAsync();
    }
}

public class SupplierOption
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class PurchaseOrderInput
{
    public int SupplierId { get; set; }

    [DataType(DataType.Date)]
    public DateTime OrderDate { get; set; } = DateTime.Today;

    [StringLength(3)]
    public string Currency { get; set; } = "TRY";

    [StringLength(1000)]
    public string? Notes { get; set; }

    public List<PurchaseLineInput> Lines { get; set; } = new();
}

public class PurchaseLineInput
{
    [StringLength(250)]
    public string ProductDescription { get; set; } = "";

    [StringLength(20)]
    public string Unit { get; set; } = "Adet";

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal VatRate { get; set; }
}
