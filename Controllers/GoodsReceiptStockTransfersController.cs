
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using MasterChefTakip.Models;
using MasterChefTakip.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

[Authorize(Roles = AppRoles.Admin)]
public class GoodsReceiptStockTransfersController : Controller
{
    private readonly ApplicationDbContext _db;

    public GoodsReceiptStockTransfersController(
        ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Transfer(int id)
    {
        var form = await BuildForm(id);

        if (form == null)
            return NotFound();

        return View(form);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Transfer(
        GoodsReceiptStockTransferForm form)
    {
        form.Lines ??= new();

        var userId = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        await using var transaction =
            await _db.Database.BeginTransactionAsync();

        var receipt = await _db.Set<PurchaseGoodsReceipt>()
            .AsNoTracking()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == form.ReceiptId);

        if (receipt == null)
            return NotFound();

        if (receipt.Status != "Recorded")
            return BadRequest("Mal kabul kaydi aktarima uygun degil.");

        var alreadyTransferred = await _db
            .Set<GoodsReceiptStockTransfer>()
            .AsNoTracking()
            .Where(x =>
                x.PurchaseGoodsReceiptLine
                    .PurchaseGoodsReceiptId == receipt.Id)
            .Select(x => x.PurchaseGoodsReceiptLineId)
            .ToListAsync();

        // MC_CROSS_STOCK_PROTECTION_2026
        // Hammadde stokuna aktarilan kalem mamul stokuna aktarilamaz.
        var rawTransferred = await _db
            .Set<RawMaterialReceiptTransfer>()
            .AsNoTracking()
            .Where(x =>
                x.PurchaseGoodsReceiptLine
                    .PurchaseGoodsReceiptId == receipt.Id)
            .Select(x => x.PurchaseGoodsReceiptLineId)
            .ToListAsync();

        var transferredIds = alreadyTransferred
            .Concat(rawTransferred)
            .ToHashSet();

        var eligible = receipt.Lines
            .Where(x =>
                x.AcceptedQuantity > 0 &&
                !transferredIds.Contains(x.Id) &&
                IsSupportedUnit(x.Unit) &&
                x.AcceptedQuantity <= int.MaxValue &&
                decimal.Truncate(x.AcceptedQuantity)
                    == x.AcceptedQuantity)
            .ToList();

        var eligibleIds = eligible
            .Select(x => x.Id)
            .ToHashSet();

        var submittedIds = form.Lines
            .Select(x => x.ReceiptLineId)
            .ToList();

        if (submittedIds.Count != eligibleIds.Count ||
            submittedIds.Distinct().Count() != submittedIds.Count ||
            !eligibleIds.SetEquals(submittedIds))
        {
            return BadRequest(
                "Mal kabul kalemleri degisti. Sayfayi yenileyiniz.");
        }

        if (eligible.Count == 0)
        {
            return BadRequest(
                "Aktarilabilecek adet birimli kalem bulunmuyor.");
        }

        var products = await _db.Set<Product>()
            .AsNoTracking()
            .Where(x => x.IsActive)
            .ToDictionaryAsync(x => x.Id);

        var pallets = await _db.Set<Pallet>()
            .AsNoTracking()
            .Where(x => x.IsActive && !x.IsCancelled)
            .ToDictionaryAsync(x => x.Id);

        foreach (var line in form.Lines)
        {
            if (!products.ContainsKey(line.ProductId))
                ModelState.AddModelError(
                    "", "Aktif bir urun seciniz.");

            if (!pallets.ContainsKey(line.PalletId))
                ModelState.AddModelError(
                    "", "Aktif bir palet seciniz.");
        }

        // Koli aktariminda urunun koli ici adedi zorunludur.
        // Toplam adet int sinirini asamaz.
        foreach (var selection in form.Lines)
        {
            if (!products.TryGetValue(
                selection.ProductId, out var selectedProduct))
                continue;

            if (!receipt.Lines.Any(
                x => x.Id == selection.ReceiptLineId))
                continue;

            var sourceLine = receipt.Lines.First(
                x => x.Id == selection.ReceiptLineId);

            if (!IsBoxUnit(sourceLine.Unit))
                continue;

            var capacity = selectedProduct.DefaultUnitsPerBox;

            if (!capacity.HasValue || capacity.Value <= 0)
            {
                ModelState.AddModelError(
                    "",
                    selectedProduct.Name +
                    ": Koli ici adet tanimlanmamis.");
                continue;
            }

            if (sourceLine.AcceptedQuantity >
                int.MaxValue / capacity.Value)
            {
                ModelState.AddModelError(
                    "",
                    selectedProduct.Name +
                    ": Koli miktari stok sinirini asiyor.");
            }
        }

        if (!ModelState.IsValid)
        {
            await transaction.RollbackAsync();

            var refreshed = await BuildForm(form.ReceiptId);

            if (refreshed == null)
                return NotFound();

            foreach (var row in refreshed.Lines)
            {
                var submitted = form.Lines.FirstOrDefault(
                    x => x.ReceiptLineId == row.ReceiptLineId);

                if (submitted != null)
                {
                    row.ProductId = submitted.ProductId;
                    row.PalletId = submitted.PalletId;
                }
            }

            return View(refreshed);
        }

        var receiptLines = eligible.ToDictionary(x => x.Id);

        var now = DateTime.UtcNow;

        foreach (var selection in form.Lines)
        {
            var source = receiptLines[selection.ReceiptLineId];

            var quantity = checked((int)source.AcceptedQuantity);

            var product = products[selection.ProductId];

            var pallet = pallets[selection.PalletId];

            var capacity = product.DefaultUnitsPerBox;

            if (capacity <= 0)
                capacity = null;

            var isBox = IsBoxUnit(source.Unit);

            var boxCount = isBox ? quantity : 0;

            var looseCount = isBox ? 0 : quantity;

            var totalUnits = isBox
                ? checked(quantity * capacity!.Value)
                : quantity;

            // Bu asamada sadece acilmamis koli degil,
            // tekil adet stok girisi olusturulur.
            var item = new PalletItem
            {
                PalletId = pallet.Id,
                ProductId = product.Id,
                BoxCount = boxCount,
                LooseUnitCount = looseCount,
                OpenBoxUnitCount = 0,
                OpenBoxCapacity = null,
                UnitsPerBox = capacity,
                ConcurrencyStamp =
                    Guid.NewGuid().ToString("N")
            };

            var movement = new StockMovement
            {
                PalletItem = item,
                Type = StockMovementType.Receipt,
                BoxCount = boxCount,
                LooseUnitCount = looseCount,
                UnitsPerBox = capacity,
                CreatedByUserId = userId,
                CreatedAtUtc = now,
                Notes = "Mal Kabul: " + receipt.ReceiptNumber
                    + " | " + quantity + " " + source.Unit
                    + " | Toplam adet: " + totalUnits
            };

            var transfer = new GoodsReceiptStockTransfer
            {
                PurchaseGoodsReceiptLineId = source.Id,
                ProductId = product.Id,
                PalletId = pallet.Id,
                PalletItem = item,
                StockMovement = movement,
                // Quantity alaninda toplam tekil adet tutulur.
                Quantity = totalUnits,
                CreatedByUserId = userId,
                CreatedAtUtc = now
            };

            _db.Set<GoodsReceiptStockTransfer>().Add(transfer);
        }

        try
        {
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();

            return Conflict(
                "Stok aktarimi tamamlanamadi. " +
                "Kalemlerden biri daha once aktarilmis olabilir. " +
                "Sayfayi yenileyiniz.");
        }

        TempData["GoodsStockSuccess"] =
            "Mal kabul kalemleri sto?a bir kez aktarildi.";

        return RedirectToAction(
            "Details",
            "PurchaseGoodsReceipt",
            new { id = receipt.Id });
    }

    // MC_STOCK_UNIT_CONVERSION_2026
    private static bool IsSupportedUnit(string? unit)
    {
        var value = (unit ?? "").Trim();

        return string.Equals(
                   value,
                   "Adet",
                   StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                   value,
                   "Koli",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBoxUnit(string? unit)
    {
        return string.Equals(
            (unit ?? "").Trim(),
            "Koli",
            StringComparison.OrdinalIgnoreCase);
    }

    private async Task<GoodsReceiptStockTransferForm?>
        BuildForm(int id)
    {
        var receipt = await _db.Set<PurchaseGoodsReceipt>()
            .AsNoTracking()
            .Include(x => x.PurchaseDispatchNote)
            .ThenInclude(x => x.Supplier)
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (receipt == null)
            return null;

        var transferredIds = await _db
            .Set<GoodsReceiptStockTransfer>()
            .AsNoTracking()
            .Where(x =>
                x.PurchaseGoodsReceiptLine
                    .PurchaseGoodsReceiptId == id)
            .Select(x => x.PurchaseGoodsReceiptLineId)
            .ToListAsync();

        var rawTransferredIds = await _db
            .Set<RawMaterialReceiptTransfer>()
            .AsNoTracking()
            .Where(x =>
                x.PurchaseGoodsReceiptLine
                    .PurchaseGoodsReceiptId == id)
            .Select(x => x.PurchaseGoodsReceiptLineId)
            .ToListAsync();

        var used = transferredIds
            .Concat(rawTransferredIds)
            .ToHashSet();

        var products = await _db.Set<Product>()
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new GoodsStockSelectionOption
            {
                Id = x.Id,
                Label = x.Code + " - " + x.Name
            })
            .ToListAsync();

        var pallets = await _db.Set<Pallet>()
            .AsNoTracking()
            .Where(x => x.IsActive && !x.IsCancelled)
            .OrderBy(x => x.Barcode)
            .Select(x => new GoodsStockSelectionOption
            {
                Id = x.Id,
                Label = x.Barcode
            })
            .ToListAsync();

        var form = new GoodsReceiptStockTransferForm
        {
            ReceiptId = receipt.Id,
            ReceiptNumber = receipt.ReceiptNumber,
            SupplierName =
                receipt.PurchaseDispatchNote.Supplier.CompanyName ?? "",
            CanTransfer = receipt.Status == "Recorded",
            Products = products,
            Pallets = pallets
        };

        foreach (var line in receipt.Lines
            .OrderBy(x => x.LineNumber))
        {
            var eligible =
                line.AcceptedQuantity > 0 &&
                !used.Contains(line.Id) &&
                IsSupportedUnit(line.Unit) &&
                line.AcceptedQuantity <= int.MaxValue &&
                decimal.Truncate(line.AcceptedQuantity)
                    == line.AcceptedQuantity;

            if (!eligible)
            {
                form.Excluded.Add(
                    line.ProductDescription + " - " +
                    (used.Contains(line.Id)
                        ? "Daha once aktarildi"
                        : "Adet veya Koli / tam sayi kontrolu gerekli"));

                continue;
            }

            form.Lines.Add(new GoodsStockTransferLineInput
            {
                ReceiptLineId = line.Id,
                Description = line.ProductDescription,
                Unit = line.Unit,
                Quantity = (int)line.AcceptedQuantity
            });
        }

        return form;
    }
}

public class GoodsStockSelectionOption
{
    public int Id { get; set; }
    public string Label { get; set; } = "";
}

public class GoodsReceiptStockTransferForm
{
    public int ReceiptId { get; set; }
    public string ReceiptNumber { get; set; } = "";
    public string SupplierName { get; set; } = "";
    public bool CanTransfer { get; set; }

    public List<GoodsStockTransferLineInput> Lines
        { get; set; } = new();

    public List<GoodsStockSelectionOption> Products
        { get; set; } = new();

    public List<GoodsStockSelectionOption> Pallets
        { get; set; } = new();

    public List<string> Excluded { get; set; } = new();
}

public class GoodsStockTransferLineInput
{
    public int ReceiptLineId { get; set; }

    public int ProductId { get; set; }

    public int PalletId { get; set; }

    public string Description { get; set; } = "";
    public string Unit { get; set; } = "";

    public int Quantity { get; set; }
}
