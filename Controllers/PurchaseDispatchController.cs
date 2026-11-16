
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MasterChefTakip.Models;
using MasterChefTakip.Data;
using System.ComponentModel.DataAnnotations;

namespace MasterChefTakip.Controllers;

[Authorize(Roles = AppRoles.Admin)]
public class PurchaseDispatchController : Controller
{
    private readonly ApplicationDbContext _db;

    public PurchaseDispatchController(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var records = await _db.Set<PurchaseDispatchNote>()
            .AsNoTracking()
            .Include(x => x.Supplier)
            .Include(x => x.PurchaseOrder)
            .OrderByDescending(x => x.Id)
            .Take(200)
            .ToListAsync();

        return View(records);
    }

    
    // MC_PARTIAL_DISPATCH_2026

    public async Task<IActionResult> Create(int? purchaseOrderId)
    {
        await LoadOrders();

        var input = new PurchaseDispatchInput
        {
            DocumentDate = DateTime.Today
        };

        if (!purchaseOrderId.HasValue)
            return View(input);

        var order = await _db.Set<PurchaseOrder>()
            .AsNoTracking()
            .Include(x => x.Supplier)
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == purchaseOrderId.Value);

        if (order == null)
            return NotFound();

        if (!order.Supplier.IsActive)
            return BadRequest("Tedarikci aktif degil.");

        var previous = await _db.Set<PurchaseDispatchLine>()
            .AsNoTracking()
            .Where(x =>
                x.PurchaseDispatchNote.PurchaseOrderId == order.Id)
            .Select(x => new
            {
                x.PurchaseOrderLineId,
                x.Quantity
            })
            .ToListAsync();

        input.PurchaseOrderId = order.Id;

        foreach (var line in order.Lines.OrderBy(x => x.LineNumber))
        {
            var used = previous
                .Where(x => x.PurchaseOrderLineId == line.Id)
                .Sum(x => x.Quantity);

            var remaining = line.Quantity - used;

            input.Lines.Add(new PurchaseDispatchLineInput
            {
                PurchaseOrderLineId = line.Id,
                ProductDescription = line.ProductDescription,
                Unit = line.Unit,
                OrderedQuantity = line.Quantity,
                RemainingQuantity = remaining > 0 ? remaining : 0,
                Quantity = remaining > 0 ? remaining : 0
            });
        }

