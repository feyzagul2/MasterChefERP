using System.Security.Claims;
using MasterChefTakip.Data;
using MasterChefTakip.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

[Authorize(Roles = AppRoles.Admin)]
public class ReceiptCatalogController : Controller
{
    private readonly ApplicationDbContext _db;

    public ReceiptCatalogController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int id)
    {
        var receipt = await _db.Set<PurchaseGoodsReceipt>()
            .AsNoTracking()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (receipt == null)
            return NotFound();

        var model = new ReceiptCatalogPage
        {
            ReceiptId = receipt.Id,
            ReceiptNumber = receipt.ReceiptNumber
        };

        await using var connection = new SqliteConnection(
            _db.Database.GetConnectionString());

        await connection.OpenAsync();

        foreach (var line in receipt.Lines.OrderBy(x => x.LineNumber))
        {
            var item = new ReceiptCatalogLine
            {
                Id = line.Id,
                Description = line.ProductDescription,
                Unit = line.Unit,
                AcceptedQuantity = line.AcceptedQuantity
            };

            await using var cmd = connection.CreateCommand();

            cmd.CommandText = @"
                SELECT
                    COALESCE(c.MaterialCode, ''),
                    COALESCE(c.MaterialName, '')
                FROM MCMaterialReceiptLinks l
                INNER JOIN MCMaterialCatalog c
                    ON c.Id = l.MaterialCatalogId
                WHERE l.PurchaseGoodsReceiptLineId = @id";

            cmd.Parameters.AddWithValue("@id", line.Id);

            await using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                item.MaterialCode = reader.GetString(0);
                item.MaterialName = reader.GetString(1);
            }

            model.Lines.Add(item);
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(
        int receiptId,
        int lineId,
        string materialCode)
    {
        materialCode = (materialCode ?? "").Trim();

        if (materialCode.Length == 0 || materialCode.Length > 120)
            return BadRequest("Gecersiz malzeme kodu.");

        var line = await _db.Set<PurchaseGoodsReceiptLine>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Id == lineId &&
                x.PurchaseGoodsReceiptId == receiptId);

        if (line == null)
            return NotFound();

        if (line.AcceptedQuantity <= 0)
            return BadRequest("Kabul edilmis miktar bulunmuyor.");

        await using var connection = new SqliteConnection(
            _db.Database.GetConnectionString());

        await connection.OpenAsync();

        await using var tx = await connection.BeginTransactionAsync();

        async Task<long> Count(string sql, params (string, object)[] args)
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = sql;

            foreach (var p in args)
                cmd.Parameters.AddWithValue(p.Item1, p.Item2);

            return Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }

        var transferred = await Count(@"
            SELECT
                (SELECT COUNT(*) FROM RawMaterialReceiptTransfers
                 WHERE PurchaseGoodsReceiptLineId = @id)
                +
                (SELECT COUNT(*) FROM GoodsReceiptStockTransfers
                 WHERE PurchaseGoodsReceiptLineId = @id)",
            ("@id", lineId));

        if (transferred > 0)
            return Conflict("Stoga aktarilmis kalem degistirilemez.");

        var existing = await Count(@"
            SELECT COUNT(*) FROM MCMaterialReceiptLinks
            WHERE PurchaseGoodsReceiptLineId = @id",
            ("@id", lineId));

        if (existing > 0)
            return Conflict("Bu kalem zaten eslestirilmis.");

        var matches = await Count(@"
            SELECT COUNT(*) FROM MCMaterialCatalog
            WHERE UPPER(TRIM(MaterialCode)) = UPPER(TRIM(@code))",
            ("@code", materialCode));

        if (matches != 1)
            return BadRequest(
                "Malzeme kodu bulunamadi veya birden fazla kayit var.");

        await using var insert = connection.CreateCommand();
        insert.Transaction = (SqliteTransaction)tx;

        insert.CommandText = @"
            INSERT INTO MCMaterialReceiptLinks
            (
                PurchaseGoodsReceiptLineId,
                MaterialCatalogId,
                MatchedBy,
                MatchedAtUtc
            )
            SELECT
                @line,
                Id,
                @user,
                @time
            FROM MCMaterialCatalog
            WHERE UPPER(TRIM(MaterialCode)) = UPPER(TRIM(@code))";

        insert.Parameters.AddWithValue("@line", lineId);
        insert.Parameters.AddWithValue("@code", materialCode);
        insert.Parameters.AddWithValue(
            "@user",
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "");
        insert.Parameters.AddWithValue(
            "@time", DateTime.UtcNow.ToString("O"));

        try
        {
            await insert.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }
        catch (SqliteException)
        {
            await tx.RollbackAsync();
            return Conflict("Eslestirme kaydedilemedi.");
        }

        TempData["Success"] = "Malzeme eslestirildi.";

        return RedirectToAction(nameof(Index), new { id = receiptId });
    }
}

public class ReceiptCatalogPage
{
    public int ReceiptId { get; set; }
    public string ReceiptNumber { get; set; } = "";
    public List<ReceiptCatalogLine> Lines { get; set; } = new();
}

public class ReceiptCatalogLine
{
    public int Id { get; set; }
    public string Description { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal AcceptedQuantity { get; set; }
    public string MaterialCode { get; set; } = "";
    public string MaterialName { get; set; } = "";
}
