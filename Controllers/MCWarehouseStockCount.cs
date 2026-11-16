using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

// MC_WAREHOUSE_STOCK_COUNT_V1
public sealed partial class MCProductionPlanningController
{
    private static void EnsureWarehouseCounts(SqliteConnection db)
    {
        using var schema = db.CreateCommand();

        schema.CommandText = """
            CREATE TABLE IF NOT EXISTS MCWarehousePhysicalCounts
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PalletItemId INTEGER NOT NULL,
                SystemUnits TEXT NOT NULL,
                CountedUnits TEXT NOT NULL,
                DifferenceUnits TEXT NOT NULL,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                Notes TEXT NOT NULL DEFAULT ''
            );

            CREATE INDEX IF NOT EXISTS IX_MCWarehouseCounts_Pallet
                ON MCWarehousePhysicalCounts(PalletItemId,Id);
            """;

        schema.ExecuteNonQuery();
    }

    private static bool WarehouseCountQuantity(
        string? input,
        out long quantity)
    {
        quantity = 0;

        input = (input ?? "").Trim();

        return System.Text.RegularExpressions.Regex.IsMatch(
                   input, @"^\d{1,10}$") &&
               long.TryParse(
                   input,
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out quantity) &&
               quantity <= 1000000000;
    }

    private static string WarehouseNumber(decimal value)
    {
        return value.ToString(
            "0.###",
            CultureInfo.InvariantCulture);
    }

    private static List<(long Id, decimal Units, bool Valid)>
        WarehouseBalances(SqliteConnection db)
    {
        var result =
            new List<(long Id,decimal Units,bool Valid)>();

        using var cmd = db.CreateCommand();

        cmd.CommandText = """
            SELECT
                PalletItemId,
                Type,
                BoxCount,
                LooseUnitCount,
                UnitsPerBox
            FROM StockMovements
            WHERE PalletItemId IS NOT NULL
            ORDER BY PalletItemId,Id
            """;

        var totals =
            new Dictionary<long,(decimal Units,bool Valid)>();

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            long id;
            long type;
            decimal boxes;
            decimal loose;
            decimal perBox;

            try
            {
                id = Convert.ToInt64(reader.GetValue(0));
                type = Convert.ToInt64(reader.GetValue(1));

                boxes = reader.IsDBNull(2)
                    ? -1m
                    : Convert.ToDecimal(reader.GetValue(2));

                loose = reader.IsDBNull(3)
                    ? -1m
                    : Convert.ToDecimal(reader.GetValue(3));

                perBox = reader.IsDBNull(4)
                    ? 0m
                    : Convert.ToDecimal(reader.GetValue(4));
            }
            catch
            {
                continue;
            }

            if (id <= 0)
                continue;

            var valid =
                boxes >= 0m &&
                loose >= 0m &&
                (boxes == 0m || perBox > 0m) &&
                type >= 1 &&
                type <= 6;

            var movementUnits = 0m;

            if (valid)
            {
                try
                {
                    movementUnits =
                        checked(boxes * perBox + loose);
                }
                catch
                {
                    valid = false;
                }
            }

            var sign = type switch
            {
                1 => 1,
                2 => -1,
                3 => 1,
                4 => -1,
                5 => -1,
                6 => 1,
                _ => 0
            };

            if (!totals.TryGetValue(id,out var current))
                current = (0m,true);

            try
            {
                totals[id] = (
                    checked(current.Units +
                            movementUnits * sign),
                    current.Valid && valid
                );
            }
            catch
            {
                totals[id] = (current.Units,false);
            }
        }

        foreach (var entry in totals)
        {
            result.Add((
                entry.Key,
                entry.Value.Units,
                entry.Value.Valid &&
                entry.Value.Units >= 0m
            ));
        }

        result.Sort((a,b) => b.Id.CompareTo(a.Id));

