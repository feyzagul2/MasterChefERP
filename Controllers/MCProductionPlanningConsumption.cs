using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

// MC_PRODUCTION_CONSUMPTION_V1
public sealed partial class MCProductionPlanningController
{
    private SqliteConnection OpenConsumption()
    {
        var db = OpenOperations();

        try
        {
            using var schema = db.CreateCommand();

            schema.CommandText = """
                CREATE TABLE IF NOT EXISTS MCProductionPackagingRecipeLines (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    RecipeId INTEGER NOT NULL,
                    PackagingName TEXT NOT NULL,
                    QuantityPerKg TEXT NOT NULL,
                    Unit TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                    FOREIGN KEY(RecipeId)
                        REFERENCES MCProductionPlanningRecipes(Id)
                );

                CREATE TABLE IF NOT EXISTS MCProductionActualConsumption (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    OrderId INTEGER NOT NULL,
                    Kind TEXT NOT NULL,
                    SourceLineId INTEGER NOT NULL,
                    ItemName TEXT NOT NULL,
                    PlannedQuantity TEXT NOT NULL,
                    ActualQuantity TEXT NOT NULL,
                    Unit TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                    UNIQUE(OrderId,Kind,SourceLineId),
                    FOREIGN KEY(OrderId)
                        REFERENCES MCProductionPlanningOrders(Id)
                );

                CREATE INDEX IF NOT EXISTS IX_MCProductionPackagingRecipe
                    ON MCProductionPackagingRecipeLines(RecipeId);

                CREATE INDEX IF NOT EXISTS IX_MCProductionActualOrder
                    ON MCProductionActualConsumption(OrderId);
                """;

            schema.ExecuteNonQuery();
            return db;
        }
        catch
        {
            db.Dispose();
            throw;
        }
    }

    private static bool ConsumptionQuantity(
        string? input, bool allowZero, out decimal amount)
    {
        amount = 0m;
        input = (input ?? "").Trim();

        if (!Regex.IsMatch(input, @"^\d{1,9}([.,]\d{1,3})?$"))
            return false;

        if (!decimal.TryParse(
            input.Replace(",", "."),
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out amount))
            return false;

        return amount <= 100000000m &&
               (allowZero ? amount >= 0m : amount > 0m);
    }

