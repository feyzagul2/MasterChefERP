using System;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

// MC_FINISHED_GOODS_V1
public sealed partial class MCProductionPlanningController
{
    private SqliteConnection OpenFinishedGoods()
    {
        var db = OpenOperations();

        try
        {
            using var schema = db.CreateCommand();

            schema.CommandText = """
                CREATE TABLE IF NOT EXISTS MCProductionFinishedGoodsReceipts (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    OrderId INTEGER NOT NULL UNIQUE,
                    RecipeId INTEGER NOT NULL,
                    ProductName TEXT NOT NULL,
                    CardTable TEXT NOT NULL,
                    CardId INTEGER NOT NULL,
                    CardLabel TEXT NOT NULL,
                    ActualKg TEXT NOT NULL,
                    ScrapKg TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                    FOREIGN KEY(OrderId)
                        REFERENCES MCProductionPlanningOrders(Id)
                );

                CREATE INDEX IF NOT EXISTS IX_MCFinishedGoods_Card
                    ON MCProductionFinishedGoodsReceipts(CardTable,CardId);
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

    [HttpGet("finishedGoods")]
    public IActionResult FinishedGoods()
    {
        using var db = OpenFinishedGoods();
        var html = new StringBuilder();

        html.Append("""
            <!doctype html>
            <html lang="tr">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <title>MASTER CHEF - Mamul Girisleri</title>
            <style>
            body{font-family:Arial;background:#f1f5f9;color:#172033;padding:24px}
            main{max-width:1250px;margin:auto}
            section{background:white;padding:20px;margin:18px 0;border-radius:10px}
            table{width:100%;border-collapse:collapse}
            th,td{padding:11px;text-align:left;border-bottom:1px solid #ddd}
            th{background:#edf3f8}
            button{background:#24496c;color:white;padding:10px 15px;
                   border:0;border-radius:5px;cursor:pointer}
            .warn{background:#fff7ed;padding:12px;border-radius:7px}
            .scroll{overflow-x:auto}
            </style>
            </head>
            <body><main>
            <h1>MASTER CHEF - Mamul Girisleri</h1>
            <p><a href="/MCProductionPlanning">Uretim Planlama</a>
            &nbsp; | &nbsp;
            <a href="/MCProductionPlanning/operations">Uretim Operasyonlari</a>
            &nbsp; | &nbsp;
            <a href="/MCProductionPlanning/consumption">Hammadde ve Ambalaj</a></p>

            <p class="warn">
            Bu ekran uretim kaynakli mamul girislerini kaydeder.
            Ana stok bakiyelerine henuz hareket islemez.
            </p>

            <section>
            <h2>Tamamlanan Uretimler</h2>
            <div class="scroll"><table>
            <tr>
            <th>Emir</th>
            <th>Urun</th>
            <th>Urun Karti</th>
            <th>Gerceklesen</th>
            <th>Fire</th>
            <th>Durum / Islem</th>
            </tr>
            """);

        using (var orderCmd = Command(db, """
            SELECT
                o.Id,
                o.ProductName,
                COALESCE(l.CardLabel,''),
                COALESCE(r.ActualKg,''),
                COALESCE(r.ScrapKg,''),
                CASE WHEN g.Id IS NULL THEN 0 ELSE 1 END,
                COALESCE(l.CardId,0)
            FROM MCProductionPlanningOrders o
            JOIN MCProductionPlanningOrderResults r
                ON r.OrderId=o.Id
            LEFT JOIN MCProductionPlanningCardLinks l
                ON l.Kind='product'
               AND l.RecordId=o.RecipeId
            LEFT JOIN MCProductionFinishedGoodsReceipts g
                ON g.OrderId=o.Id
            WHERE o.Status='TAMAMLANDI'
            ORDER BY o.Id DESC
            LIMIT 500
            """))
        using (var orderReader = orderCmd.ExecuteReader())
        {
            while (orderReader.Read())
            {
                var orderId = orderReader.GetInt64(0);
                var registered = orderReader.GetInt64(5) == 1;
                var cardId = Convert.ToInt64(orderReader.GetValue(6));

                html.Append("<tr><td>#");
                html.Append(orderId);
                html.Append("</td><td>");
                html.Append(H(orderReader.GetString(1)));
                html.Append("</td><td>");
                html.Append(H(orderReader.GetString(2)));
                html.Append("</td><td>");
                html.Append(H(orderReader.GetString(3)));
                html.Append(" kg</td><td>");
                html.Append(H(orderReader.GetString(4)));
                html.Append(" kg</td><td>");

                if (registered)
                {
                    html.Append("KAYDEDILDI");
                }
                else if (cardId <= 0)
                {
                    html.Append("Once urun karti baglanmali");
                }
                else
                {
                    html.Append("<form method='post' action='/MCProductionPlanning/registerFinishedGoods'>");
                    html.Append(Token());
                    html.Append("<input type='hidden' name='orderId' value='");
                    html.Append(orderId);
                    html.Append("'><button type='submit'>Mamul Girisini Kaydet</button>");
                    html.Append("</form>");
                }

                html.Append("</td></tr>");
            }
        }

        html.Append("""
            </table></div></section>
            <section>
            <h2>Kaydedilen Mamul Girisleri</h2>
            <div class="scroll"><table>
            <tr>
            <th>Giris ID</th><th>Emir</th><th>Urun</th>
            <th>Kart</th><th>Miktar</th><th>Tarih</th>
            </tr>
            """);

        using (var receiptCmd = Command(db, """
            SELECT Id,OrderId,ProductName,CardLabel,ActualKg,CreatedAt
            FROM MCProductionFinishedGoodsReceipts
            ORDER BY Id DESC
            LIMIT 500
            """))
        using (var receiptReader = receiptCmd.ExecuteReader())
        {
            while (receiptReader.Read())
            {
                html.Append("<tr><td>");
                html.Append(receiptReader.GetInt64(0));
                html.Append("</td><td>#");
                html.Append(receiptReader.GetInt64(1));
                html.Append("</td><td>");
                html.Append(H(receiptReader.GetString(2)));
                html.Append("</td><td>");
                html.Append(H(receiptReader.GetString(3)));
                html.Append("</td><td>");
                html.Append(H(receiptReader.GetString(4)));
                html.Append(" kg</td><td>");
                html.Append(H(receiptReader.GetString(5)));
                html.Append("</td></tr>");
            }
        }

                // MC_REAL_FINISHED_STOCK_V1
        // Mevcut paketleme kaynakli stok girislerini okur.
        // Yeni stok hareketi olusturmaz.

        html.Append("<section><h2>Gercek Mamul Stok Girisleri</h2>");
        html.Append("<p>Asagidaki kayitlar mevcut stok hareketi tablosundan okunur. ");
        html.Append("Yukaridaki uretim kayitlari tek basina stok bakiyesi degildir.</p>");

        using (var check = db.CreateCommand())
        {
            check.CommandText = """
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type='table' AND name='StockMovements'
                """;

            if (Convert.ToInt64(check.ExecuteScalar()) == 1)
            {
                var required = new[]
                {
                    "Id", "PackagingBatchId", "PalletItemId",
                    "Type", "BoxCount", "LooseUnitCount", "UnitsPerBox"
                };

                var columns = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

                using (var schema = db.CreateCommand())
                {
                    schema.CommandText = "PRAGMA table_info(StockMovements)";

                    using var schemaReader = schema.ExecuteReader();

                    while (schemaReader.Read())
                        columns.Add(schemaReader.GetString(1));
                }

                if (required.All(columns.Contains))
                {
                    html.Append("<div class='scroll'><table>");
                    html.Append("<tr><th>Hareket</th><th>Paketleme Partisi</th>");
                    html.Append("<th>Palet Kalemi</th><th>Koli</th>");
                    html.Append("<th>Acik Adet</th><th>Kolide Adet</th>");
                    html.Append("<th>Toplam Adet</th></tr>");

                    using var stockCmd = db.CreateCommand();

                    stockCmd.CommandText = """
                        SELECT Id, PackagingBatchId, PalletItemId,
                               BoxCount, LooseUnitCount, UnitsPerBox
                        FROM StockMovements
                        WHERE Type = 1
                          AND PackagingBatchId IS NOT NULL
                          AND TRIM(PackagingBatchId) <> ''
                        ORDER BY Id DESC
                        LIMIT 300
                        """;

                    using var stockReader = stockCmd.ExecuteReader();

                    while (stockReader.Read())
                    {
                        long boxes = stockReader.IsDBNull(3)
                            ? 0L : Convert.ToInt64(stockReader.GetValue(3));

                        long loose = stockReader.IsDBNull(4)
                            ? 0L : Convert.ToInt64(stockReader.GetValue(4));

                        long perBox = stockReader.IsDBNull(5)
                            ? 0L : Convert.ToInt64(stockReader.GetValue(5));

                        decimal total = (decimal)boxes * perBox + loose;

                        html.Append("<tr><td>#");
                        html.Append(H(stockReader.GetValue(0)));
                        html.Append("</td><td>");
                        html.Append(H(stockReader.GetValue(1)));
                        html.Append("</td><td>");
                        html.Append(H(stockReader.GetValue(2)));
                        html.Append("</td><td>");
                        html.Append(boxes);
                        html.Append("</td><td>");
                        html.Append(loose);
                        html.Append("</td><td>");
                        html.Append(perBox);
                        html.Append("</td><td>");
                        html.Append(total);
                        html.Append("</td></tr>");
                    }

                    html.Append("</table></div>");
                    html.Append("<p>Bu liste giris hareketlerinin miktarlarini gosterir; ");
                    html.Append("sevk, iade ve iptaller dusulmus guncel bakiye degildir.</p>");
                }
                else
                {
                    html.Append("<p>Stok hareketi alanlari uyusmuyor. ");
                    html.Append("Herhangi bir stok kaydi degistirilmedi.</p>");
                }
            }
            else
            {
                html.Append("<p>Mevcut stok hareketi tablosu bulunamadi.</p>");
            }
        }

        html.Append("</section>");
        html.Append("</table></div></section></main></body></html>");

        return Content(html.ToString(),"text/html; charset=utf-8");
    }

    [HttpPost("registerFinishedGoods")]
    [ValidateAntiForgeryToken]
    public IActionResult RegisterFinishedGoods(long orderId)
    {
        if (orderId <= 0)
            return ErrorPage("Gecersiz uretim emri.");

        using var db = OpenFinishedGoods();
        using var tx = db.BeginTransaction();

        using (var receiptCmd = Command(db, """
            INSERT OR IGNORE INTO MCProductionFinishedGoodsReceipts
                (OrderId,RecipeId,ProductName,
                 CardTable,CardId,CardLabel,ActualKg,ScrapKg)
            SELECT
                o.Id,
                o.RecipeId,
                o.ProductName,
                l.CardTable,
                l.CardId,
                l.CardLabel,
                r.ActualKg,
                COALESCE(r.ScrapKg,'0')
            FROM MCProductionPlanningOrders o
            JOIN MCProductionPlanningOrderResults r
                ON r.OrderId=o.Id
            JOIN MCProductionPlanningCardLinks l
                ON l.Kind='product'
               AND l.RecordId=o.RecipeId
            WHERE o.Id=$id
              AND o.Status='TAMAMLANDI'
              AND r.FinishedAt IS NOT NULL
              AND l.CardId>0
              AND CAST(r.ActualKg AS REAL)>0
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM MCProductionFinishedGoodsReceipts existing
                  WHERE existing.OrderId=o.Id
              )
            """,("$id",orderId)))
        {
            receiptCmd.Transaction = tx;

            if (receiptCmd.ExecuteNonQuery() != 1)
            {
                return ErrorPage(
                    "Mamul girisi yapilamadi. Emir tamamlanmis, " +
                    "urun karti baglanmis ve daha once kaydedilmemis olmalidir.");
            }
        }

        tx.Commit();

        return Redirect("/MCProductionPlanning/finishedGoods");
    }
}