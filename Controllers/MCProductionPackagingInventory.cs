using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using MasterChefTakip.Data;
using MasterChefTakip.Models;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MasterChefTakip.Controllers;

// MC_PACKAGING_REAL_CONSUMPTION_V1
public static class MCProductionPackagingStock
{
    public static void Ensure(ApplicationDbContext db)
    {
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS MCPackagingMaterials (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Unit TEXT NOT NULL,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE TABLE IF NOT EXISTS MCPackagingProductRecipes (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProductId INTEGER NOT NULL,
                MaterialId INTEGER NOT NULL,
                QtyPerKg TEXT NOT NULL,
                UNIQUE(ProductId,MaterialId)
            );

            CREATE TABLE IF NOT EXISTS MCPackagingMaterialMovements (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                MaterialId INTEGER NOT NULL,
                Direction TEXT NOT NULL,
                Quantity TEXT NOT NULL,
                ProductionOrderId INTEGER,
                Note TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE UNIQUE INDEX IF NOT EXISTS
                UX_MCPackagingMaterialOrder
            ON MCPackagingMaterialMovements(
                ProductionOrderId,MaterialId
            )
            WHERE ProductionOrderId IS NOT NULL;
            """);
    }

    private static decimal Parse(object value)
    {
        var text = Convert.ToString(
            value,CultureInfo.InvariantCulture) ?? "";

        if (!decimal.TryParse(
            text,NumberStyles.Number,
            CultureInfo.InvariantCulture,out var number))
        {
            throw new InvalidOperationException(
                "Ambalaj miktari okunamadi.");
        }

        return number;
    }

    public static void Apply(
        ApplicationDbContext db,
        int? productId,
        int productionOrderId,
        decimal producedKg)
    {
        if (!productId.HasValue)
            return;

        if (productionOrderId <= 0 || producedKg <= 0m)
            throw new InvalidOperationException(
                "Uretim miktari veya emir bilgisi gecersiz.");

        Ensure(db);

        var connection = db.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
            db.Database.OpenConnection();

        var transaction = db.Database.CurrentTransaction?
            .GetDbTransaction();

        if (transaction == null)
            throw new InvalidOperationException(
                "Ambalaj tuketimi icin aktif uretim transaction bulunamadi.");

        using var recipeCommand = connection.CreateCommand();
        recipeCommand.Transaction = transaction;

        recipeCommand.CommandText = """
            SELECT MaterialId,QtyPerKg
            FROM MCPackagingProductRecipes
            WHERE ProductId=@product
            ORDER BY MaterialId
            """;

        var productParameter = recipeCommand.CreateParameter();
        productParameter.ParameterName = "@product";
        productParameter.Value = productId.Value;
        recipeCommand.Parameters.Add(productParameter);

        var requirements =
            new System.Collections.Generic.List<(long Id,decimal Qty)>();

        using (var recipeReader = recipeCommand.ExecuteReader())
        {
            while (recipeReader.Read())
            {
                var materialId = Convert.ToInt64(
                    recipeReader.GetValue(0));

                var perKg = Parse(recipeReader.GetValue(1));

                if (perKg <= 0m)
                    throw new InvalidOperationException(
                        "Ambalaj recete miktari gecersiz.");

                requirements.Add((
                    materialId,
                    decimal.Round(
                        perKg * producedKg,3,
                        MidpointRounding.AwayFromZero)
                ));
            }
        }

        // Recetesi tanimlanmamis urunlerde
        // otomatik ambalaj tuketimi yapilmaz.
        if (requirements.Count == 0)
            return;

        foreach (var requirement in requirements)
        {
            using var balanceCommand = connection.CreateCommand();
            balanceCommand.Transaction = transaction;

            balanceCommand.CommandText = """
                SELECT COALESCE(SUM(
                    CASE Direction
                        WHEN 'IN' THEN CAST(Quantity AS REAL)
                        WHEN 'OUT' THEN -CAST(Quantity AS REAL)
                        ELSE 0
                    END
                ),0)
                FROM MCPackagingMaterialMovements
                WHERE MaterialId=@material
                """;

            var materialParameter = balanceCommand.CreateParameter();
            materialParameter.ParameterName = "@material";
            materialParameter.Value = requirement.Id;
            balanceCommand.Parameters.Add(materialParameter);

            var balance = Parse(
                balanceCommand.ExecuteScalar() ?? 0);

            if (balance + 0.0000001m < requirement.Qty)
            {
                throw new InvalidOperationException(
                    "Yetersiz ambalaj stogu. Malzeme ID: " +
                    requirement.Id +
                    ". Gerekli: " +
                    requirement.Qty.ToString(CultureInfo.InvariantCulture) +
                    ". Mevcut: " +
                    balance.ToString(CultureInfo.InvariantCulture));
            }
        }

        foreach (var requirement in requirements)
        {
            using var movementCommand = connection.CreateCommand();
            movementCommand.Transaction = transaction;

            movementCommand.CommandText = """
                INSERT INTO MCPackagingMaterialMovements
                    (MaterialId,Direction,Quantity,ProductionOrderId,Note)
                VALUES(@material,'OUT',@quantity,@order,'URETIM')
                """;

            void Add(string name,object value)
            {
                var parameter = movementCommand.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value;
                movementCommand.Parameters.Add(parameter);
            }

            Add("@material",requirement.Id);
            Add("@quantity",
                requirement.Qty.ToString(CultureInfo.InvariantCulture));
            Add("@order",productionOrderId);

            movementCommand.ExecuteNonQuery();
        }
    }
}

[Authorize(Roles = AppRoles.Admin)]
[Route("MCPackagingInventory")]
public sealed class MCPackagingInventoryController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IAntiforgery _antiforgery;

    public MCPackagingInventoryController(
        ApplicationDbContext db,
        IAntiforgery antiforgery)
    {
        _db = db;
        _antiforgery = antiforgery;
    }

    private static string H(object? value) =>
        WebUtility.HtmlEncode(Convert.ToString(value) ?? "");

    private string Token() =>
        "<input type='hidden' name='__RequestVerificationToken' value='" +
        H(_antiforgery.GetAndStoreTokens(HttpContext).RequestToken) + "'>";

    private static bool Qty(string? value,out decimal amount)
    {
        amount = 0m;

        if (!Regex.IsMatch(
            (value ?? "").Trim(),@"^\d{1,9}([.,]\d{1,3})?$"))
            return false;

        return decimal.TryParse(
            value!.Trim().Replace(",","."),
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,out amount) &&
            amount > 0m && amount <= 100000000m;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        MCProductionPackagingStock.Ensure(_db);
        _db.Database.OpenConnection();

        var html = new StringBuilder();

        html.Append("""
            <!doctype html><html lang='tr'><meta charset='utf-8'>
            <meta name='viewport' content='width=device-width,initial-scale=1'>
            <title>MASTER CHEF - Ambalaj Envanteri</title>
            <style>
            body{font-family:Arial;background:#f1f5f9;color:#172033;padding:24px}
            main{max-width:1150px;margin:auto}
            section{background:white;padding:20px;margin:16px 0;border-radius:10px}
            input,select{padding:10px;margin:5px;border:1px solid #ccc;border-radius:5px}
            button{background:#24496c;color:white;border:0;padding:11px;border-radius:5px}
            table{width:100%;border-collapse:collapse}
            th,td{padding:10px;border-bottom:1px solid #ddd;text-align:left}
            th{background:#edf3f8}
            </style><body><main>
            <h1>MASTER CHEF - Ambalaj Malzemesi Envanteri</h1>
            <p>Şişe, kapak, etiket, koli ve stick ambalaj stokları.</p>
            <p>Stoklar yalnızca gerçek giriş kayıtlarıyla artar.
            Üretim tamamlanırken tanımlı reçeteye göre azalır.</p>
            <section><h2>1. Ambalaj Malzemesi Tanımla</h2>
            <form method='post' action='/MCPackagingInventory/material'>
            """);

        html.Append(Token());

        html.Append("""
            <input name='name' maxlength='150'
                   placeholder='Ambalaj adı' required>
            <select name='unit'>
                <option value='ADET'>Adet</option>
                <option value='KG'>Kg</option>
                <option value='METRE'>Metre</option>
            </select>
            <button>Kaydet</button></form></section>
            <section><h2>2. Gerçek Ambalaj Stok Girişi</h2>
            <form method='post' action='/MCPackagingInventory/receipt'>
            """);

        html.Append(Token());
        html.Append("<select name='materialId' required>");

        using (var cmd = _db.Database.GetDbConnection().CreateCommand())
        {
            cmd.CommandText = """
                SELECT Id,Name,Unit
                FROM MCPackagingMaterials ORDER BY Id DESC
                """;

            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                html.Append("<option value='");
                html.Append(reader.GetValue(0));
                html.Append("'>");
                html.Append(H(reader.GetValue(1)));
                html.Append(" (");
                html.Append(H(reader.GetValue(2)));
                html.Append(")</option>");
            }
        }

        html.Append("""
            </select>
            <input name='quantity' placeholder='Gerçek giriş miktarı' required>
            <input name='note' maxlength='200'
                   placeholder='İrsaliye / açıklama'>
            <button>Stok Girişi Kaydet</button></form></section>
            <section><h2>3. Ürüne Ambalaj Reçetesi Bağla</h2>
            <p>Mevcut ürün kartının ID numarasını gir.
            Miktar, 1 kg mamul için gereken ambalaj miktarıdır.</p>
            <form method='post' action='/MCPackagingInventory/recipe'>
            """);

        html.Append(Token());

        html.Append("""
            <input type='number' min='1' name='productId'
                   placeholder='Ürün kartı ID' required>
            <input type='number' min='1' name='materialId'
                   placeholder='Ambalaj malzemesi ID' required>
            <input name='qtyPerKg'
                   placeholder='1 kg ürün için miktar' required>
            <button>Reçeteye Bağla</button></form></section>
            <section><h2>4. Ambalaj Stok Bakiyeleri</h2>
            <table><tr><th>ID</th><th>Malzeme</th>
            <th>Birim</th><th>Mevcut Stok</th></tr>
            """);

        using (var cmd = _db.Database.GetDbConnection().CreateCommand())
        {
            cmd.CommandText = """
                SELECT m.Id,m.Name,m.Unit,
                COALESCE(SUM(
                    CASE v.Direction
                        WHEN 'IN' THEN CAST(v.Quantity AS REAL)
                        WHEN 'OUT' THEN -CAST(v.Quantity AS REAL)
                        ELSE 0 END
                ),0)
                FROM MCPackagingMaterials m
                LEFT JOIN MCPackagingMaterialMovements v
                    ON v.MaterialId=m.Id
                GROUP BY m.Id,m.Name,m.Unit
                ORDER BY m.Id DESC
                """;

            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                html.Append("<tr>");

                for (var i = 0; i < 4; i++)
                {
                    html.Append("<td>");
                    html.Append(H(reader.GetValue(i)));
                    html.Append("</td>");
                }

                html.Append("</tr>");
            }
        }

        html.Append("""
            </table></section><section><h2>5. Ürün Ambalaj Reçeteleri</h2>
            <table><tr><th>Ürün ID</th><th>Malzeme</th>
            <th>1 kg İçin</th><th>Birim</th></tr>
            """);

        using (var cmd = _db.Database.GetDbConnection().CreateCommand())
        {
            cmd.CommandText = """
                SELECT r.ProductId,m.Name,r.QtyPerKg,m.Unit
                FROM MCPackagingProductRecipes r
                JOIN MCPackagingMaterials m ON m.Id=r.MaterialId
                ORDER BY r.ProductId,m.Name
                """;

            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                html.Append("<tr>");

                for (var i = 0; i < 4; i++)
                {
                    html.Append("<td>");
                    html.Append(H(reader.GetValue(i)));
                    html.Append("</td>");
                }

                html.Append("</tr>");
            }
        }

        html.Append("""
            </table></section></main></body></html>
            """);

        return Content(html.ToString(),"text/html; charset=utf-8");
    }

    [HttpPost("material")]
    [ValidateAntiForgeryToken]
    public IActionResult CreateMaterial(string name,string unit)
    {
        name = (name ?? "").Trim();
        unit = (unit ?? "").Trim();

        if (name.Length < 2 || name.Length > 150 ||
            (unit != "ADET" && unit != "KG" && unit != "METRE"))
            return BadRequest("Ambalaj malzemesi bilgisi geçersiz.");

        MCProductionPackagingStock.Ensure(_db);

        _db.Database.ExecuteSqlInterpolated(
            $"INSERT INTO MCPackagingMaterials(Name,Unit) VALUES({name},{unit})");

        return Redirect("/MCPackagingInventory");
    }

    [HttpPost("receipt")]
    [ValidateAntiForgeryToken]
    public IActionResult Receipt(
        long materialId,string quantity,string? note)
    {
        if (materialId <= 0 || !Qty(quantity,out var amount))
            return BadRequest("Stok giriş miktarı geçersiz.");

        note = (note ?? "").Trim();

        if (note.Length > 200)
            return BadRequest("Açıklama çok uzun.");

        MCProductionPackagingStock.Ensure(_db);

        _db.Database.OpenConnection();

        using var check = _db.Database.GetDbConnection().CreateCommand();
        check.CommandText =
            "SELECT COUNT(*) FROM MCPackagingMaterials WHERE Id=@id";

        var parameter = check.CreateParameter();
        parameter.ParameterName = "@id";
        parameter.Value = materialId;
        check.Parameters.Add(parameter);

        if (Convert.ToInt64(check.ExecuteScalar()) != 1)
            return BadRequest("Ambalaj kartı bulunamadı.");

        var amountText = amount.ToString(CultureInfo.InvariantCulture);

        _db.Database.ExecuteSqlInterpolated($"""
            INSERT INTO MCPackagingMaterialMovements
                (MaterialId,Direction,Quantity,Note)
            VALUES({materialId},'IN',{amountText},{note})
            """);

        return Redirect("/MCPackagingInventory");
    }

    [HttpPost("recipe")]
    [ValidateAntiForgeryToken]
    public IActionResult SetRecipe(
        int productId,long materialId,string qtyPerKg)
    {
        if (productId <= 0 || materialId <= 0 ||
            !Qty(qtyPerKg,out var quantity))
            return BadRequest("Reçete bilgileri geçersiz.");

        if (!_db.Set<Product>().Any(x => x.Id == productId))
            return BadRequest("Mevcut ürün kartı bulunamadı.");

        MCProductionPackagingStock.Ensure(_db);
        _db.Database.OpenConnection();

        using var check = _db.Database.GetDbConnection().CreateCommand();
        check.CommandText =
            "SELECT COUNT(*) FROM MCPackagingMaterials WHERE Id=@id";

        var parameter = check.CreateParameter();
        parameter.ParameterName = "@id";
        parameter.Value = materialId;
        check.Parameters.Add(parameter);

        if (Convert.ToInt64(check.ExecuteScalar()) != 1)
            return BadRequest("Ambalaj malzemesi bulunamadı.");

        var quantityText = quantity.ToString(
            CultureInfo.InvariantCulture);

        _db.Database.ExecuteSqlInterpolated($"""
            INSERT INTO MCPackagingProductRecipes
                (ProductId,MaterialId,QtyPerKg)
            VALUES({productId},{materialId},{quantityText})
            ON CONFLICT(ProductId,MaterialId)
            DO UPDATE SET QtyPerKg=excluded.QtyPerKg
            """);

        return Redirect("/MCPackagingInventory");
    }
}