    private static string ConsumptionNumber(decimal value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    [HttpGet("consumption")]
    public IActionResult Consumption()
    {
        using var db = OpenConsumption();
        var html = new StringBuilder();

        html.Append("""
            <!doctype html>
            <html lang="tr">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <title>MASTER CHEF - Uretim Tuketimi</title>
            <style>
            body{font-family:Arial;background:#f1f5f9;color:#172033;padding:24px}
            main{max-width:1250px;margin:auto}
            section{background:#fff;padding:20px;margin:18px 0;border-radius:10px}
            table{width:100%;border-collapse:collapse}
            th,td{padding:10px;text-align:left;border-bottom:1px solid #ddd}
            th{background:#edf3f8}
            input,select{padding:9px;border:1px solid #cbd5e1;border-radius:5px}
            button{background:#24496c;color:white;padding:10px;border:0;border-radius:5px}
            form{display:inline-flex;gap:8px;align-items:center;flex-wrap:wrap}
            .scroll{overflow-x:auto}
            .note{background:#fff7ed;padding:12px;border-radius:7px}
            </style>
            </head>
            <body><main>
            <h1>MASTER CHEF - Uretim Tuketimi</h1>
            <p><a href="/MCProductionPlanning">Uretim Planlama</a>
            &nbsp; | &nbsp;
            <a href="/MCProductionPlanning/operations">Uretim Operasyonlari</a></p>
            <p class="note">
            Burada kaydedilen tuketimler uretim kayitlaridir.
            Mevcut stok bakiyelerine henuz hareket islenmez.
            </p>
            <section>
            <h2>1. Receteye Ambalaj Ekle</h2>
            <form method="post" action="/MCProductionPlanning/addPackaging">
            """);

        html.Append(Token());
        html.Append("<select name='recipeId' required>");
        html.Append("<option value=''>Recete seciniz</option>");

        using (var recipesCmd = Command(db, """
            SELECT Id,ProductName
            FROM MCProductionPlanningRecipes
            WHERE IsActive=1
            ORDER BY Id DESC
            """))
        using (var recipesReader = recipesCmd.ExecuteReader())
        {
            while (recipesReader.Read())
            {
                html.Append("<option value='");
                html.Append(recipesReader.GetInt64(0));
                html.Append("'>");
                html.Append(H(recipesReader.GetString(1)));
                html.Append("</option>");
            }
        }

        html.Append("""
            </select>
            <input name="packagingName" maxlength="150"
                   placeholder="Sise, kapak, etiket, koli..." required>
            <input name="quantityPerKg" placeholder="1 kg icin miktar" required>
            <select name="unit">
            <option value="Adet">Adet</option>
            <option value="Kg">Kg</option>
            <option value="Metre">Metre</option>
            </select>
            <button type="submit">Ambalaj Ekle</button>
            </form>
            <p>Ornek: 1 kg urun icin 2 sise gerekiyorsa miktar 2 yazilir.</p>
            </section>
            <section>
            <h2>2. Ambalaj Receteleri</h2>
            <div class="scroll"><table>
            <tr>
            <th>Urun</th><th>Ambalaj</th><th>1 kg Icin</th><th>Islem</th>
            </tr>
            """);

        using (var packagingCmd = Command(db, """
            SELECT p.Id,r.ProductName,p.PackagingName,
                   p.QuantityPerKg,p.Unit
            FROM MCProductionPackagingRecipeLines p
            JOIN MCProductionPlanningRecipes r ON r.Id=p.RecipeId
            WHERE r.IsActive=1
            ORDER BY r.Id DESC,p.Id
            """))
        using (var packagingReader = packagingCmd.ExecuteReader())
        {
            while (packagingReader.Read())
            {
                html.Append("<tr><td>");
                html.Append(H(packagingReader.GetString(1)));
                html.Append("</td><td>");
                html.Append(H(packagingReader.GetString(2)));
                html.Append("</td><td>");
                html.Append(H(packagingReader.GetString(3)));
                html.Append(" ");
                html.Append(H(packagingReader.GetString(4)));
                html.Append("</td><td>");

                html.Append("<form method='post' action='/MCProductionPlanning/removePackaging'>");
                html.Append(Token());
                html.Append("<input type='hidden' name='lineId' value='");
                html.Append(packagingReader.GetInt64(0));
                html.Append("'><button type='submit'>Kaldir</button></form>");
                html.Append("</td></tr>");
            }
        }

        html.Append("""
            </table></div></section>
            <section>
            <h2>3. Tamamlanan Uretimlerin Hammadde Tuketimi</h2>
            <div class="scroll"><table>
            <tr>
            <th>Emir</th><th>Urun</th><th>Hammadde</th>
            <th>Planlanan</th><th>Gerceklesen</th><th>Islem</th>
            </tr>
            """);

        using (var rawCmd = Command(db, """
            SELECT o.Id,o.ProductName,m.Id,m.MaterialName,
                   m.PlannedKg,COALESCE(c.ActualQuantity,'')
            FROM MCProductionPlanningOrders o
            JOIN MCProductionPlanningOrderMaterials m
                ON m.OrderId=o.Id
            LEFT JOIN MCProductionActualConsumption c
                ON c.OrderId=o.Id AND c.Kind='HAMMADDE'
               AND c.SourceLineId=m.Id
            WHERE o.Status='TAMAMLANDI'
            ORDER BY o.Id DESC,m.Id
            LIMIT 1000
            """))
        using (var rawReader = rawCmd.ExecuteReader())
        {
            while (rawReader.Read())
            {
                var orderId = rawReader.GetInt64(0);
                var lineId = rawReader.GetInt64(2);
                var recorded = rawReader.GetString(5);

                html.Append("<tr><td>#");
                html.Append(orderId);
                html.Append("</td><td>");
                html.Append(H(rawReader.GetString(1)));
                html.Append("</td><td>");
                html.Append(H(rawReader.GetString(3)));
                html.Append("</td><td>");
                html.Append(H(rawReader.GetString(4)));
                html.Append(" kg</td><td>");
                html.Append(H(recorded));
                html.Append("</td><td>");

                if (recorded.Length == 0)
                {
                    html.Append("<form method='post' action='/MCProductionPlanning/recordConsumption'>");
                    html.Append(Token());
                    html.Append("<input type='hidden' name='kind' value='HAMMADDE'>");
                    html.Append("<input type='hidden' name='orderId' value='");
                    html.Append(orderId);
                    html.Append("'><input type='hidden' name='sourceLineId' value='");
                    html.Append(lineId);
                    html.Append("'><input name='actualQuantity' value='");
                    html.Append(H(rawReader.GetString(4)));
                    html.Append("' required><button>Kaydet</button></form>");
                }
                else
                {
                    html.Append("Kaydedildi");
                }

                html.Append("</td></tr>");
            }
        }

        html.Append("""
            </table></div></section>
            <section>
            <h2>4. Tamamlanan Uretimlerin Ambalaj Tuketimi</h2>
            <div class="scroll"><table>
            <tr>
            <th>Emir</th><th>Urun</th><th>Ambalaj</th>
            <th>Planlanan</th><th>Gerceklesen</th><th>Islem</th>
            </tr>
            """);

        using (var packageCmd = Command(db, """
            SELECT o.Id,o.ProductName,p.Id,p.PackagingName,
                   p.QuantityPerKg,p.Unit,o.PlannedKg,
                   COALESCE(c.ActualQuantity,''),
                   COALESCE(c.PlannedQuantity,'')
            FROM MCProductionPlanningOrders o
            JOIN MCProductionPackagingRecipeLines p
                ON p.RecipeId=o.RecipeId
            LEFT JOIN MCProductionActualConsumption c
                ON c.OrderId=o.Id AND c.Kind='AMBALAJ'
               AND c.SourceLineId=p.Id
            WHERE o.Status='TAMAMLANDI'
            ORDER BY o.Id DESC,p.Id
            LIMIT 1000
            """))
        using (var packageReader = packageCmd.ExecuteReader())
        {
            while (packageReader.Read())
            {
                var orderId = packageReader.GetInt64(0);
                var lineId = packageReader.GetInt64(2);
                var recorded = packageReader.GetString(7);

                var rateOk = ConsumptionQuantity(
                    packageReader.GetString(4), false, out var perKg);

                var orderOk = ConsumptionQuantity(
                    packageReader.GetString(6), false, out var plannedKg);

                if (!rateOk || !orderOk)
                    continue;

                var planned = ConsumptionNumber(perKg * plannedKg);

                if (recorded.Length > 0)
                    planned = packageReader.GetString(8);

                html.Append("<tr><td>#");
                html.Append(orderId);
                html.Append("</td><td>");
                html.Append(H(packageReader.GetString(1)));
                html.Append("</td><td>");
                html.Append(H(packageReader.GetString(3)));
                html.Append("</td><td>");
                html.Append(H(planned));
                html.Append(" ");
                html.Append(H(packageReader.GetString(5)));
                html.Append("</td><td>");
                html.Append(H(recorded));
                html.Append("</td><td>");

                if (recorded.Length == 0)
                {
                    html.Append("<form method='post' action='/MCProductionPlanning/recordConsumption'>");
                    html.Append(Token());
                    html.Append("<input type='hidden' name='kind' value='AMBALAJ'>");
                    html.Append("<input type='hidden' name='orderId' value='");
                    html.Append(orderId);
                    html.Append("'><input type='hidden' name='sourceLineId' value='");
                    html.Append(lineId);
                    html.Append("'><input name='actualQuantity' value='");
                    html.Append(H(planned));
                    html.Append("' required><button>Kaydet</button></form>");
                }
                else
                {
                    html.Append("Kaydedildi");
                }

                html.Append("</td></tr>");
            }
        }

        html.Append("</table></div></section></main></body></html>");

        return Content(html.ToString(), "text/html; charset=utf-8");
    }

    [HttpPost("addPackaging")]
    [ValidateAntiForgeryToken]
    public IActionResult AddProductionPackaging(
        long recipeId,string packagingName,
        string quantityPerKg,string unit)
    {
        packagingName = (packagingName ?? "").Trim();
        unit = (unit ?? "").Trim();

        if (recipeId <= 0 ||
            packagingName.Length == 0 ||
            packagingName.Length > 150 ||
            (unit != "Adet" && unit != "Kg" && unit != "Metre") ||
            !ConsumptionQuantity(quantityPerKg,false,out var quantity))
            return ErrorPage("Ambalaj bilgileri gecersiz.");

        using var db = OpenConsumption();

        using var check = Command(db, """
            SELECT COUNT(*)
            FROM MCProductionPlanningRecipes
            WHERE Id=$id AND IsActive=1
            """,("$id",recipeId));

        if (Convert.ToInt64(check.ExecuteScalar()) != 1)
            return ErrorPage("Aktif recete bulunamadi.");

        using var save = Command(db, """
            INSERT INTO MCProductionPackagingRecipeLines
                (RecipeId,PackagingName,QuantityPerKg,Unit)
            VALUES($recipe,$name,$qty,$unit)
            """,
            ("$recipe",recipeId),
            ("$name",packagingName),
            ("$qty",ConsumptionNumber(quantity)),
            ("$unit",unit));

        save.ExecuteNonQuery();

        return Redirect("/MCProductionPlanning/consumption");
    }

    [HttpPost("removePackaging")]
    [ValidateAntiForgeryToken]
    public IActionResult RemoveProductionPackaging(long lineId)
    {
        if (lineId <= 0)
            return ErrorPage("Ambalaj satiri gecersiz.");

        using var db = OpenConsumption();

        using var command = Command(db, """
            DELETE FROM MCProductionPackagingRecipeLines
            WHERE Id=$id
              AND RecipeId IN
              (
                  SELECT Id FROM MCProductionPlanningRecipes
                  WHERE IsActive=1
              )
              AND NOT EXISTS
              (
                  SELECT 1 FROM MCProductionActualConsumption c
                  WHERE c.Kind='AMBALAJ'
                    AND c.SourceLineId=MCProductionPackagingRecipeLines.Id
              )
            """,("$id",lineId));

        if (command.ExecuteNonQuery() != 1)
            return ErrorPage("Ambalaj satiri bulunamadi veya tuketim kaydi var.");

        return Redirect("/MCProductionPlanning/consumption");
    }

    [HttpPost("recordConsumption")]
    [ValidateAntiForgeryToken]
    public IActionResult RecordProductionConsumption(
        long orderId,string kind,long sourceLineId,
        string actualQuantity)
    {
        if (orderId <= 0 ||
            sourceLineId <= 0 ||
            (kind != "HAMMADDE" && kind != "AMBALAJ") ||
            !ConsumptionQuantity(actualQuantity,true,out var actual))
            return ErrorPage("Tuketim kaydi gecersiz.");

        using var db = OpenConsumption();
        using var tx = db.BeginTransaction();

        using (var statusCmd = Command(db, """
            SELECT COUNT(*)
            FROM MCProductionPlanningOrders
            WHERE Id=$id AND Status='TAMAMLANDI'
            """,("$id",orderId)))
        {
            statusCmd.Transaction = tx;

            if (Convert.ToInt64(statusCmd.ExecuteScalar()) != 1)
                return ErrorPage("Yalnizca tamamlanan uretime tuketim kaydedilebilir.");
        }

        string itemName;
        string plannedQuantity;
        string unit;

        if (kind == "HAMMADDE")
        {
            using var materialCmd = Command(db, """
                SELECT MaterialName,PlannedKg
                FROM MCProductionPlanningOrderMaterials
                WHERE Id=$line AND OrderId=$order
                """,
                ("$line",sourceLineId),
                ("$order",orderId));

            materialCmd.Transaction = tx;

            using var materialReader = materialCmd.ExecuteReader();

            if (!materialReader.Read())
                return ErrorPage("Uretim emrine ait hammadde bulunamadi.");

            itemName = materialReader.GetString(0);
            plannedQuantity = materialReader.GetString(1);
            unit = "Kg";
        }
        else
        {
            using var packageCmd = Command(db, """
                SELECT p.PackagingName,p.QuantityPerKg,p.Unit,o.PlannedKg
                FROM MCProductionPackagingRecipeLines p
                JOIN MCProductionPlanningOrders o ON o.RecipeId=p.RecipeId
                WHERE p.Id=$line AND o.Id=$order
                """,
                ("$line",sourceLineId),
                ("$order",orderId));

            packageCmd.Transaction = tx;

            using var packageReader = packageCmd.ExecuteReader();

            if (!packageReader.Read())
                return ErrorPage("Uretim emrine ait ambalaj bulunamadi.");

            itemName = packageReader.GetString(0);
            unit = packageReader.GetString(2);

            if (!ConsumptionQuantity(
                    packageReader.GetString(1),false,out var perKg) ||
                !ConsumptionQuantity(
                    packageReader.GetString(3),false,out var orderKg))
                return ErrorPage("Ambalaj recete miktari gecersiz.");

            plannedQuantity = ConsumptionNumber(perKg * orderKg);
        }

        using (var check = Command(db, """
            SELECT COUNT(*)
            FROM MCProductionActualConsumption
            WHERE OrderId=$order AND Kind=$kind AND SourceLineId=$line
            """,
            ("$order",orderId),
            ("$kind",kind),
            ("$line",sourceLineId)))
        {
            check.Transaction = tx;

            if (Convert.ToInt64(check.ExecuteScalar()) != 0)
                return ErrorPage("Bu tuketim daha once kaydedilmis.");
        }

        using (var save = Command(db, """
            INSERT INTO MCProductionActualConsumption
                (OrderId,Kind,SourceLineId,ItemName,
                 PlannedQuantity,ActualQuantity,Unit)
            VALUES($order,$kind,$line,$name,$planned,$actual,$unit)
            """,
            ("$order",orderId),
            ("$kind",kind),
            ("$line",sourceLineId),
            ("$name",itemName),
            ("$planned",plannedQuantity),
            ("$actual",ConsumptionNumber(actual)),
            ("$unit",unit)))
        {
            save.Transaction = tx;
            save.ExecuteNonQuery();
        }

        tx.Commit();

        return Redirect("/MCProductionPlanning/consumption");
    }
}