


using System.Security.Claims;


using MasterChefTakip.Models;


using MasterChefTakip.Data;


using Microsoft.AspNetCore.Authorization;


using Microsoft.AspNetCore.Mvc;


using Microsoft.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore.Storage;





namespace MasterChefTakip.Controllers;





[Authorize(Roles = AppRoles.Admin)]


public class RawMaterialReceiptsController : Controller


{


    private readonly ApplicationDbContext _db;





    public RawMaterialReceiptsController(ApplicationDbContext db)


    {


        _db = db;


    }





    [HttpGet]


    public async Task<IActionResult> Transfer(int id)


    {


        var model = await BuildPage(id);





        if (model == null)


            return NotFound();





        return View(model);


    }





    [HttpPost]


    [ValidateAntiForgeryToken]


    [MCStockTransferConfirmation]
    public async Task<IActionResult> Transfer(


        int receiptLineId,


        int rawMaterialId,


        int warehouseId)


    {


        var userId = User.FindFirstValue(


            ClaimTypes.NameIdentifier);





        if (string.IsNullOrWhiteSpace(userId))


            return Unauthorized();





        if (receiptLineId <= 0 ||


            rawMaterialId <= 0 ||


            warehouseId <= 0)


        {


            return BadRequest("Gecersiz aktarim secimi.");


        }





        await using var transaction =


            await _db.Database.BeginTransactionAsync();





        var line = await _db


            .Set<PurchaseGoodsReceiptLine>()


            .AsNoTracking()


            .Include(x => x.PurchaseGoodsReceipt)


            .FirstOrDefaultAsync(x => x.Id == receiptLineId);





        if (line == null)


            return NotFound();





        var receipt = line.PurchaseGoodsReceipt;





        if (receipt.Status != "Recorded")


            return BadRequest("Mal kabul durumu uygun degil.");





        if (line.AcceptedQuantity <= 0)


            return BadRequest("Kabul edilen miktar bulunmuyor.");





        if (decimal.Round(line.AcceptedQuantity, 4)


            != line.AcceptedQuantity)


        {


            return BadRequest("Miktar hassasiyeti gecersiz.");


        }





        var unit = NormalizeUnit(line.Unit);





        if (unit == null)


            return BadRequest(


                "Yalnizca Kg ve L birimleri desteklenir.");





        var usedByRaw = await _db


            .Set<RawMaterialReceiptTransfer>()


            .AnyAsync(x => x.PurchaseGoodsReceiptLineId == line.Id);





        var usedByPallet = await _db


            .Set<GoodsReceiptStockTransfer>()


            .AnyAsync(x => x.PurchaseGoodsReceiptLineId == line.Id);





        if (usedByRaw || usedByPallet)


        {


            return Conflict(


                "Bu mal kabul kalemi daha once sto?a aktarilmis.");


        }





        var material = await _db.Set<RawMaterial>()


            .AsNoTracking()


            .FirstOrDefaultAsync(x =>


                x.Id == rawMaterialId && x.IsActive);





        var warehouse = await _db.Set<RawMaterialWarehouse>()


            .AsNoTracking()


            .FirstOrDefaultAsync(x =>


                x.Id == warehouseId && x.IsActive);





        if (material == null || warehouse == null)


        {


            return BadRequest(


                "Aktif hammadde ve depo seciniz.");


        }





        if (NormalizeUnit(material.Unit) != unit)


        {


            return BadRequest(


                "Mal kabul birimi ile hammadde birimi ayni olmali.");


        }







        // MC_RECEIPT_CATALOG_STOCK_GUARD_V2

        // Dogrulanmamis mal kabul satiri stok artiramaz.

        await using (var check =

            _db.Database.GetDbConnection().CreateCommand())

        {

            check.Transaction =

                _db.Database.CurrentTransaction!.GetDbTransaction();



            check.CommandText = @"

                SELECT COUNT(*)

                FROM MCMaterialReceiptLinks rl

                INNER JOIN MCMaterialStockCardLinks sl

                    ON sl.MaterialCatalogId = rl.MaterialCatalogId

                INNER JOIN MCMaterialCatalog c

                    ON c.Id = rl.MaterialCatalogId

                WHERE rl.PurchaseGoodsReceiptLineId = @line

                  AND sl.RawMaterialId = @raw

                  AND UPPER(TRIM(c.MaterialType)) = 'HM'";



            var lineParameter = check.CreateParameter();

            lineParameter.ParameterName = "@line";

            lineParameter.Value = line.Id;

            check.Parameters.Add(lineParameter);



            var rawParameter = check.CreateParameter();

            rawParameter.ParameterName = "@raw";

            rawParameter.Value = material.Id;

            check.Parameters.Add(rawParameter);



            var count = Convert.ToInt64(

                await check.ExecuteScalarAsync() ?? 0L);



            if (count != 1)

            {

                return Conflict(

                    "Mal kabul katalog kaydi ile stok karti "

                    + "eslesmiyor. Stok girisi engellendi.");

            }

        }



        var movement = new RawMaterialStockMovement


        {


            RawMaterialId = material.Id,


            WarehouseId = warehouse.Id,


            MovementType = "IN",


            Quantity = line.AcceptedQuantity,


            Unit = material.Unit,


            OperationNumber = "HM-MK-" +


                Guid.NewGuid().ToString("N").ToUpperInvariant(),


            ReferenceNumber = receipt.ReceiptNumber,


            Notes = "Mal kabulden hammadde stok girisi",


            CreatedByUserId = userId,


            CreatedAtUtc = DateTime.UtcNow


        };





        var transfer = new RawMaterialReceiptTransfer


        {


            PurchaseGoodsReceiptLineId = line.Id,


            RawMaterialId = material.Id,


            WarehouseId = warehouse.Id,


            RawMaterialStockMovement = movement,


            Quantity = line.AcceptedQuantity,


            Unit = material.Unit,


            CreatedByUserId = userId,


            CreatedAtUtc = DateTime.UtcNow


        };





        _db.Set<RawMaterialReceiptTransfer>().Add(transfer);





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


                "Ayni kalem baska bir islemde aktarilmis olabilir.");


        }





        TempData["RawReceiptSuccess"] =


            "Hammadde stoguna basariyla aktarildi.";





        return RedirectToAction(


            nameof(Transfer),


            new { id = receipt.Id });


    }





    private static string? NormalizeUnit(string? source)


    {


        var value = (source ?? "").Trim().ToUpperInvariant();





        return value switch


        {


            "KG" => "Kg",


            "KILOGRAM" => "Kg",


            "K?LOGRAM" => "Kg",


            "L" => "L",


            "LT" => "L",


            "LITRE" => "L",


            "L?TRE" => "L",


            _ => null


        };


    }





    private async Task<RawReceiptPage?> BuildPage(int id)


    {


        var receipt = await _db.Set<PurchaseGoodsReceipt>()


            .AsNoTracking()


            .Include(x => x.PurchaseDispatchNote)


            .ThenInclude(x => x.Supplier)


            .Include(x => x.Lines)


            .FirstOrDefaultAsync(x => x.Id == id);





        if (receipt == null)


            return null;





        var rawUsed = await _db


            .Set<RawMaterialReceiptTransfer>()


            .AsNoTracking()


            .Where(x =>


                x.PurchaseGoodsReceiptLine


                    .PurchaseGoodsReceiptId == id)


            .Select(x => x.PurchaseGoodsReceiptLineId)


            .ToListAsync();





        var palletUsed = await _db


            .Set<GoodsReceiptStockTransfer>()


            .AsNoTracking()


            .Where(x =>


                x.PurchaseGoodsReceiptLine

                    .PurchaseGoodsReceiptId == id)


            .Select(x => x.PurchaseGoodsReceiptLineId)


            .ToListAsync();





        var used = rawUsed.Concat(palletUsed).ToHashSet();





        var model = new RawReceiptPage


        {


            ReceiptId = receipt.Id,


            ReceiptNumber = receipt.ReceiptNumber,


            SupplierName = receipt.PurchaseDispatchNote


                .Supplier.CompanyName ?? "",


            CanTransfer = receipt.Status == "Recorded",





            Materials = await _db.Set<RawMaterial>()


                .AsNoTracking()


                .Where(x => x.IsActive)


                .OrderBy(x => x.Name)


                .Select(x => new RawReceiptOption


                {


                    Id = x.Id,


                    Name = x.Code + " - " + x.Name,


                    Unit = x.Unit


                })


                .ToListAsync(),







            Warehouses = await _db.Set<RawMaterialWarehouse>()


                .AsNoTracking()


                .Where(x => x.IsActive)



                .OrderBy(x => x.Name)


                .Select(x => new RawReceiptOption


                {


                    Id = x.Id,


                    Name = x.Code + " - " + x.Name


                })


                .ToListAsync()


        };





        foreach (var line in receipt.Lines.OrderBy(x => x.LineNumber))


        {


            model.Lines.Add(new RawReceiptLineRow


            {


                Id = line.Id,


                Description = line.ProductDescription,


                Unit = line.Unit,


                AcceptedQuantity = line.AcceptedQuantity,


                Transferred = used.Contains(line.Id),


                SupportedUnit = NormalizeUnit(line.Unit) != null


            });


        }





        return model;


    }


}





public class RawReceiptOption


{


    public int Id { get; set; }


    public string Name { get; set; } = "";


    public string Unit { get; set; } = "";


}





public class RawReceiptLineRow


{


    public int Id { get; set; }


    public string Description { get; set; } = "";


    public string Unit { get; set; } = "";


    public decimal AcceptedQuantity { get; set; }


    public bool Transferred { get; set; }


    public bool SupportedUnit { get; set; }


}





public class RawReceiptPage


{


    public int ReceiptId { get; set; }


    public string ReceiptNumber { get; set; } = "";


    public string SupplierName { get; set; } = "";


    public bool CanTransfer { get; set; }





    public List<RawReceiptLineRow> Lines { get; set; } = new();


    public List<RawReceiptOption> Materials { get; set; } = new();


    public List<RawReceiptOption> Warehouses { get; set; } = new();


}


