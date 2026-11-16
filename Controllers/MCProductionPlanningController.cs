using MasterChefTakip.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

[Microsoft.AspNetCore.Authorization.Authorize(Policy = AppPermissions.ProductionManage)]
[Route("MCProductionPlanning")]
public sealed partial class MCProductionPlanningController : Controller
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly IAntiforgery _antiforgery;

    public MCProductionPlanningController(
        IConfiguration config,
        IWebHostEnvironment env,
        IAntiforgery antiforgery)
    {
        _config = config;
        _env = env;
        _antiforgery = antiforgery;
    }

    private static string H(object? value) =>
        WebUtility.HtmlEncode(Convert.ToString(value) ?? "");

    private static bool Qty(string? text, out decimal value)
    {
        value = 0m;
        text = (text ?? "").Trim();

        if (!Regex.IsMatch(text, @"^\d{1,9}([.,]\d{1,3})?$"))
            return false;

        return decimal.TryParse(
            text.Replace(",", "."),
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out value) && value > 0m;
    }

    private static string N(decimal value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private string DatabaseConnection()
    {
        var candidates = new List<string>();

        foreach (var item in _config.AsEnumerable())
        {
            var value = item.Value;

            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (value.Contains("Data Source=", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var builder = new SqliteConnectionStringBuilder(value);
                    candidates.Add(builder.DataSource);
                }
                catch { }
            }
        }

        var roots = new[]
        {
            _env.ContentRootPath,
            Path.Combine(_env.ContentRootPath, "Data"),
            Path.Combine(_env.ContentRootPath, "App_Data"),
            Path.Combine(_env.ContentRootPath, "Database"),
            Path.Combine(_env.ContentRootPath, "db")
        };

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
                continue;

            foreach (var extension in new[] { "*.db", "*.sqlite", "*.sqlite3" })
            {
                candidates.AddRange(
                    Directory.EnumerateFiles(root, extension, SearchOption.TopDirectoryOnly));
            }
        }

        foreach (var source in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(source) || source == ":memory:")
                continue;

            string path;

            try
            {
                path = Path.GetFullPath(
                    Path.IsPathRooted(source)
                        ? source
                        : Path.Combine(_env.ContentRootPath, source));
            }
            catch
            {
                continue;
            }

            if (!System.IO.File.Exists(path))
                continue;

            try
            {
                var checkString = new SqliteConnectionStringBuilder
                {
                    DataSource = path,
                    Mode = SqliteOpenMode.ReadOnly
                }.ToString();

                using var check = new SqliteConnection(checkString);
                check.Open();

                using var cmd = check.CreateCommand();
                cmd.CommandText = """
                    SELECT COUNT(*)
                    FROM sqlite_master
                    WHERE type='table'
                      AND name='MCPurchaseInvoices'
                    """;

                if (Convert.ToInt64(cmd.ExecuteScalar()) != 1)
                    continue;

                return new SqliteConnectionStringBuilder
                {
                    DataSource = path,
                    Mode = SqliteOpenMode.ReadWrite
                }.ToString();
            }
            catch { }
        }

        throw new InvalidOperationException(
            "Mevcut TAKIP veritabani guvenli bicimde belirlenemedi. " +
            "Yeni veya bos bir veritabani olusturulmadi.");
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(DatabaseConnection());
        connection.Open();

        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();

        Initialize(connection);
        return connection;
    }

    private static void Initialize(SqliteConnection db)
    {
        using var cmd = db.CreateCommand();

        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS MCProductionPlanningRecipes (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProductName TEXT NOT NULL,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                IsActive INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS MCProductionPlanningRecipeLines (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RecipeId INTEGER NOT NULL,
                MaterialName TEXT NOT NULL,
                QtyPerKg TEXT NOT NULL,
                FOREIGN KEY (RecipeId)
                    REFERENCES MCProductionPlanningRecipes(Id)
                    ON DELETE RESTRICT
            );

            CREATE TABLE IF NOT EXISTS MCProductionPlanningOrders (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RecipeId INTEGER NOT NULL,
                ProductName TEXT NOT NULL,
                PlannedKg TEXT NOT NULL,
                Status TEXT NOT NULL DEFAULT 'PLANLANDI',
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (RecipeId)
                    REFERENCES MCProductionPlanningRecipes(Id)
            );

            CREATE TABLE IF NOT EXISTS MCProductionPlanningOrderMaterials (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OrderId INTEGER NOT NULL,
                MaterialName TEXT NOT NULL,
                PlannedKg TEXT NOT NULL,
                FOREIGN KEY (OrderId)
                    REFERENCES MCProductionPlanningOrders(Id)
                    ON DELETE RESTRICT
            );

            CREATE INDEX IF NOT EXISTS IX_MCProductionRecipeLines_Recipe
                ON MCProductionPlanningRecipeLines(RecipeId);

            CREATE INDEX IF NOT EXISTS IX_MCProductionOrders_Recipe
                ON MCProductionPlanningOrders(RecipeId);

            CREATE INDEX IF NOT EXISTS IX_MCProductionOrderMaterials_Order
                ON MCProductionPlanningOrderMaterials(OrderId);
            """;

        cmd.ExecuteNonQuery();
    }

    private static SqliteCommand Command(
        SqliteConnection db,
        string sql,
        params (string Name, object Value)[] args)
    {
        var cmd = db.CreateCommand();
        cmd.CommandText = sql;

        foreach (var arg in args)
            cmd.Parameters.AddWithValue(arg.Name, arg.Value);

        return cmd;
    }

    private string Token()
    {
        var token = _antiforgery.GetAndStoreTokens(HttpContext).RequestToken;
        return "<input type='hidden' name='__RequestVerificationToken' value='" +
               H(token) + "'>";
    }

    private IActionResult ErrorPage(string message)
    {
        return Content(
            "<!doctype html><html><meta charset='utf-8'>" +
            "<body style='font-family:Arial;padding:30px'>" +
            "<h2>MASTER CHEF - Uretim Planlama</h2>" +
            "<p style='color:#b91c1c'>" + H(message) + "</p>" +
            "<a href='/MCProductionPlanning'>Geri Don</a>" +
            "</body></html>",
            "text/html; charset=utf-8");
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        using var db = Open();

        var html = new StringBuilder();

        html.Append("""
            <!doctype html>
            <html lang="tr">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <title>MASTER CHEF - Uretim Planlama</title>
            <style>
            body{font-family:Arial,sans-serif;background:#f1f5f9;
                 color:#172033;margin:0;padding:24px}
            .wrap{max-width:1250px;margin:auto}
            h1{color:#173753;margin-bottom:5px}
            h2{font-size:19px;color:#24496c}
            .card{background:white;padding:20px;border-radius:12px;
                  margin:18px 0;border:1px solid #e2e8f0}
            input,select{padding:10px;border:1px solid #cbd5e1;
                         border-radius:6px;max-width:100%;box-sizing:border-box}
            button{background:#24496c;color:white;padding:11px 18px;
                   border:0;border-radius:6px;cursor:pointer}
            table{border-collapse:collapse;width:100%;background:white}
            th,td{border-bottom:1px solid #e2e8f0;padding:11px;text-align:left}
            th{background:#edf3f8}
            .row{display:flex;gap:12px;flex-wrap:wrap;align-items:end}
            label{display:block;font-size:13px;margin-bottom:5px}
            .hint{color:#64748b;font-size:13px}
            .badge{background:#dcfce7;padding:5px 9px;border-radius:5px}
            .cancel{background:#991b1b}
            .scroll{overflow-x:auto}
            </style>
            </head>
            <body>
            <div class="wrap">
            <h1>MASTER CHEF - Uretim Planlama</h1>
            <p>Uretim emirleri, urun receteleri ve planlanan hammadde ihtiyaclari</p>
            <p class="hint">Bu modul planlama yapar.
            Gercek stok dusumu ve mamul girisi yapmaz.</p>
            """);

                html.Append("<div class='card'>");
        html.Append("<a href='/MCProductionPlanning/recipes' ");
        html.Append("style='display:inline-block;background:#24496c;color:white;");
        html.Append("padding:12px 18px;border-radius:7px;text-decoration:none'>");
        html.Append("Receteleri Duzenle</a></div>");
                html.Append("<div class='card'><a href='/MCProductionPlanning/operations' style='display:inline-block;background:#24496c;color:white;padding:12px 18px;border-radius:7px;text-decoration:none'>Uretim Operasyonlari ve Kart Baglantilari</a></div>");
        html.Append("<div class='card'><h2>1. Yeni Uretim Recetesi</h2>");
        html.Append("<form method='post' action='/MCProductionPlanning/recipe'>");
        html.Append(Token());
        html.Append("""
            <div class="row">
            <div><label>Urun adi</label>
            <input name="productName" maxlength="150"
                   placeholder="Ornek: Burger Sos 950 g" required></div>
            <button type="submit">Recete Olustur</button>
            </div></form></div>
            """);

        html.Append("<div class='card'><h2>2. Receteye Hammadde Ekle</h2>");

        html.Append("<form method='post' action='/MCProductionPlanning/material'>");
        html.Append(Token());

        html.Append("<div class='row'><div><label>Recete</label>");
        html.Append("<select name='recipeId' required>");

        using (var cmd = Command(db, """
            SELECT Id, ProductName
            FROM MCProductionPlanningRecipes
            WHERE IsActive=1
            ORDER BY Id DESC
            """))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                html.Append("<option value='");
                html.Append(reader.GetInt64(0));
                html.Append("'>");
                html.Append(H(reader.GetString(1)));
                html.Append(" (#");
                html.Append(reader.GetInt64(0));
                html.Append(")</option>");
            }
        }

        html.Append("</select></div>");

        html.Append("""
            <div><label>Hammadde adi</label>
            <input name="materialName" maxlength="150"
                   placeholder="Ornek: Domates salcasi" required></div>
            <div><label>1 kg urun icin hammadde (kg)</label>
            <input name="qtyPerKg" inputmode="decimal"
                   placeholder="0,250" required></div>
            <button type="submit">Hammadde Ekle</button>
            </div></form>
            <p class="hint">Ornek: 1 kg sos icin 250 gram salca
            kullaniliyorsa 0,250 yazilir.</p></div>
            """);

        html.Append("<div class='card'><h2>3. Uretim Emri Olustur</h2>");
        html.Append("<form method='post' action='/MCProductionPlanning/order'>");
        html.Append(Token());

        html.Append("<div class='row'><div><label>Uretilecek urun</label>");
        html.Append("<select name='recipeId' required>");

        using (var cmd = Command(db, """
            SELECT r.Id, r.ProductName
            FROM MCProductionPlanningRecipes r
            WHERE r.IsActive=1
            ORDER BY r.Id DESC
            """))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                html.Append("<option value='");
                html.Append(reader.GetInt64(0));
                html.Append("'>");
                html.Append(H(reader.GetString(1)));
                html.Append("</option>");
            }
        }

        html.Append("</select></div>");

        html.Append("""
            <div><label>Planlanan uretim miktari (kg)</label>
            <input name="plannedKg" inputmode="decimal"
                   placeholder="1000" required></div>
            <button type="submit">Uretim Emri Olustur</button>
            </div></form>
            <p class="hint">Emir olusturuldugunda recetedeki
            hammaddeler o emre kaydedilir.
            Sonradan recete degisse bile eski emrin miktarlari korunur.</p>
            </div>
            """);

        html.Append("<div class='card'><h2>4. Kayitli Receteler</h2>");
        html.Append("<div class='scroll'><table>");
        html.Append("<tr><th>Recete ID</th><th>Urun</th>");
        html.Append("<th>Hammadde</th><th>1 kg icin miktar</th></tr>");

        using (var cmd = Command(db, """
            SELECT r.Id, r.ProductName,
                   COALESCE(l.MaterialName,''),
                   COALESCE(l.QtyPerKg,'')
            FROM MCProductionPlanningRecipes r
            LEFT JOIN MCProductionPlanningRecipeLines l
                ON l.RecipeId=r.Id
            WHERE r.IsActive=1
            ORDER BY r.Id DESC,l.Id
            """))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                html.Append("<tr><td>");
                html.Append(reader.GetInt64(0));
                html.Append("</td><td>");
                html.Append(H(reader.GetString(1)));
                html.Append("</td><td>");
                html.Append(H(reader.GetString(2)));
                html.Append("</td><td>");
                html.Append(H(reader.GetString(3)));
                html.Append(" kg</td></tr>");
            }
        }

        html.Append("</table></div></div>");

        html.Append("<div class='card'><h2>5. Uretim Emirleri</h2>");
        html.Append("<div class='scroll'><table>");
        html.Append("<tr><th>Emir</th><th>Urun</th><th>Planlanan</th>");
        html.Append("<th>Durum</th><th>Tarih</th><th>Islem</th></tr>");

        using (var cmd = Command(db, """
            SELECT Id,ProductName,PlannedKg,Status,CreatedAt
            FROM MCProductionPlanningOrders
            ORDER BY Id DESC
            LIMIT 200
            """))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                var status = reader.GetString(3);

                html.Append("<tr><td>#");
                html.Append(id);
                html.Append("</td><td>");
                html.Append(H(reader.GetString(1)));
                html.Append("</td><td>");
                html.Append(H(reader.GetString(2)));
                html.Append(" kg</td><td><span class='badge'>");
                html.Append(H(status));
                html.Append("</span></td><td>");
                html.Append(H(reader.GetString(4)));
                html.Append("</td><td>");

                if (status == "PLANLANDI")
                {
                    html.Append("<form method='post' action='/MCProductionPlanning/cancel'>");
                    html.Append(Token());
                    html.Append("<input type='hidden' name='orderId' value='");
                    html.Append(id);
                    html.Append("'><button class='cancel' type='submit'>Iptal Et</button>");
                    html.Append("</form>");
                }
                else
                {
                    html.Append("-");
                }

                html.Append("</td></tr>");
            }
        }

        html.Append("</table></div></div>");

        html.Append("<div class='card'><h2>6. Uretim Emri Hammadde Ihtiyaci</h2>");
        html.Append("<div class='scroll'><table>");
        html.Append("<tr><th>Emir</th><th>Urun</th><th>Hammadde</th>");
        html.Append("<th>Gerekli miktar</th></tr>");

        using (var cmd = Command(db, """
            SELECT o.Id,o.ProductName,m.MaterialName,m.PlannedKg
            FROM MCProductionPlanningOrderMaterials m
            JOIN MCProductionPlanningOrders o ON o.Id=m.OrderId
            WHERE o.Status='PLANLANDI'
            ORDER BY o.Id DESC,m.Id
            LIMIT 1000
            """))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                html.Append("<tr><td>#");
                html.Append(reader.GetInt64(0));
                html.Append("</td><td>");
                html.Append(H(reader.GetString(1)));
                html.Append("</td><td>");
                html.Append(H(reader.GetString(2)));
                html.Append("</td><td>");
                html.Append(H(reader.GetString(3)));
                html.Append(" kg</td></tr>");
            }
        }

        html.Append("</table></div></div>");
        html.Append("</div></body></html>");

        return Content(html.ToString(), "text/html; charset=utf-8");
    }

    [HttpPost("recipe")]
    [ValidateAntiForgeryToken]
    public IActionResult CreateRecipe(string productName)
    {
        productName = (productName ?? "").Trim();

        if (productName.Length == 0 || productName.Length > 150)
            return ErrorPage("Gecerli bir urun adi giriniz.");

        using var db = Open();

        using var cmd = Command(db, """
            INSERT INTO MCProductionPlanningRecipes(ProductName)
            VALUES($name)
            """, ("$name", productName));

        cmd.ExecuteNonQuery();

        return Redirect("/MCProductionPlanning");
    }

    [HttpPost("material")]
    [ValidateAntiForgeryToken]
    public IActionResult AddMaterial(
        long recipeId,
        string materialName,
        string qtyPerKg)
    {
        materialName = (materialName ?? "").Trim();

        if (recipeId <= 0 ||
            materialName.Length == 0 ||
            materialName.Length > 150 ||
            !Qty(qtyPerKg, out var quantity) ||
            quantity > 1000m)
        {
            return ErrorPage("Hammadde veya miktar bilgisi gecersiz.");
        }

        using var db = Open();

        using (var check = Command(db, """
            SELECT COUNT(*)
            FROM MCProductionPlanningRecipes
            WHERE Id=$id AND IsActive=1
            """, ("$id", recipeId)))
        {
            if (Convert.ToInt64(check.ExecuteScalar()) != 1)
                return ErrorPage("Recete bulunamadi.");
        }

        using var cmd = Command(db, """
            INSERT INTO MCProductionPlanningRecipeLines
                (RecipeId,MaterialName,QtyPerKg)
            VALUES($id,$name,$qty)
            """,
            ("$id", recipeId),
            ("$name", materialName),
            ("$qty", N(quantity)));

        cmd.ExecuteNonQuery();

        return Redirect("/MCProductionPlanning");
    }

    [HttpPost("order")]
    [ValidateAntiForgeryToken]
    public IActionResult CreateOrder(long recipeId, string plannedKg)
    {
        if (recipeId <= 0 ||
            !Qty(plannedKg, out var quantity) ||
            quantity > 1000000m)
        {
            return ErrorPage("Uretim miktari gecersiz.");
        }

        using var db = Open();
        using var transaction = db.BeginTransaction();

        string productName;

        using (var recipe = Command(db, """
            SELECT ProductName
            FROM MCProductionPlanningRecipes
            WHERE Id=$id AND IsActive=1
            """, ("$id", recipeId)))
        {
            recipe.Transaction = transaction;

            var result = recipe.ExecuteScalar();

            if (result == null)
                return ErrorPage("Recete bulunamadi.");

            productName = Convert.ToString(result) ?? "";
        }

        var materials = new List<(string Name, decimal Amount)>();

        using (var lines = Command(db, """
            SELECT MaterialName,QtyPerKg
            FROM MCProductionPlanningRecipeLines
            WHERE RecipeId=$id
            ORDER BY Id
            """, ("$id", recipeId)))
        {
            lines.Transaction = transaction;

            using var reader = lines.ExecuteReader();

            while (reader.Read())
            {
                if (!Qty(reader.GetString(1), out var perKg))
                    return ErrorPage("Recetede gecersiz hammadde miktari var.");

                materials.Add((
                    reader.GetString(0),
                    perKg * quantity
                ));
            }
        }

        if (materials.Count == 0)
            return ErrorPage("Uretim emri icin once receteye hammadde ekleyiniz.");

        long orderId;

        using (var insert = Command(db, """
            INSERT INTO MCProductionPlanningOrders
                (RecipeId,ProductName,PlannedKg,Status)
            VALUES($recipe,$product,$quantity,'PLANLANDI');
            SELECT last_insert_rowid();
            """,
            ("$recipe", recipeId),
            ("$product", productName),
            ("$quantity", N(quantity))))
        {
            insert.Transaction = transaction;
            orderId = Convert.ToInt64(insert.ExecuteScalar());
        }

        foreach (var material in materials)
        {
            using var insertLine = Command(db, """
                INSERT INTO MCProductionPlanningOrderMaterials
                    (OrderId,MaterialName,PlannedKg)
                VALUES($order,$material,$quantity)
                """,
                ("$order", orderId),
                ("$material", material.Name),
                ("$quantity", N(material.Amount)));

            insertLine.Transaction = transaction;
            insertLine.ExecuteNonQuery();
        }

        transaction.Commit();

        return Redirect("/MCProductionPlanning");
    }

    [HttpPost("cancel")]
    [ValidateAntiForgeryToken]
    public IActionResult CancelOrder(long orderId)
    {
        if (orderId <= 0)
            return ErrorPage("Gecersiz uretim emri.");

        using var db = Open();

        using var cmd = Command(db, """
            UPDATE MCProductionPlanningOrders
            SET Status='IPTAL'
            WHERE Id=$id AND Status='PLANLANDI'
            """, ("$id", orderId));

        cmd.ExecuteNonQuery();

        return Redirect("/MCProductionPlanning");
    }

    // MC_PRODUCTION_RECIPE_MANAGEMENT_V1

    [HttpGet("recipes")]
    public IActionResult ManageRecipes()
    {
        using var db = Open();

        var html = new StringBuilder();

        html.Append("""
            <!doctype html>
            <html lang="tr">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <title>MASTER CHEF - Recete Yonetimi</title>
            <style>
            body{font-family:Arial;background:#f1f5f9;padding:25px;color:#172033}
            .wrap{max-width:1200px;margin:auto}
            .card{background:white;padding:20px;margin:15px 0;
                  border-radius:10px;border:1px solid #e2e8f0}
            input{padding:10px;border:1px solid #cbd5e1;border-radius:6px}
            button{padding:10px 14px;background:#24496c;color:white;
                   border:0;border-radius:6px;cursor:pointer}
            .danger{background:#991b1b}
            table{width:100%;border-collapse:collapse}
            th,td{padding:10px;text-align:left;border-bottom:1px solid #e2e8f0}
            th{background:#edf3f8}
            form{display:inline-flex;gap:8px;align-items:center;flex-wrap:wrap}
            .scroll{overflow-x:auto}
            </style>
            </head>
            <body><div class="wrap">
            <h1>MASTER CHEF - Recete Yonetimi</h1>
            <p><a href="/MCProductionPlanning">Uretim Planlamaya Don</a></p>
            <p>Recete degisiklikleri eski uretim emirlerini etkilemez.</p>
            """);

        html.Append("<div class='card'><h2>Kayitli Receteler</h2>");
        html.Append("<div class='scroll'><table>");
        html.Append("<tr><th>ID</th><th>Urun</th><th>Durum</th><th>Islemler</th></tr>");

        using (var cmd = Command(db, """
            SELECT Id,ProductName,IsActive
            FROM MCProductionPlanningRecipes
            ORDER BY IsActive DESC,Id DESC
            """))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                var name = reader.GetString(1);
                var active = reader.GetInt64(2) == 1;

                html.Append("<tr><td>");
                html.Append(id);
                html.Append("</td><td>");
                html.Append(H(name));
                html.Append("</td><td>");
                html.Append(active ? "AKTIF" : "PASIF");
                html.Append("</td><td>");

                if (active)
                {
                    html.Append("<form method='post' action='/MCProductionPlanning/renameRecipe'>");
                    html.Append(Token());
                    html.Append("<input type='hidden' name='recipeId' value='");
                    html.Append(id);
                    html.Append("'>");
                    html.Append("<input name='productName' maxlength='150' value='");
                    html.Append(H(name));
                    html.Append("' required>");
                    html.Append("<button type='submit'>Adi Kaydet</button></form> ");

                    html.Append("<form method='post' action='/MCProductionPlanning/deactivateRecipe'>");
                    html.Append(Token());
                    html.Append("<input type='hidden' name='recipeId' value='");
                    html.Append(id);
                    html.Append("'>");
                    html.Append("<button class='danger' type='submit'>Pasiflestir</button>");
                    html.Append("</form>");
                }
                else
                {
                    html.Append("-");
                }

                html.Append("</td></tr>");
            }
        }

        html.Append("</table></div></div>");

        html.Append("<div class='card'><h2>Recete Hammadde Satirlari</h2>");
        html.Append("<div class='scroll'><table>");
        html.Append("<tr><th>Recete</th><th>Hammadde</th>");
        html.Append("<th>1 kg Urun Icin</th><th>Islemler</th></tr>");

        using (var cmd = Command(db, """
            SELECT l.Id,r.Id,r.ProductName,l.MaterialName,l.QtyPerKg
            FROM MCProductionPlanningRecipeLines l
            JOIN MCProductionPlanningRecipes r ON r.Id=l.RecipeId
            WHERE r.IsActive=1
            ORDER BY r.Id DESC,l.Id
            """))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var lineId = reader.GetInt64(0);
                var recipeId = reader.GetInt64(1);

                html.Append("<tr><td>#");
                html.Append(recipeId);
                html.Append(" - ");
                html.Append(H(reader.GetString(2)));
                html.Append("</td><td>");
                html.Append(H(reader.GetString(3)));
                html.Append("</td><td>");
                html.Append(H(reader.GetString(4)));
                html.Append(" kg</td><td>");

                html.Append("<form method='post' action='/MCProductionPlanning/updateMaterial'>");
                html.Append(Token());
                html.Append("<input type='hidden' name='lineId' value='");
                html.Append(lineId);
                html.Append("'>");
                html.Append("<input name='materialName' maxlength='150' value='");
                html.Append(H(reader.GetString(3)));
                html.Append("' required>");
                html.Append("<input name='qtyPerKg' value='");
                html.Append(H(reader.GetString(4)));
                html.Append("' style='width:100px' required>");
                html.Append("<button type='submit'>Guncelle</button></form> ");

                html.Append("<form method='post' action='/MCProductionPlanning/removeMaterial'>");
                html.Append(Token());
                html.Append("<input type='hidden' name='lineId' value='");
                html.Append(lineId);
                html.Append("'>");
                html.Append("<button class='danger' type='submit'>Sil</button>");
                html.Append("</form></td></tr>");
            }
        }

        html.Append("</table></div></div>");
        html.Append("</div></body></html>");

        return Content(html.ToString(), "text/html; charset=utf-8");
    }

    [HttpPost("renameRecipe")]
    [ValidateAntiForgeryToken]
    public IActionResult RenameRecipe(long recipeId, string productName)
    {
        productName = (productName ?? "").Trim();

        if (recipeId <= 0 || productName.Length == 0 || productName.Length > 150)
            return ErrorPage("Gecersiz recete bilgisi.");

        using var db = Open();

        using var cmd = Command(db, """
            UPDATE MCProductionPlanningRecipes
            SET ProductName=$name
            WHERE Id=$id AND IsActive=1
            """,
            ("$name",productName),
            ("$id",recipeId));

        if (cmd.ExecuteNonQuery() != 1)
            return ErrorPage("Aktif recete bulunamadi.");

        return Redirect("/MCProductionPlanning/recipes");
    }

    [HttpPost("updateMaterial")]
    [ValidateAntiForgeryToken]
    public IActionResult UpdateMaterial(
        long lineId,
        string materialName,
        string qtyPerKg)
    {
        materialName = (materialName ?? "").Trim();

        if (lineId <= 0 ||
            materialName.Length == 0 ||
            materialName.Length > 150 ||
            !Qty(qtyPerKg,out var quantity) ||
            quantity > 1000m)
        {
            return ErrorPage("Hammadde veya miktar gecersiz.");
        }

        using var db = Open();

        using var cmd = Command(db, """
            UPDATE MCProductionPlanningRecipeLines
            SET MaterialName=$name,QtyPerKg=$qty
            WHERE Id=$id
              AND RecipeId IN
              (
                  SELECT Id
                  FROM MCProductionPlanningRecipes
                  WHERE IsActive=1
              )
            """,
            ("$name",materialName),
            ("$qty",N(quantity)),
            ("$id",lineId));

        if (cmd.ExecuteNonQuery() != 1)
            return ErrorPage("Aktif recetede hammadde bulunamadi.");

        return Redirect("/MCProductionPlanning/recipes");
    }

    [HttpPost("removeMaterial")]
    [ValidateAntiForgeryToken]
    public IActionResult RemoveMaterial(long lineId)
    {
        if (lineId <= 0)
            return ErrorPage("Gecersiz hammadde satiri.");

        using var db = Open();

        using var cmd = Command(db, """
            DELETE FROM MCProductionPlanningRecipeLines
            WHERE Id=$id
              AND RecipeId IN
              (
                  SELECT Id
                  FROM MCProductionPlanningRecipes
                  WHERE IsActive=1
              )
            """,("$id",lineId));

        if (cmd.ExecuteNonQuery() != 1)
            return ErrorPage("Aktif recetede hammadde bulunamadi.");

        return Redirect("/MCProductionPlanning/recipes");
    }

    [HttpPost("deactivateRecipe")]
    [ValidateAntiForgeryToken]
    public IActionResult DeactivateRecipe(long recipeId)
    {
        if (recipeId <= 0)
            return ErrorPage("Gecersiz recete.");

        using var db = Open();

        using var cmd = Command(db, """
            UPDATE MCProductionPlanningRecipes
            SET IsActive=0
            WHERE Id=$id AND IsActive=1
            """,("$id",recipeId));

        if (cmd.ExecuteNonQuery() != 1)
            return ErrorPage("Aktif recete bulunamadi.");

        return Redirect("/MCProductionPlanning/recipes");
    }
}