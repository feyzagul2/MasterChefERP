
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MasterChefTakip.Models;
using MasterChefTakip.Data;
using System.ComponentModel.DataAnnotations;

namespace MasterChefTakip.Controllers;

[Authorize(Roles = AppRoles.Admin)]
public class PurchaseGoodsReceiptController : Controller
{
    private readonly ApplicationDbContext _db;

    public PurchaseGoodsReceiptController(ApplicationDbContext db)
    {
        _db = db;
    }

    
    // MC_GOODS_STOCK_PREPARATION_2026
    [HttpGet]
    public async Task<IActionResult> StockPreparation(int id)
    {
        var receipt = await _db.Set<PurchaseGoodsReceipt>()
            .AsNoTracking()
            .Include(x => x.PurchaseDispatchNote)
            .ThenInclude(x => x.Supplier)
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (receipt == null)
            return NotFound();

        var products = await _db.Set<Product>()
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new GoodsStockProductOption
            {
                Id = x.Id,
                Name = x.Name,
                Code = x.Code
            })
            .ToListAsync();

        var rows = new List<GoodsStockPreparationRow>();

        foreach (var line in receipt.Lines.OrderBy(x => x.LineNumber))
        {
            var name = (line.ProductDescription ?? "").Trim();

            var matches = products
                .Where(x =>
                    string.Equals(
                        x.Name.Trim(),
                        name,
                        StringComparison.OrdinalIgnoreCase)
                    ||
                    string.Equals(
                        x.Code.Trim(),
                        name,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

            rows.Add(new GoodsStockPreparationRow
            {
                LineId = line.Id,
                Description = line.ProductDescription ?? "",
                Unit = line.Unit ?? "",
                AcceptedQuantity = line.AcceptedQuantity,
                RejectedQuantity = line.RejectedQuantity,
                MatchedProductId =
                    matches.Count == 1 ? matches[0].Id : null,
                MatchedProductName =
                    matches.Count == 1 ? matches[0].Name : null,
                MatchCount = matches.Count
            });
        }

        ViewBag.Receipt = receipt;

        return View(rows);
    }

    public async Task<IActionResult> Index()
    {
        var data = await _db.Set<PurchaseGoodsReceipt>()
            .AsNoTracking()
            .Include(x => x.PurchaseDispatchNote)
            .ThenInclude(x => x.Supplier)
            .OrderByDescending(x => x.Id)
            .Take(200)
            .ToListAsync();

        return View(data);
    }

    public async Task<IActionResult> Create(int dispatchId)
    {
        var note = await _db.Set<PurchaseDispatchNote>()
            .AsNoTracking()
            .Include(x => x.Supplier)
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == dispatchId);

        if (note == null) return NotFound();

        if (note.Status == "Cancelled")
            return BadRequest("Iptal edilmis irsaliye.");

        var remaining = await RemainingLines(note);

        if (!remaining.Any(x => x.RemainingQuantity > 0))
            return BadRequest("Irsaliye miktarlari zaten islenmis.");

        ViewBag.Note = note;

        return View(new GoodsReceiptInput
        {
            PurchaseDispatchNoteId = note.Id,
            ReceiptDate = DateTime.Today,
            Lines = remaining
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(GoodsReceiptInput input)
    {
        input.Lines ??= new();

        await using var transaction =
            await _db.Database.BeginTransactionAsync();

        var note = await _db.Set<PurchaseDispatchNote>()
            .AsNoTracking()
            .Include(x => x.Supplier)
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x =>
                x.Id == input.PurchaseDispatchNoteId);

        if (note == null) return NotFound();

        if (note.Status == "Cancelled")
            return BadRequest("Iptal edilmis irsaliye.");

        var remaining = await RemainingLines(note);

        var available = remaining.ToDictionary(
            x => x.PurchaseDispatchLineId);

        if (input.ReceiptDate == default)
            ModelState.AddModelError("", "Mal kabul tarihi zorunludur.");

        if (input.Lines.Count != available.Count)
            ModelState.AddModelError("", "Kalem sayisi gecersiz.");

        if (input.Lines
            .GroupBy(x => x.PurchaseDispatchLineId)
            .Any(x => x.Count() > 1))
        {
            ModelState.AddModelError(
                "", "Tekrarlanan irsaliye kalemi.");
        }

        foreach (var line in input.Lines)
        {
            if (!available.TryGetValue(
                line.PurchaseDispatchLineId, out var item))
            {
                ModelState.AddModelError(
                    "", "Irsaliye disinda kalem var.");
                continue;
            }

            if (line.ReceivedQuantity < 0 ||
                line.AcceptedQuantity < 0 ||
                line.RejectedQuantity < 0)
            {
                ModelState.AddModelError(
                    "", "Negatif miktar girilemez.");
            }

            if (line.ReceivedQuantity > item.RemainingQuantity)
            {
                ModelState.AddModelError(
                    "", "Gelen miktar kalan miktari asamaz.");
            }

            if (line.AcceptedQuantity + line.RejectedQuantity
                != line.ReceivedQuantity)
            {
                ModelState.AddModelError(
                    "", "Kabul + Ret = Gelen olmalidir.");
            }

            if (new[]
            {
                line.ReceivedQuantity,
                line.AcceptedQuantity,
                line.RejectedQuantity
            }.Any(x => decimal.Round(x, 4) != x))
            {
                ModelState.AddModelError(
                    "", "En fazla 4 ondalik hane girilebilir.");
            }
        }

        if (!input.Lines.Any(x => x.ReceivedQuantity > 0))
            ModelState.AddModelError(
                "", "En az bir urun miktari giriniz.");

        if (!ModelState.IsValid)
        {
            await transaction.RollbackAsync();

            foreach (var line in input.Lines)
            {
                if (available.TryGetValue(
                    line.PurchaseDispatchLineId, out var item))
                {
                    line.ProductDescription = item.ProductDescription;
                    line.Unit = item.Unit;
                    line.RemainingQuantity = item.RemainingQuantity;
                }
            }

            ViewBag.Note = note;

            return View(input);
        }

        var receipt = new PurchaseGoodsReceipt
        {
            ReceiptNumber = "MK-" +
                Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            PurchaseDispatchNoteId = note.Id,
            ReceiptDate = input.ReceiptDate.Date,
            Status = "Recorded",
            Notes = input.Notes?.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = User.Identity?.Name
        };

        var number = 1;

        foreach (var line in input.Lines.Where(
            x => x.ReceivedQuantity > 0))
        {
            var item = available[line.PurchaseDispatchLineId];

            receipt.Lines.Add(new PurchaseGoodsReceiptLine
            {
                PurchaseDispatchLineId = item.PurchaseDispatchLineId,
                LineNumber = number++,
                ProductDescription = item.ProductDescription,
                Unit = item.Unit,
                ReceivedQuantity = line.ReceivedQuantity,
                AcceptedQuantity = line.AcceptedQuantity,
                RejectedQuantity = line.RejectedQuantity
            });
        }

        _db.Set<PurchaseGoodsReceipt>().Add(receipt);

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return RedirectToAction(nameof(Details), new { id = receipt.Id });
    }

    public async Task<IActionResult> Details(int id)
    {
        var item = await _db.Set<PurchaseGoodsReceipt>()
            .AsNoTracking()
            .Include(x => x.PurchaseDispatchNote)
            .ThenInclude(x => x.Supplier)
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (item == null) return NotFound();

        return View(item);
    }

    private async Task<List<GoodsReceiptLineInput>> RemainingLines(
        PurchaseDispatchNote note)
    {
        var previous = await _db.Set<PurchaseGoodsReceiptLine>()
            .AsNoTracking()
            .Where(x =>
                x.PurchaseGoodsReceipt.PurchaseDispatchNoteId == note.Id)
            .Select(x => new
            {
                x.PurchaseDispatchLineId,
                x.ReceivedQuantity
            })
            .ToListAsync();

        return note.Lines
            .OrderBy(x => x.LineNumber)
            .Select(line =>
            {
                var used = previous
                    .Where(x => x.PurchaseDispatchLineId == line.Id)
                    .Sum(x => x.ReceivedQuantity);

                var left = Math.Max(0m, line.Quantity - used);

                return new GoodsReceiptLineInput
                {
                    PurchaseDispatchLineId = line.Id,
                    ProductDescription = line.ProductDescription,
                    Unit = line.Unit,
                    RemainingQuantity = left,
                    ReceivedQuantity = left,
                    AcceptedQuantity = left,
                    RejectedQuantity = 0
                };
            })
            .ToList();
    }
}

public class GoodsReceiptInput
{
    public int PurchaseDispatchNoteId { get; set; }

    [DataType(DataType.Date)]
    public DateTime ReceiptDate { get; set; } = DateTime.Today;

    [StringLength(1000)]
    public string? Notes { get; set; }

    public List<GoodsReceiptLineInput> Lines { get; set; } = new();
}

public class GoodsReceiptLineInput
{
    public int PurchaseDispatchLineId { get; set; }
    public string ProductDescription { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal RemainingQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal AcceptedQuantity { get; set; }
    public decimal RejectedQuantity { get; set; }
}


public class GoodsStockProductOption
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public string Code { get; set; } = "";
}

public class GoodsStockPreparationRow
{
    public int LineId { get; set; }

    public string Description { get; set; } = "";

    public string Unit { get; set; } = "";

    public decimal AcceptedQuantity { get; set; }

    public decimal RejectedQuantity { get; set; }

    public int? MatchedProductId { get; set; }

    public string? MatchedProductName { get; set; }

    public int MatchCount { get; set; }
}
