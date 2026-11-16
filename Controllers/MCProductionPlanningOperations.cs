using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

// MC_PRODUCTION_OPERATIONS_PARTIAL_V1
public sealed partial class MCProductionPlanningController
{
    private SqliteConnection OpenOperations()
    {
        var db = Open();

        try
        {
            using var cmd = db.CreateCommand();

            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS MCProductionPlanningOrderResults (
                    OrderId INTEGER PRIMARY KEY,
                    StartedAt TEXT NOT NULL,
                    FinishedAt TEXT,
                    ActualKg TEXT,
                    ScrapKg TEXT,
                    FOREIGN KEY(OrderId)
                        REFERENCES MCProductionPlanningOrders(Id)
                );

                CREATE TABLE IF NOT EXISTS MCProductionPlanningCardLinks (
                    Kind TEXT NOT NULL,
                    RecordId INTEGER NOT NULL,
                    CardTable TEXT NOT NULL,
                    CardId INTEGER NOT NULL,
                    CardLabel TEXT NOT NULL,
                    PRIMARY KEY (Kind,RecordId)
                );
                """;

            cmd.ExecuteNonQuery();
            return db;
        }
        catch
        {
            db.Dispose();
            throw;
        }
    }

    private static bool ValidProductionKg(
        string? text, bool allowZero, out decimal value)
    {
        value = 0m;
        text = (text ?? "").Trim();

        if (!Regex.IsMatch(text, @"^\d{1,9}([.,]\d{1,3})?$"))
            return false;

        if (!decimal.TryParse(
            text.Replace(",", "."),
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out value))
            return false;

        return value <= 1000000m &&
               (allowZero ? value >= 0m : value > 0m);
    }

    private static List<(string Table,long Id,string Name)> GetCards(
        SqliteConnection db, bool material)
    {
        var result = new List<(string Table,long Id,string Name)>();
        var tables = new List<string>();

        using (var tableCmd = db.CreateCommand())
        {
            tableCmd.CommandText =
                "SELECT name FROM sqlite_master WHERE type='table'";

            using var tableReader = tableCmd.ExecuteReader();

            while (tableReader.Read())
            {
                var tableName = tableReader.GetString(0);

                if (!Regex.IsMatch(tableName, @"^[A-Za-z_][A-Za-z0-9_]*$"))
                    continue;

                var wanted = material
                    ? @"RawMaterial|Hammadde|MaterialCard|StockItem|StokKart"
                    : @"Product|UrunKart|Mamul|FinishedGood|StockItem|StokKart";

                var excluded =
                    @"Movement|Invoice|Receipt|Order|Audit|Log|Transfer|" +
                    @"Recipe|Planning|Posting|Transaction|Batch|Line|History";

                if (!Regex.IsMatch(
                        tableName, wanted, RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(
                        tableName, excluded, RegexOptions.IgnoreCase))
                    continue;

                tables.Add(tableName);
            }
        }

        foreach (var tableName in tables)
        {
            var columns = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            using (var columnCmd = db.CreateCommand())
            {
                columnCmd.CommandText =
                    "PRAGMA table_info(\"" + tableName + "\")";

                using var columnReader = columnCmd.ExecuteReader();

                while (columnReader.Read())
                    columns.Add(columnReader.GetString(1));
            }

            if (!columns.Contains("Id"))
                continue;

            var nameColumn = new[]
            {
                "Name","ProductName","MaterialName",
                "Title","Code","ProductCode",
                "MaterialCode","StockCode"
            }.FirstOrDefault(columns.Contains);

            if (nameColumn == null)
                continue;

            using var cardCmd = db.CreateCommand();

            cardCmd.CommandText =
                "SELECT Id, CAST(\"" + nameColumn +
                "\" AS TEXT) FROM \"" + tableName +
                "\" WHERE Id IS NOT NULL ORDER BY Id DESC LIMIT 300";

            using var cardReader = cardCmd.ExecuteReader();

            while (cardReader.Read())
            {
                if (cardReader.IsDBNull(0))
                    continue;

                if (!long.TryParse(
                    Convert.ToString(cardReader.GetValue(0)),
                    out var id))
                    continue;

                var name = cardReader.IsDBNull(1)
                    ? ""
                    : Convert.ToString(cardReader.GetValue(1)) ?? "";

                result.Add((tableName,id,name));
            }
        }

        return result;
    }

    private void AddCardForm(
        StringBuilder html,
        string kind,
        long recordId,
        List<(string Table,long Id,string Name)> cards)
    {
        if (cards.Count == 0)
        {
            html.Append("Uygun kart bulunamadi.");
            return;
        }

        html.Append("<form method='post' action='/MCProductionPlanning/linkCard'>");
        html.Append(Token());

        html.Append("<input type='hidden' name='kind' value='");
        html.Append(H(kind));

        html.Append("'><input type='hidden' name='recordId' value='");
        html.Append(recordId);

        html.Append("'><select name='cardKey' required>");
        html.Append("<option value=''>Kart seciniz</option>");

        foreach (var card in cards)
        {
            html.Append("<option value='");
            html.Append(H(card.Table + "#" + card.Id));
            html.Append("'>");
            html.Append(H(card.Name));
            html.Append(" (#");
            html.Append(card.Id);
            html.Append(" - ");
            html.Append(H(card.Table));
            html.Append(")</option>");
        }

        html.Append("</select> <button>Bagla</button></form>");
    }

    [HttpGet("operations")]
    public IActionResult Operations()
    {
        using var db = OpenOperations();

        var products = GetCards(db,false);
        var materials = GetCards(db,true);
        var html = new StringBuilder();

        html.Append("""
            <!doctype html>
            <html lang="tr">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <title>MASTER CHEF - Uretim Operasyonlari</title>
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
            </style>
            </head>
            <body><main>
            <h1>MASTER CHEF - Uretim Operasyonlari</h1>
            <p><a href="/MCProductionPlanning">Uretim Planlamaya Don</a></p>
            <p>Uretim emirleri, gerceklesen miktarlar, fire ve kart baglantilari.</p>
            <p><strong>Bu bolum stok hareketi olusturmaz.</strong></p>
            <section>
            <h2>Uretim Emirleri</h2>
            <div class="scroll"><table>
            <tr>
            <th>Emir</th><th>Urun</th><th>Planlanan</th>
            <th>Durum</th><th>Gerceklesen / Fire</th><th>Islem</th>
            </tr>
            """);

        using (var orderCmd = Command(db, """
            SELECT o.Id,o.ProductName,o.PlannedKg,o.Status,
                   COALESCE(r.ActualKg,''),
                   COALESCE(r.ScrapKg,'')
            FROM MCProductionPlanningOrders o
            LEFT JOIN MCProductionPlanningOrderResults r
                ON r.OrderId=o.Id
            ORDER BY o.Id DESC LIMIT 200
            """))
        using (var orderReader = orderCmd.ExecuteReader())
        {
            while (orderReader.Read())
            {
                var id = orderReader.GetInt64(0);
                var status = orderReader.GetString(3);

                html.Append("<tr><td>#");
                html.Append(id);
                html.Append("</td><td>");
                html.Append(H(orderReader.GetString(1)));
                html.Append("</td><td>");
                html.Append(H(orderReader.GetString(2)));
                html.Append(" kg</td><td>");
                html.Append(H(status));
                html.Append("</td><td>");

                if (status == "TAMAMLANDI")
                {
                    html.Append("Uretim: ");
                    html.Append(H(orderReader.GetString(4)));
                    html.Append(" kg / Fire: ");
                    html.Append(H(orderReader.GetString(5)));
                    html.Append(" kg");
                }
                else
                {
                    html.Append("-");
                }

                html.Append("</td><td>");

                if (status == "PLANLANDI")
                {
                    html.Append("<form method='post' action='/MCProductionPlanning/start'>");
                    html.Append(Token());
                    html.Append("<input type='hidden' name='orderId' value='");
                    html.Append(id);
                    html.Append("'><button>Baslat</button></form>");
                }
                else if (status == "URETIMDE")
                {
                    html.Append("<form method='post' action='/MCProductionPlanning/finish'>");
                    html.Append(Token());
                    html.Append("<input type='hidden' name='orderId' value='");
                    html.Append(id);
                    html.Append("'>");
                    html.Append("<input name='actualKg' placeholder='Gerceklesen kg' required>");
                    html.Append("<input name='scrapKg' value='0' placeholder='Fire kg' required>");
                    html.Append("<button>Tamamla</button></form>");
                }
                else
                {
                    html.Append("-");
                }

                html.Append("</td></tr>");
            }
        }

        html.Append("</table></div></section>");
        html.Append("<section><h2>Urun Karti Baglantilari</h2>");
        html.Append("<div class='scroll'><table>");
        html.Append("<tr><th>Recete</th><th>Bagli Kart</th><th>Islem</th></tr>");

        using (var productCmd = Command(db, """
            SELECT r.Id,r.ProductName,COALESCE(l.CardLabel,'')
            FROM MCProductionPlanningRecipes r
            LEFT JOIN MCProductionPlanningCardLinks l
                ON l.Kind='product' AND l.RecordId=r.Id
            WHERE r.IsActive=1
            ORDER BY r.Id DESC
            """))
        using (var productReader = productCmd.ExecuteReader())
        {
            while (productReader.Read())
            {
                var id = productReader.GetInt64(0);

                html.Append("<tr><td>#");
                html.Append(id);
                html.Append(" ");
                html.Append(H(productReader.GetString(1)));
                html.Append("</td><td>");
                html.Append(H(productReader.GetString(2)));
                html.Append("</td><td>");

                AddCardForm(html,"product",id,products);

                html.Append("</td></tr>");
            }
        }

        html.Append("</table></div></section>");
        html.Append("<section><h2>Hammadde Karti Baglantilari</h2>");
        html.Append("<div class='scroll'><table>");
        html.Append("<tr><th>Recete / Hammadde</th><th>Bagli Kart</th><th>Islem</th></tr>");

        using (var materialCmd = Command(db, """
            SELECT l.Id,r.ProductName,l.MaterialName,
                   COALESCE(k.CardLabel,'')
            FROM MCProductionPlanningRecipeLines l
            JOIN MCProductionPlanningRecipes r ON r.Id=l.RecipeId
            LEFT JOIN MCProductionPlanningCardLinks k
                ON k.Kind='material' AND k.RecordId=l.Id
            WHERE r.IsActive=1
            ORDER BY r.Id DESC,l.Id
            """))
        using (var materialReader = materialCmd.ExecuteReader())
        {
            while (materialReader.Read())
            {
                var id = materialReader.GetInt64(0);

                html.Append("<tr><td>");
                html.Append(H(materialReader.GetString(1)));
                html.Append(" / ");
                html.Append(H(materialReader.GetString(2)));
                html.Append("</td><td>");
                html.Append(H(materialReader.GetString(3)));
                html.Append("</td><td>");

                AddCardForm(html,"material",id,materials);

                html.Append("</td></tr>");
            }
        }

        html.Append("</table></div></section></main></body></html>");

        return Content(html.ToString(),"text/html; charset=utf-8");
    }

    [HttpPost("start")]
    [ValidateAntiForgeryToken]
    public IActionResult StartProduction(long orderId)
    {
        if (orderId <= 0)
            return ErrorPage("Gecersiz uretim emri.");

        using var db = OpenOperations();
        using var tx = db.BeginTransaction();

        using (var cmd = Command(db, """
            UPDATE MCProductionPlanningOrders
            SET Status='URETIMDE'
            WHERE Id=$id AND Status='PLANLANDI'
            """,("$id",orderId)))
        {
            cmd.Transaction = tx;

            if (cmd.ExecuteNonQuery() != 1)
                return ErrorPage("Yalnizca planlanan emir baslatilabilir.");
        }

        using (var cmd = Command(db, """
            INSERT INTO MCProductionPlanningOrderResults(OrderId,StartedAt)
            VALUES($id,datetime('now'))
            """,("$id",orderId)))
        {
            cmd.Transaction = tx;
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
        return Redirect("/MCProductionPlanning/operations");
    }

    [HttpPost("finish")]
    [ValidateAntiForgeryToken]
    public IActionResult FinishProduction(
        long orderId,string actualKg,string scrapKg)
    {
        if (orderId <= 0 ||
            !ValidProductionKg(actualKg,false,out var actual) ||
            !ValidProductionKg(scrapKg,true,out var scrap))
            return ErrorPage("Uretim veya fire miktari gecersiz.");

        using var db = OpenOperations();
        using var tx = db.BeginTransaction();

        using (var cmd = Command(db, """
            UPDATE MCProductionPlanningOrders
            SET Status='TAMAMLANDI'
            WHERE Id=$id AND Status='URETIMDE'
            """,("$id",orderId)))
        {
            cmd.Transaction = tx;

            if (cmd.ExecuteNonQuery() != 1)
                return ErrorPage("Yalnizca uretimdeki emir tamamlanabilir.");
        }

        using (var cmd = Command(db, """
            UPDATE MCProductionPlanningOrderResults
            SET FinishedAt=datetime('now'),
                ActualKg=$actual,
                ScrapKg=$scrap
            WHERE OrderId=$id AND FinishedAt IS NULL
            """,
            ("$id",orderId),
            ("$actual",N(actual)),
            ("$scrap",N(scrap))))
        {
            cmd.Transaction = tx;

            if (cmd.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("Uretim sonucu kaydedilemedi.");
        }

        tx.Commit();
        return Redirect("/MCProductionPlanning/operations");
    }

    [HttpPost("linkCard")]
    [ValidateAntiForgeryToken]
    public IActionResult LinkProductionCard(
        string kind,long recordId,string cardKey)
    {
        if ((kind != "product" && kind != "material") ||
            recordId <= 0 ||
            string.IsNullOrWhiteSpace(cardKey))
            return ErrorPage("Kart baglantisi gecersiz.");

        var separator = cardKey.LastIndexOf('#');

        if (separator <= 0 ||
            !long.TryParse(
                cardKey.Substring(separator + 1),out var cardId))
            return ErrorPage("Kart secimi gecersiz.");

        var table = cardKey.Substring(0,separator);

        using var db = OpenOperations();

        var cards = GetCards(db,kind == "material");

        var selected = cards.FirstOrDefault(
            x => x.Table == table && x.Id == cardId);

        if (selected.Table != table || selected.Id != cardId)
            return ErrorPage("Secilen kart bulunamadi.");

        var query = kind == "product"
            ? "SELECT COUNT(*) FROM MCProductionPlanningRecipes WHERE Id=$id AND IsActive=1"
            : """
              SELECT COUNT(*)
              FROM MCProductionPlanningRecipeLines l
              JOIN MCProductionPlanningRecipes r ON r.Id=l.RecipeId
              WHERE l.Id=$id AND r.IsActive=1
              """;

        using (var check = Command(db,query,("$id",recordId)))
        {
            if (Convert.ToInt64(check.ExecuteScalar()) != 1)
                return ErrorPage("Aktif recete veya hammadde bulunamadi.");
        }

        using var save = Command(db, """
            INSERT INTO MCProductionPlanningCardLinks
                (Kind,RecordId,CardTable,CardId,CardLabel)
            VALUES($kind,$record,$table,$card,$label)
            ON CONFLICT(Kind,RecordId) DO UPDATE SET
                CardTable=excluded.CardTable,
                CardId=excluded.CardId,
                CardLabel=excluded.CardLabel
            """,
            ("$kind",kind),
            ("$record",recordId),
            ("$table",table),
            ("$card",cardId),
            ("$label",selected.Name));

        save.ExecuteNonQuery();

        return Redirect("/MCProductionPlanning/operations");
    }
}