        return View(input);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PurchaseDispatchInput input)
    {
        input.Lines ??= new List<PurchaseDispatchLineInput>();

        var order = await _db.Set<PurchaseOrder>()
            .AsNoTracking()
            .Include(x => x.Supplier)
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x =>
                x.Id == input.PurchaseOrderId);

        if (order == null)
            return BadRequest("Satin alma siparisi bulunamadi.");

        if (!order.Supplier.IsActive)
            return BadRequest("Tedarikci aktif degil.");

        if (input.DocumentDate == default)
            ModelState.AddModelError(
                "", "Irsaliye tarihi zorunludur.");

        if (input.Lines.Count == 0 || input.Lines.Count > 100)
            ModelState.AddModelError(
                "", "Irsaliye satir sayisi gecersiz.");

        var orderLines = order.Lines.ToDictionary(x => x.Id);

        var duplicateIds = input.Lines
            .GroupBy(x => x.PurchaseOrderLineId)
            .Any(x => x.Count() > 1);

        if (duplicateIds)
            ModelState.AddModelError(
                "", "Ayni siparis kalemi birden fazla gonderildi.");

        if (input.Lines.Any(x =>
            !orderLines.ContainsKey(x.PurchaseOrderLineId)))
        {
            ModelState.AddModelError(
                "", "Siparise ait olmayan urun kalemi bulundu.");
        }

        var previous = await _db.Set<PurchaseDispatchLine>()
            .AsNoTracking()
            .Where(x =>
                x.PurchaseDispatchNote.PurchaseOrderId == order.Id)
            .Select(x => new
            {
                x.PurchaseOrderLineId,
                x.Quantity
            })
            .ToListAsync();

        var remainingById = new Dictionary<int, decimal>();

        foreach (var line in order.Lines)
        {
            var used = previous
                .Where(x => x.PurchaseOrderLineId == line.Id)
                .Sum(x => x.Quantity);

            remainingById[line.Id] = line.Quantity - used;
        }

        foreach (var line in input.Lines)
        {
            if (line.Quantity < 0)
                ModelState.AddModelError(
                    "", "Miktar negatif olamaz.");

            if (line.Quantity > 0 &&
                remainingById.TryGetValue(
                    line.PurchaseOrderLineId, out var remaining) &&
                line.Quantity > remaining)
            {
                ModelState.AddModelError(
                    "",
                    "Irsaliye miktari kalan siparis miktarini asamaz.");
            }
        }

        if (!input.Lines.Any(x => x.Quantity > 0))
            ModelState.AddModelError(
                "", "En az bir urun icin miktar giriniz.");

        if (!ModelState.IsValid)
        {
            foreach (var line in input.Lines)
            {
                if (orderLines.TryGetValue(
                    line.PurchaseOrderLineId, out var original))
                {
                    line.ProductDescription = original.ProductDescription;
                    line.Unit = original.Unit;
                    line.OrderedQuantity = original.Quantity;

                    line.RemainingQuantity =
                        remainingById.GetValueOrDefault(original.Id);
                }
            }

            await LoadOrders();

            return View(input);
        }


        // MC_DUPLICATE_SUPPLIER_DISPATCH_2026
        var supplierDocumentNumber =
            input.SupplierDocumentNumber?.Trim();

        if (string.IsNullOrWhiteSpace(supplierDocumentNumber))
        {
            ModelState.AddModelError(
                nameof(input.SupplierDocumentNumber),
                "Tedarikci irsaliye numarasi zorunludur.");

            await LoadOrders();
            return View(input);
        }

        var duplicateDispatch = await _db
            .Set<PurchaseDispatchNote>()
            .AsNoTracking()
            .AnyAsync(x =>
                x.SupplierId == order.SupplierId &&
                x.SupplierDocumentNumber != null &&
                x.SupplierDocumentNumber.ToUpper() ==
                    supplierDocumentNumber.ToUpper());

        if (duplicateDispatch)
        {
            ModelState.AddModelError(
                nameof(input.SupplierDocumentNumber),
                "Bu tedarikciye ait ayni irsaliye numarasi " +
                "daha once kaydedilmis.");

            await LoadOrders();
            return View(input);
        }

        var note = new PurchaseDispatchNote
        {
            DocumentNumber = "AI-" +
                Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            SupplierDocumentNumber =
                input.SupplierDocumentNumber?.Trim(),
            PurchaseOrderId = order.Id,
            SupplierId = order.SupplierId,
            DocumentDate = input.DocumentDate.Date,
            Status = "Draft",
            Notes = input.Notes?.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = User.Identity?.Name
        };

        var number = 1;

        foreach (var line in input.Lines.Where(x => x.Quantity > 0))
        {
            var original = orderLines[line.PurchaseOrderLineId];

            note.Lines.Add(new PurchaseDispatchLine
            {
                PurchaseOrderLineId = original.Id,
                LineNumber = number++,
                ProductDescription = original.ProductDescription,
                Unit = original.Unit,
                Quantity = line.Quantity
            });
        }

        _db.Set<PurchaseDispatchNote>().Add(note);

        await _db.SaveChangesAsync();

        return RedirectToAction(
            nameof(Details), new { id = note.Id });
    }


    // MC_DIRECT_PURCHASE_DISPATCH_2026
    [HttpGet]
    public async Task<IActionResult> Direct()
    {
        await LoadDirectSuppliers();

        var input = new DirectPurchaseDispatchInput
        {
            DocumentDate = DateTime.Today
        };

        for (var i = 0; i < 5; i++)
            input.Lines.Add(new DirectPurchaseDispatchLineInput());

        return View(input);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Direct(
        DirectPurchaseDispatchInput input)
    {
        input.Lines ??= new();

        var supplier = await _db.Set<CariAccount>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Id == input.SupplierId &&
                x.IsSupplier &&
                x.IsActive);

        if (supplier == null)
            ModelState.AddModelError(
                nameof(input.SupplierId),
                "Aktif tedarikci seciniz.");

        var documentNumber =
            input.SupplierDocumentNumber?.Trim();

        if (string.IsNullOrWhiteSpace(documentNumber))
            ModelState.AddModelError(
                nameof(input.SupplierDocumentNumber),
                "Tedarikci irsaliye numarasi zorunludur.");

        if (input.DocumentDate == default)
            ModelState.AddModelError(
                nameof(input.DocumentDate),
                "Irsaliye tarihi zorunludur.");

        if (input.Lines.Count > 100)
            ModelState.AddModelError(
                "",
                "En fazla 100 irsaliye kalemi eklenebilir.");

        var lines = input.Lines
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.ProductDescription) ||
                x.Quantity != 0)
            .ToList();

        if (lines.Count == 0)
            ModelState.AddModelError(
                "",
                "En az bir urun veya hammadde giriniz.");

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line.ProductDescription) ||
                line.ProductDescription.Trim().Length > 250)
            {
                ModelState.AddModelError(
                    "",
                    "Urun veya hammadde adi 1-250 karakter olmalidir.");
            }

            if (line.Quantity <= 0 ||
                line.Quantity > 999999999 ||
                decimal.Round(line.Quantity, 4) != line.Quantity)
            {
                ModelState.AddModelError(
                    "",
                    "Miktar pozitif ve en fazla 4 ondalikli olmalidir.");
            }

            if (string.IsNullOrWhiteSpace(line.Unit) ||
                line.Unit.Trim().Length > 20)
            {
                ModelState.AddModelError(
                    "",
                    "Gecerli bir olcu birimi seciniz.");
            }
        }

        if (!ModelState.IsValid)
        {
            await LoadDirectSuppliers();
            return View(input);
        }

        await using var transaction =
            await _db.Database.BeginTransactionAsync();

        var duplicate = await _db.Set<PurchaseDispatchNote>()
            .AsNoTracking()
            .AnyAsync(x =>
                x.SupplierId == input.SupplierId &&
                x.SupplierDocumentNumber != null &&
                x.SupplierDocumentNumber.Trim().ToUpper() ==
                    documentNumber!.ToUpper());

        if (duplicate)
        {
            await transaction.RollbackAsync();
            ModelState.AddModelError(
                nameof(input.SupplierDocumentNumber),
                "Bu tedarikcinin irsaliye numarasi zaten kayitli.");

            await LoadDirectSuppliers();
            return View(input);
        }

        var note = new PurchaseDispatchNote
        {
            DocumentNumber = "AI-" +
                Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            SupplierDocumentNumber = documentNumber,
            PurchaseOrderId = null,
            SupplierId = supplier!.Id,
            DocumentDate = input.DocumentDate.Date,
            Status = "Draft",
            Notes = input.Notes?.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = User.Identity?.Name
        };

        var number = 1;

        foreach (var line in lines)
        {
            note.Lines.Add(new PurchaseDispatchLine
            {
                PurchaseOrderLineId = null,
                LineNumber = number++,
                ProductDescription =
                    line.ProductDescription.Trim(),
                Unit = line.Unit.Trim(),
                Quantity = line.Quantity
            });
        }

        _db.Set<PurchaseDispatchNote>().Add(note);

        try
        {
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            ModelState.AddModelError(
                "",
                "Kayit tamamlanamadi. Irsaliye numarasi " +
                "daha once kaydedilmis olabilir.");

            await LoadDirectSuppliers();
            return View(input);
        }

        return RedirectToAction(
            nameof(Details), new { id = note.Id });
    }

    private async Task LoadDirectSuppliers()
    {
        ViewBag.DirectSuppliers = await _db.Set<CariAccount>()
            .AsNoTracking()
            .Where(x => x.IsSupplier && x.IsActive)
            .OrderBy(x => x.CompanyName)
            .ToListAsync();
    }

    private async Task LoadOrders()
    {
        ViewBag.Orders = await _db.Set<PurchaseOrder>()
            .AsNoTracking()
            .Include(x => x.Supplier)
            .Where(x => x.Supplier.IsActive)
            .OrderByDescending(x => x.Id)
            .Take(200)
            .ToListAsync();
    }

