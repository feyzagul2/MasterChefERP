using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

// MC_WAREHOUSE_DISPATCH_OVERVIEW_V1
public sealed partial class MCProductionPlanningController
{
    [HttpGet("warehouseDispatch")]
    public IActionResult WarehouseDispatch(
        string? shipmentSearch = null,
        int page = 1)
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
            <title>MASTER CHEF - Depo ve Sevkiyat</title>
            <style>
            body{font-family:Arial;background:#f1f5f9;
                 color:#172033;padding:24px}
            main{max-width:1350px;margin:auto}
            section{background:white;padding:20px;
                    margin:18px 0;border-radius:10px}
            table{width:100%;border-collapse:collapse}
            th,td{padding:11px;text-align:left;
                  border-bottom:1px solid #e2e8f0}
            th{background:#edf3f8}
            input{padding:10px;border:1px solid #cbd5e1;
                  border-radius:6px}
            button{padding:10px 16px;background:#24496c;
                   border:0;border-radius:6px;color:white}
            .scroll{overflow-x:auto}
            .ok{color:#166534;font-weight:bold}
            .warn{color:#b45309;font-weight:bold}
            .card{display:inline-block;background:#edf3f8;
                  padding:15px;margin:5px;border-radius:9px}
            .note{background:#fff7ed;padding:12px;
                  border-radius:7px}
            a{color:#24496c}
            </style>
            </head>
            <body><main>
            <h1>MASTER CHEF - Depo ve Sevkiyat</h1>
            <p><a href="/MCProductionPlanning">Uretim Planlama</a>
            &nbsp; | &nbsp;
            <a href="/MCProductionPlanning/stockTrace">
            Uretim - Stok Izleme</a></p>
            <p class="note">
            Bu ekran mevcut stok hareketlerini inceler.
            Yeni sevkiyat veya stok hareketi olusturmaz.
            </p>
            """);

        var columns = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        using (var schema = db.CreateCommand())
        {
            schema.CommandText =
                "PRAGMA table_info(\"StockMovements\")";

            using var schemaReader = schema.ExecuteReader();

            while (schemaReader.Read())
                columns.Add(schemaReader.GetString(1));
        }

        var required = new[]
        {
            "Id",
            "RelatedShipmentId",
            "PalletItemId",
            "Type",
            "BoxCount",
            "LooseUnitCount",
            "UnitsPerBox"
        };

        var missing = required.Except(columns).ToArray();

        if (missing.Length > 0)
        {
            html.Append("<section><h2>Veritabani Uyumu</h2>");
            html.Append("<p class='warn'>Sevkiyat hareketleri ");
            html.Append("okunamadi. Eksik alanlar: ");
            html.Append(H(string.Join(", ", missing)));
            html.Append("</p></section></main></body></html>");

            return Content(
                html.ToString(),
                "text/html; charset=utf-8");
        }

        shipmentSearch = (shipmentSearch ?? "").Trim();

        if (shipmentSearch.Length > 40)
            shipmentSearch = shipmentSearch.Substring(0, 40);

        var movements = new List<(
            long Id,
            long ShipmentId,
            long PalletItemId,
            long Type,
            decimal Boxes,
            decimal Loose,
            decimal PerBox,
            bool Valid,
            decimal Units)>();

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                SELECT
                    Id,
                    RelatedShipmentId,
                    PalletItemId,
                    Type,
                    BoxCount,
                    LooseUnitCount,
                    UnitsPerBox
                FROM StockMovements
                WHERE RelatedShipmentId IS NOT NULL
                  AND Type IN (2,3,6)
                ORDER BY Id DESC
                """;

            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                try
                {
                    var id = Convert.ToInt64(reader.GetValue(0));
                    var shipmentId = Convert.ToInt64(reader.GetValue(1));
                    var palletId = Convert.ToInt64(reader.GetValue(2));
                    var type = Convert.ToInt64(reader.GetValue(3));

                    var boxes = reader.IsDBNull(4)
                        ? -1m
                        : Convert.ToDecimal(reader.GetValue(4));

                    var loose = reader.IsDBNull(5)
                        ? -1m
                        : Convert.ToDecimal(reader.GetValue(5));

                    var perBox = reader.IsDBNull(6)
                        ? 0m
                        : Convert.ToDecimal(reader.GetValue(6));

                    var valid =
                        boxes >= 0m &&
                        loose >= 0m &&
                        (boxes == 0m || perBox > 0m);

                    decimal units = 0m;

                    if (valid)
                    {
                        try
                        {
                            units = checked(boxes * perBox + loose);
                        }
                        catch
                        {
                            valid = false;
                        }
                    }

                    movements.Add((
                        id,
                        shipmentId,
                        palletId,
                        type,
                        boxes,
                        loose,
                        perBox,
                        valid,
                        units));
                }
                catch
                {
                    // Kaynak kayitlar degistirilmez.
                    // Okunamayan satirlar ozet hesabina dahil edilmez.
                }
            }
        }

        var filtered = movements
            .Where(x =>
                shipmentSearch.Length == 0 ||
                x.ShipmentId.ToString().Contains(
                    shipmentSearch, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var groups = filtered
            .GroupBy(x => x.ShipmentId)
            .OrderByDescending(x => x.Key)
            .ToList();

        var shipmentCount = groups.Count;
        var movementCount = filtered.Count;
        var invalidCount = filtered.Count(x => !x.Valid);
        var cancellationCount = filtered.Count(x => x.Type == 6);

        html.Append("<section><h2>Sevkiyat Ozeti</h2>");

        foreach (var card in new[]
        {
            ("Sevkiyat", shipmentCount),
            ("Hareket", movementCount),
            ("Iptal Hareketi", cancellationCount),
            ("Hatali Miktar", invalidCount)
        })
        {
            html.Append("<div class='card'><strong>");
            html.Append(H(card.Item1));
            html.Append("</strong><br><h2>");
            html.Append(card.Item2);
            html.Append("</h2></div>");
        }

        html.Append("""
            </section>
            <section>
            <h2>Sevkiyat Ara</h2>
            <form method="get" action="/MCProductionPlanning/warehouseDispatch">
            <input name="shipmentSearch"
                   placeholder="Sevkiyat ID"
                   maxlength="40"
            """);

        html.Append(" value='");
        html.Append(H(shipmentSearch));
        html.Append("' />");

        html.Append("""
            <button type="submit">Ara</button>
            <a href="/MCProductionPlanning/warehouseDispatch">
            Temizle</a>
            </form></section>
            """);

        html.Append("""
            <section><h2>Sevkiyat Bazinda Hareketler</h2>
            <div class="scroll"><table>
            <tr>
            <th>Sevkiyat ID</th>
            <th>Sevk Hareketi</th>
            <th>Iade Hareketi</th>
            <th>Iptal Hareketi</th>
            <th>Sevk Edilen Adet</th>
            <th>Iade Adedi</th>
            <th>Iptal Hareketi Adedi</th>
            <th>Kontrol</th>
            </tr>
            """);

        const int pageSize = 50;

        var totalPages = Math.Max(
            1, (groups.Count + pageSize - 1) / pageSize);

        page = Math.Clamp(page, 1, totalPages);

        foreach (var group in groups
            .Skip((page - 1) * pageSize)
            .Take(pageSize))
        {
            var sent = group.Where(x => x.Type == 2).ToList();
            var returned = group.Where(x => x.Type == 3).ToList();
            var cancelled = group.Where(x => x.Type == 6).ToList();

            var issue = group.Any(x => !x.Valid);

            html.Append("<tr><td>#");
            html.Append(group.Key);
            html.Append("</td><td>");
            html.Append(sent.Count);
            html.Append("</td><td>");
            html.Append(returned.Count);
            html.Append("</td><td>");
            html.Append(cancelled.Count);
            html.Append("</td><td>");
            html.Append(sent.Where(x => x.Valid).Sum(x => x.Units));
            html.Append("</td><td>");
            html.Append(returned.Where(x => x.Valid).Sum(x => x.Units));
            html.Append("</td><td>");
            html.Append(cancelled.Where(x => x.Valid).Sum(x => x.Units));
            html.Append("</td><td>");

            html.Append(issue
                ? "<span class='warn'>MIKTAR KONTROLU GEREKIYOR</span>"
                : "<span class='ok'>HAREKETLER OKUNDU</span>");

            html.Append("</td></tr>");
        }

        html.Append("</table></div>");

        if (totalPages > 1)
        {
            html.Append("<p>Sayfa ");
            html.Append(page);
            html.Append(" / ");
            html.Append(totalPages);
            html.Append(" &nbsp; ");

            if (page > 1)
            {
                html.Append("<a href='?shipmentSearch=");
                html.Append(Uri.EscapeDataString(shipmentSearch));
                html.Append("&page=");
                html.Append(page - 1);
                html.Append("'>Onceki</a> &nbsp; ");
            }

            if (page < totalPages)
            {
                html.Append("<a href='?shipmentSearch=");
                html.Append(Uri.EscapeDataString(shipmentSearch));
                html.Append("&page=");
                html.Append(page + 1);
                html.Append("'>Sonraki</a>");
            }

            html.Append("</p>");
        }

        html.Append("""
            <p class="note">
            Iade ve iptal hareketleri ayri gosterilir.
            Bu rakamlar mevcut hareketlerin brut toplamidir.
            Tek basina guncel net stok bakiyesi veya
            tamamlanmis sevkiyat mutabakati degildir.
            </p>
            </section></main></body></html>
            """);

        return Content(
            html.ToString(),
            "text/html; charset=utf-8");
    }
}