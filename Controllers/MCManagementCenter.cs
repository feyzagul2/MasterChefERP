using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

// MC_MANAGEMENT_CENTER_V1
public sealed partial class MCProductionPlanningController
{
    private static bool ManagementTableExists(
        SqliteConnection db,
        string name)
    {
        using var cmd = db.CreateCommand();

        cmd.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type='table' AND name=$name
            """;

        cmd.Parameters.AddWithValue("$name", name);

        return Convert.ToInt64(cmd.ExecuteScalar()) == 1;
    }

    private static long ManagementCount(
        SqliteConnection db,
        string table,
        string? status = null)
    {
        var allowed = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "MCProductionPlanningOrders",
            "MCProductionPlanningRecipes",
            "MCProductionActualConsumption",
            "MCProductionFinishedGoodsReceipts",
            "MCWarehousePhysicalCounts",
            "StockMovements",
            "MCPurchaseInvoices",
            "MCCariMovements"
        };

        if (!allowed.Contains(table) ||
            !ManagementTableExists(db, table))
            return -1;

        using var cmd = db.CreateCommand();

        cmd.CommandText =
            "SELECT COUNT(*) FROM \"" + table + "\"";

        if (status != null)
        {
            cmd.CommandText += " WHERE Status=$status";
            cmd.Parameters.AddWithValue("$status", status);
        }

        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static long ManagementMovementCount(
        SqliteConnection db,
        int type)
    {
        if (!ManagementTableExists(db, "StockMovements"))
            return -1;

        using var cmd = db.CreateCommand();

        cmd.CommandText = """
            SELECT COUNT(*)
            FROM StockMovements
            WHERE Type=$type
            """;

        cmd.Parameters.AddWithValue("$type", type);

        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static string ManagementValue(long value)
    {
        return value < 0 ? "Veri kaynagi yok" : value.ToString("N0");
    }

    private static void ManagementCard(
        StringBuilder html,
        string title,
        long number,
        string description)
    {
        html.Append("<div class='card'><div class='label'>");
        html.Append(H(title));
        html.Append("</div><div class='number'>");
        html.Append(H(ManagementValue(number)));
        html.Append("</div><div class='small'>");
        html.Append(H(description));
        html.Append("</div></div>");
    }

    private static void ManagementLink(
        StringBuilder html,
        string title,
        string url,
        string description)
    {
        html.Append("<a class='link' href='");
        html.Append(H(url));
        html.Append("'><strong>");
        html.Append(H(title));
        html.Append("</strong><span>");
        html.Append(H(description));
        html.Append("</span></a>");
    }

    [HttpGet("management")]
    public IActionResult ManagementCenter()
    {
        using var db = Open();

        var planned = ManagementCount(
            db, "MCProductionPlanningOrders", "PLANLANDI");

        var running = ManagementCount(
            db, "MCProductionPlanningOrders", "URETIMDE");

        var completed = ManagementCount(
            db, "MCProductionPlanningOrders", "TAMAMLANDI");

        var cancelled = ManagementCount(
            db, "MCProductionPlanningOrders", "IPTAL");

        var recipes = ManagementCount(
            db, "MCProductionPlanningRecipes");

        var consumptions = ManagementCount(
            db, "MCProductionActualConsumption");

        var finished = ManagementCount(
            db, "MCProductionFinishedGoodsReceipts");

        var counts = ManagementCount(
            db, "MCWarehousePhysicalCounts");

        var receipts = ManagementMovementCount(db, 1);
        var shipments = ManagementMovementCount(db, 2);
        var returns = ManagementMovementCount(db, 3);

        var purchaseInvoices = ManagementCount(
            db, "MCPurchaseInvoices");

        var cariMovements = ManagementCount(
            db, "MCCariMovements");

        var html = new StringBuilder();

        html.Append("""
            <!doctype html>
            <html lang="tr">
            <head>
            <meta charset="utf-8">
            <meta name="viewport"
                  content="width=device-width,initial-scale=1">
            <title>MASTER CHEF - Yonetim Merkezi</title>
            <style>
            *{box-sizing:border-box}
            body{
                margin:0;
                background:#f1f5f9;
                color:#172033;
                font-family:Arial,sans-serif
            }
            main{
                max-width:1350px;
                padding:28px;
                margin:auto
            }
            h1{
                margin-bottom:8px;
                color:#173753
            }
            h2{
                margin-top:0;
                font-size:21px;
                color:#24496c
            }
            .subtitle{
                color:#64748b;
                margin-bottom:25px
            }
            section{
                background:white;
                border-radius:12px;
                padding:22px;
                margin-bottom:22px;
                border:1px solid #e2e8f0
            }
            .grid{
                display:grid;
                grid-template-columns:
                    repeat(auto-fit,minmax(190px,1fr));
                gap:14px
            }
            .card{
                border:1px solid #e2e8f0;
                background:#f8fafc;
                border-radius:10px;
                padding:20px
            }
            .label{
                color:#475569;
                font-size:14px;
                font-weight:bold
            }
            .number{
                font-size:28px;
                color:#173753;
                font-weight:bold;
                margin:12px 0
            }
            .small{
                font-size:12px;
                color:#64748b
            }
            .links{
                display:grid;
                grid-template-columns:
                    repeat(auto-fit,minmax(260px,1fr));
                gap:12px
            }
            .link{
                display:flex;
                flex-direction:column;
                gap:8px;
                padding:17px;
                border:1px solid #cbd5e1;
                border-radius:9px;
                text-decoration:none;
                color:#24496c;
                background:#f8fafc
            }
            .link:hover{background:#edf3f8}
            .link span{
                color:#64748b;
                font-size:13px;
                line-height:1.5
            }
            .notice{
                background:#fff7ed;
                border-left:4px solid #d97706;
                padding:15px;
                border-radius:5px;
                line-height:1.6
            }
            </style>
            </head>
            <body>
            <main>
            <h1>MASTER CHEF - Yonetim Merkezi</h1>
            <p class="subtitle">
            Uretim, depo, sevkiyat ve finans operasyonlari
            </p>

            <section>
            <h2>Uretim Durumu</h2>
            <div class="grid">
            """);

        ManagementCard(
            html, "Planlanan Uretim", planned,
            "Planlanan uretim emri sayisi");

        ManagementCard(
            html, "Uretimde", running,
            "Devam eden uretim emri sayisi");

        ManagementCard(
            html, "Tamamlanan", completed,
            "Tamamlanan uretim emri sayisi");

        ManagementCard(
            html, "Iptal Edilen", cancelled,
            "Iptal edilen uretim emri sayisi");

        html.Append("</div></section>");

        html.Append("""
            <section>
            <h2>Recete ve Mamul Kayitlari</h2>
            <div class="grid">
            """);

        ManagementCard(
            html, "Receteler", recipes,
            "Kayitli recete sayisi");

        ManagementCard(
            html, "Tuketim Kayitlari", consumptions,
            "Hammadde ve ambalaj tuketim satirlari");

        ManagementCard(
            html, "Mamul Giris Kayitlari", finished,
            "Uretim kaynakli mamul giris kayitlari");

        html.Append("</div></section>");

        html.Append("""
            <section>
            <h2>Depo ve Sevkiyat</h2>
            <div class="grid">
            """);

        ManagementCard(
            html, "Stok Giris Hareketleri", receipts,
            "Stok hareketi kayit sayisi");

        ManagementCard(
            html, "Sevkiyat Hareketleri", shipments,
            "Sevk hareketi kayit sayisi");

        ManagementCard(
            html, "Iade Hareketleri", returns,
            "Iade hareketi kayit sayisi");

        ManagementCard(
            html, "Fiziksel Sayim", counts,
            "Kaydedilen sayim sayisi");

        html.Append("</div></section>");

        html.Append("""
            <section>
            <h2>Muhasebe ve Finans</h2>
            <div class="grid">
            """);

        ManagementCard(
            html, "Alis Faturalari", purchaseInvoices,
            "Kayitli alis faturasi sayisi");

        ManagementCard(
            html, "Cari Hareketler", cariMovements,
            "Kayitli cari hareket sayisi");

        html.Append("""
            </div>
            <p class="notice">
            Finans kartlari islem sayilarini gosterir.
            Bunlar banka bakiyesi, toplam borc,
            toplam alacak veya nakit akisi degildir.
            Bu tutarlar mevcut finans modullerindeki
            hesaplamalarla ayrica baglanmalidir.
            </p>
            </section>

            <section>
            <h2>Hizli Erisim</h2>
            <div class="links">
            """);

        ManagementLink(
            html,
            "Uretim Planlama",
            "/MCProductionPlanning",
            "Uretim emri ve recete olusturma");

        ManagementLink(
            html,
            "Recete Yonetimi",
            "/MCProductionPlanning/recipes",
            "Receteleri ve hammaddeleri duzenleme");

        ManagementLink(
            html,
            "Uretim Operasyonlari",
            "/MCProductionPlanning/operations",
            "Uretimi baslatma ve tamamlama");

        ManagementLink(
            html,
            "Hammadde ve Ambalaj",
            "/MCProductionPlanning/consumption",
            "Gerceklesen tuketim kayitlari");

        ManagementLink(
            html,
            "Mamul Girisleri",
            "/MCProductionPlanning/finishedGoods",
            "Tamamlanan uretimlerin mamul kayitlari");

        ManagementLink(
            html,
            "Uretim - Stok Izleme",
            "/MCProductionPlanning/stockTrace",
            "Uretim, lot ve paketleme baglantilari");

        ManagementLink(
            html,
            "Depo ve Sevkiyat",
            "/MCProductionPlanning/warehouseDispatch",
            "Sevkiyat, iade ve iptal hareketleri");

        ManagementLink(
            html,
            "Fiziksel Stok Sayimi",
            "/MCProductionPlanning/warehouseCount",
            "Depo sayimlari ve fark kayitlari");

        ManagementLink(
            html,
            "Sevkiyat CSV Aktarimi",
            "/MCProductionPlanning/shipmentExport",
            "Mevcut sevkiyat hareketlerini disa aktar");

        ManagementLink(
            html,
            "Satin Alma Denetimi",
            "/MCProcurementAudit",
            "Fatura, mal kabul ve cari mutabakat");

        html.Append("""
            </div>
            </section>

            <section>
            <h2>Entegrasyon Durumu</h2>
            <p class="notice">
            Mevcut siparis sisteminden otomatik veri alma
            baglantisi henuz kurulmus degildir.
            Bu panel, TAKIP veritabaninda kayitli
            islemleri okur ve veri degistirmez.
            </p>
            </section>
            </main></body></html>
            """);

        
        // MC_MANAGEMENT_ORDER_INTEGRATION_FIXED_V2
        html.Replace(
            "</body>",
            global::MasterChefTakip.Controllers.MCManagementOrderIntegration
                .Render(HttpContext.RequestServices) + "</body>");
return Content(
            html.ToString(),
            "text/html; charset=utf-8");
    }
}