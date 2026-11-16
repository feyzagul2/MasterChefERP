using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

// MC_PRODUCTION_STOCK_TRACE_FINAL_V2
public sealed partial class MCProductionPlanningController
{
    private static HashSet<string> StockTraceColumns(
        SqliteConnection db, string table)
    {
        var columns = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        using var check = db.CreateCommand();
        check.CommandText =
            "SELECT COUNT(*) FROM sqlite_master " +
            "WHERE type='table' AND name=$name";
        check.Parameters.AddWithValue("$name", table);

        if (Convert.ToInt64(check.ExecuteScalar()) == 0)
            return columns;

        using var cmd = db.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(\"" + table + "\")";

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
            columns.Add(reader.GetString(1));

        return columns;
    }

    [HttpGet("stockTrace")]
    public IActionResult ProductionStockTrace()
    {
        using var db = Open();

        var html = new StringBuilder();

        html.Append("""
            <!doctype html>
            <html lang="tr">
            <head>
            <meta charset="utf-8">
            <meta name="viewport"
                  content="width=device-width,initial-scale=1">
            <title>MASTER CHEF - Uretim Stok Baglantisi</title>
            <style>
            body{font-family:Arial;background:#f1f5f9;
                 color:#172033;padding:24px}
            main{max-width:1400px;margin:auto}
            section{background:white;padding:20px;margin:18px 0;
                    border-radius:10px}
            table{width:100%;border-collapse:collapse}
            th,td{padding:10px;text-align:left;
                  border-bottom:1px solid #ddd}
            th{background:#edf3f8}
            .scroll{overflow-x:auto}
            .ok{color:#166534;font-weight:bold}
            .warn{color:#b45309;font-weight:bold}
            .note{padding:12px;background:#fff7ed;
                  border-radius:7px}
            </style>
            </head>
            <body><main>
            <h1>MASTER CHEF - Uretim ve Stok Baglantisi</h1>
            <p>
            <a href="/MCProductionPlanning">Uretim Planlama</a>
            &nbsp; | &nbsp;
            <a href="/MCProductionPlanning/operations">
            Uretim Operasyonlari</a>
            &nbsp; | &nbsp;
            <a href="/MCProductionPlanning/finishedGoods">
            Mamul Girisleri</a>
            </p>
            <p class="note">
            Mevcut uretim, lot, paketleme ve stok hareketleri
            birlikte gosterilir. Bu ekran yeni stok hareketi
            olusturmaz ve net stok bakiyesi hesaplamaz.
            </p>
            """);

        var links = StockTraceColumns(
            db, "ProductionPackagingLinks");

        var orders = StockTraceColumns(
            db, "ProductionOrders");

        var movements = StockTraceColumns(
            db, "StockMovements");

        var requiredLinks = new[]
        {
            "ProductionOrderId",
            "ProductLotId",
            "PackagingBatchId"
        };

        var requiredOrders = new[]
        {
            "Id", "Status"
        };

        var requiredMovements = new[]
        {
            "Id", "PackagingBatchId", "Type",
            "BoxCount", "LooseUnitCount", "UnitsPerBox"
        };

        if (!requiredLinks.All(links.Contains) ||
            !requiredOrders.All(orders.Contains) ||
            !requiredMovements.All(movements.Contains))
        {
            html.Append("<section><h2>Tablo Baglantilari</h2>");
            html.Append("<p class='warn'>Mevcut veritabani alanlari ");
            html.Append("beklenen yapida degil. Yanlis eslestirme yapilmadi.</p>");

            foreach (var entry in new[]
            {
                ("ProductionPackagingLinks",
                    requiredLinks.Except(links).ToArray()),
                ("ProductionOrders",
                    requiredOrders.Except(orders).ToArray()),
                ("StockMovements",
                    requiredMovements.Except(movements).ToArray())
            })
            {
                html.Append("<p><strong>");
                html.Append(H(entry.Item1));
                html.Append("</strong>: ");

                html.Append(entry.Item2.Length == 0
                    ? "Uyumlu"
                    : "Eksik alanlar: " +
                      H(string.Join(", ", entry.Item2)));

                html.Append("</p>");
            }

            html.Append("</section></main></body></html>");

            return Content(
                html.ToString(), "text/html; charset=utf-8");
        }

        var orderNumberSql = orders.Contains("OrderNumber")
            ? "CAST(o.OrderNumber AS TEXT)"
            : "CAST(o.Id AS TEXT)";

        html.Append("""
            <section>
            <h2>Uretim - Lot - Paketleme - Stok Girisleri</h2>
            <div class="scroll">
            <table>
            <tr>
            <th>Uretim Emri</th>
            <th>Durum</th>
            <th>Lot ID</th>
            <th>Paketleme Partisi</th>
            <th>Stok Hareketi</th>
            <th>Koli</th>
            <th>Acik Adet</th>
            <th>Kolide Adet</th>
            <th>Toplam Adet</th>
            <th>Kontrol</th>
            </tr>
            """);

        using var cmd = db.CreateCommand();

        cmd.CommandText = @"
            SELECT
                " + orderNumberSql + @",
                CAST(o.Status AS TEXT),
                l.ProductLotId,
                l.PackagingBatchId,
                m.Id,
                COALESCE(m.BoxCount,0),
                COALESCE(m.LooseUnitCount,0),
                COALESCE(m.UnitsPerBox,0)
            FROM ProductionPackagingLinks l
            JOIN ProductionOrders o
                ON o.Id=l.ProductionOrderId
            LEFT JOIN StockMovements m
                ON m.PackagingBatchId=l.PackagingBatchId
               AND m.Type=1
            ORDER BY l.ProductionOrderId DESC,m.Id DESC
            LIMIT 500
            ";

        using var reader = cmd.ExecuteReader();

        var totalRows = 0;
        var stockRows = 0;
        var missingRows = 0;
        var invalidRows = 0;

        while (reader.Read())
        {
            totalRows++;

            var hasStock = !reader.IsDBNull(4);

            decimal boxCount;
            decimal looseCount;
            decimal unitsPerBox;

            try
            {
                boxCount = Convert.ToDecimal(reader.GetValue(5));
                looseCount = Convert.ToDecimal(reader.GetValue(6));
                unitsPerBox = Convert.ToDecimal(reader.GetValue(7));
            }
            catch
            {
                boxCount = -1m;
                looseCount = -1m;
                unitsPerBox = -1m;
            }

            var valid =
                boxCount >= 0m &&
                looseCount >= 0m &&
                (boxCount == 0m || unitsPerBox > 0m);

            decimal totalUnits = 0m;

            if (valid)
            {
                try
                {
                    totalUnits =
                        checked(boxCount * unitsPerBox + looseCount);
                }
                catch
                {
                    valid = false;
                }
            }

            if (!hasStock)
                missingRows++;
            else
                stockRows++;

            if (hasStock && !valid)
                invalidRows++;

            html.Append("<tr>");

            foreach (var column in new[] { 0, 1, 2, 3 })
            {
                html.Append("<td>");
                html.Append(reader.IsDBNull(column)
                    ? "-"
                    : H(reader.GetValue(column)));
                html.Append("</td>");
            }

            html.Append("<td>");
            html.Append(hasStock ? H(reader.GetValue(4)) : "-");
            html.Append("</td><td>");
            html.Append(boxCount);
            html.Append("</td><td>");
            html.Append(looseCount);
            html.Append("</td><td>");
            html.Append(unitsPerBox);
            html.Append("</td><td>");
            html.Append(valid
                ? totalUnits.ToString("0.###")
                : "-");
            html.Append("</td><td>");

            if (!hasStock)
                html.Append("<span class='warn'>STOK GIRISI YOK</span>");
            else if (!valid)
                html.Append("<span class='warn'>MIKTAR HATALI</span>");
            else
                html.Append("<span class='ok'>STOK GIRISI VAR</span>");

            html.Append("</td></tr>");
        }

        html.Append("</table></div>");

        html.Append("<p><strong>Toplam eslesme satiri:</strong> ");
        html.Append(totalRows);
        html.Append(" | <strong>Stok girisi satiri:</strong> ");
        html.Append(stockRows);
        html.Append(" | <strong>Girisi bulunamayan:</strong> ");
        html.Append(missingRows);
        html.Append(" | <strong>Miktar hatasi:</strong> ");
        html.Append(invalidRows);
        html.Append("</p>");

        html.Append("""
            <p class="note">
            Ayni paketleme partisi birden fazla stok giris
            hareketine sahip olabilir. Buradaki sayaclar
            benzersiz uretim emri sayisi degildir.
            Sevkiyat ve iptal hareketleri net stok hesabina
            dahil edilmemistir.
            </p>
            </section>
            </main></body></html>
            """);

        return Content(
            html.ToString(), "text/html; charset=utf-8");
    }
}