        return result;
    }

    [HttpGet("warehouseCount")]
    public IActionResult WarehouseCount()
    {
        using var db = Open();

        EnsureWarehouseCounts(db);

        var balances = WarehouseBalances(db);
        var html = new StringBuilder();

        html.Append("""
            <!doctype html>
            <html lang="tr">
            <head>
            <meta charset="utf-8">
            <meta name="viewport"
                  content="width=device-width,initial-scale=1">
            <title>MASTER CHEF - Depo Stok Sayimi</title>
            <style>
            body{font-family:Arial;background:#f1f5f9;
                 color:#172033;padding:24px}
            main{max-width:1250px;margin:auto}
            section{background:white;padding:20px;
                    margin:18px 0;border-radius:10px}
            table{width:100%;border-collapse:collapse}
            th,td{padding:10px;text-align:left;
                  border-bottom:1px solid #ddd}
            th{background:#edf3f8}
            input{padding:9px;border:1px solid #cbd5e1;
                  border-radius:5px}
            button{background:#24496c;color:white;
                   border:0;padding:10px;border-radius:5px}
            .scroll{overflow-x:auto}
            .ok{color:#166534;font-weight:bold}
            .warn{color:#b45309;font-weight:bold}
            .note{background:#fff7ed;padding:12px;
                  border-radius:7px}
            </style>
            </head>
            <body><main>
            <h1>MASTER CHEF - Fiziksel Stok Sayimi</h1>
            <p>
            <a href="/MCProductionPlanning/warehouseDispatch">
            Depo ve Sevkiyat</a>
            </p>
            <p class="note">
            Palet kalemi bazinda sayim yapilir.
            Sayim kaydi gercek stok hareketi olusturmaz.
            Sistem adedi mevcut hareket turlerinin
            toplami esas alinarak hesaplanir.
            </p>
            <section>
            <h2>Palet Kalemi Stok Sayimi</h2>
            <div class="scroll"><table>
            <tr>
            <th>Palet Kalemi ID</th>
            <th>Sistem Adedi</th>
            <th>Durum</th>
            <th>Fiziksel Sayim</th>
            </tr>
            """);

        var shown = 0;

        foreach (var item in balances)
        {
            if (shown >= 300)
                break;

            shown++;

            html.Append("<tr><td>#");
            html.Append(item.Id);
            html.Append("</td><td>");
            html.Append(WarehouseNumber(item.Units));
            html.Append("</td><td>");

            if (!item.Valid)
            {
                html.Append(
                    "<span class='warn'>STOK HAREKETLERI KONTROL EDILMELI</span>");
                html.Append("</td><td>Sayim kaydi kapali</td></tr>");
                continue;
            }

            html.Append("<span class='ok'>HESAPLANDI</span>");
            html.Append("</td><td>");

            html.Append(
                "<form method='post' action='/MCProductionPlanning/saveWarehouseCount'>");

            html.Append(Token());

            html.Append(
                "<input type='hidden' name='palletItemId' value='");

            html.Append(item.Id);

            html.Append("'>");

            html.Append(
                "<input name='countedUnits' inputmode='numeric' ");

            html.Append(
                "placeholder='Sayilan adet' required> ");

            html.Append(
                "<input name='notes' maxlength='200' ");

            html.Append(
                "placeholder='Aciklama'> ");

            html.Append(
                "<button type='submit'>Sayimi Kaydet</button>");

            html.Append("</form></td></tr>");
        }

        html.Append("</table></div>");

        if (balances.Count > 300)
        {
            html.Append(
                "<p>Ilk 300 palet kalemi gosteriliyor.</p>");
        }

        html.Append("</section>");

        html.Append("""
            <section>
            <h2>Kaydedilen Sayimlar ve Farklar</h2>
            <div class="scroll"><table>
            <tr>
            <th>Kayit</th>
            <th>Palet Kalemi</th>
            <th>Sistem Adedi</th>
            <th>Sayilan</th>
            <th>Fark</th>
            <th>Tarih</th>
            <th>Aciklama</th>
            </tr>
            """);

        using (var history = Command(db, """
            SELECT
                Id,PalletItemId,SystemUnits,
                CountedUnits,DifferenceUnits,
                CreatedAt,Notes
            FROM MCWarehousePhysicalCounts
            ORDER BY Id DESC
            LIMIT 300
            """))
        using (var historyReader = history.ExecuteReader())
        {
            while (historyReader.Read())
            {
                html.Append("<tr>");

                for (var i = 0; i < 7; i++)
                {
                    html.Append("<td>");
                    html.Append(
                        H(historyReader.GetValue(i)));
                    html.Append("</td>");
                }

                html.Append("</tr>");
            }
        }

        html.Append("""
            </table></div>
            <p class="note">
            Fark = Fiziksel sayim - Sayim anindaki
            sistem adedi. Bu kayitlar stok duzeltmesi
            olarak uygulanmaz.
            </p>
            </section>
            </main></body></html>
            """);

        return Content(
            html.ToString(),
            "text/html; charset=utf-8");
    }

    [HttpPost("saveWarehouseCount")]
    [ValidateAntiForgeryToken]
    public IActionResult SaveWarehouseCount(
        long palletItemId,
        string countedUnits,
        string? notes)
    {
        if (palletItemId <= 0 ||
            !WarehouseCountQuantity(
                countedUnits,out var physical))
        {
            return ErrorPage(
                "Sayim miktari gecersiz.");
        }

        notes = (notes ?? "").Trim();

        if (notes.Length > 200)
            return ErrorPage(
                "Aciklama en fazla 200 karakter olabilir.");

        using var db = Open();

        EnsureWarehouseCounts(db);

        using var tx = db.BeginTransaction();

        var current = WarehouseBalances(db);

        var matched = current.Find(
            x => x.Id == palletItemId);

        if (matched.Id != palletItemId ||
            !matched.Valid)
        {
            return ErrorPage(
                "Palet kalemi bulunamadi veya " +
                "stok hareketlerinde hata var.");
        }

        var difference =
            (decimal)physical - matched.Units;

        using var save = Command(db, """
            INSERT INTO MCWarehousePhysicalCounts
                (PalletItemId,SystemUnits,
                 CountedUnits,DifferenceUnits,Notes)
            VALUES($pallet,$system,$physical,$difference,$notes)
            """,
            ("$pallet",palletItemId),
            ("$system",WarehouseNumber(matched.Units)),
            ("$physical",physical.ToString(
                CultureInfo.InvariantCulture)),
            ("$difference",WarehouseNumber(difference)),
            ("$notes",notes));

        save.Transaction = tx;

        save.ExecuteNonQuery();

        tx.Commit();

        return Redirect("/MCProductionPlanning/warehouseCount");
    }
}