public async Task<IActionResult> Details(int id)
    {
        var note = await _db.Set<PurchaseDispatchNote>()
            .AsNoTracking()
            .Include(x => x.Supplier)
            .Include(x => x.PurchaseOrder)
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (note == null)
            return NotFound();

        return View(note);
    }
}

public class PurchaseDispatchInput
{
    public List<PurchaseDispatchLineInput> Lines { get; set; } = new();

    [Range(1, int.MaxValue)]
    public int PurchaseOrderId { get; set; }

    [StringLength(60)]
    public string? SupplierDocumentNumber { get; set; }

    [DataType(DataType.Date)]
    public DateTime DocumentDate { get; set; } = DateTime.Today;

    [StringLength(1000)]
    public string? Notes { get; set; }
}


public class PurchaseDispatchLineInput
{
    public int PurchaseOrderLineId { get; set; }

    public string ProductDescription { get; set; } = "";

    public string Unit { get; set; } = "";

    public decimal OrderedQuantity { get; set; }

    public decimal RemainingQuantity { get; set; }

    [Range(typeof(decimal), "0", "999999999", ParseLimitsInInvariantCulture = true)]
    public decimal Quantity { get; set; }
}


// MC_DIRECT_PURCHASE_DISPATCH_MODELS_2026
public class DirectPurchaseDispatchInput
{
    [Range(1, int.MaxValue)]
    public int SupplierId { get; set; }

    [Required, StringLength(60)]
    public string? SupplierDocumentNumber { get; set; }

    [DataType(DataType.Date)]
    public DateTime DocumentDate { get; set; } = DateTime.Today;

    [StringLength(1000)]
    public string? Notes { get; set; }

    public List<DirectPurchaseDispatchLineInput> Lines { get; set; }
        = new();
}

public class DirectPurchaseDispatchLineInput
{
    public string ProductDescription { get; set; } = "";

    public string Unit { get; set; } = "Kg";

    public decimal Quantity { get; set; }
}
