using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;

namespace MasterChefTakip.Controllers
{
    [Authorize]
    [Route("MCOperationsSummary")]
    public class MCOperationsSummaryController : Controller
    {
        private readonly IWebHostEnvironment _environment;

        public MCOperationsSummaryController(
            IWebHostEnvironment environment)
        {
            _environment = environment;
        }

        [HttpGet("")]
        [HttpGet("Index")]
        public IActionResult Index()
        {
            var metrics = new List<Metric>();

            string path = Path.Combine(
                _environment.ContentRootPath,
                "app.db");

            if (!System.IO.File.Exists(path))
            {
                ViewData["Error"] =
                    "Veritabani dosyasi bulunamadi.";

                ViewData["Metrics"] = metrics;
                return View();
            }

            try
            {
                var cs = new SqliteConnectionStringBuilder
                {
                    DataSource = path,
                    Mode = SqliteOpenMode.ReadOnly,
                    Pooling = false
                };

                using var db = new SqliteConnection(
                    cs.ToString());

                db.Open();

                AddMetric(db, metrics,
                    "Urun Kartlari",
                    "Products",
                    "Tanimli urun karti");

                AddMetric(db, metrics,
                    "Hammadde Kartlari",
                    "RawMaterials",
                    "Tanimli hammadde karti");

                AddMetric(db, metrics,
                    "Malzeme Katalogu",
                    "MCMaterialCatalog",
                    "Excel ve diger kaynaklardan malzemeler");

                AddMetric(db, metrics,
                    "Stok Hareketleri",
                    "StockMovements",
                    "Kayitli stok hareketleri");

                AddMetric(db, metrics,
                    "Uretim Receteleri",
                    "ProductionRecipes",
                    "Kayitli uretim receteleri");

                AddMetric(db, metrics,
                    "Harici Siparis Aktarimlari",
                    "MCExternalSyncImports",
                    "Harici sistem aktarimlari");

                AddMetric(db, metrics,
                    "Harici Siparis Satirlari",
                    "MCExternalSyncLines",
                    "Harici sistemden alinan satirlar");

                // MC_PHASE7_OPERATION_METRICS

                AddMetric(db, metrics,
                    "Recete Malzemeleri",
                    "ProductionRecipeLines",
                    "Uretim recetelerindeki hammadde satirlari");

                AddMetric(db, metrics,
                    "Hammadde Depolari",
                    "RawMaterialWarehouses",
                    "Tanimli hammadde depolari");

                AddMetric(db, metrics,
                    "Hammadde Stok Hareketleri",
                    "RawMaterialStockMovements",
                    "Gercek hammadde stok hareketleri");

                AddMetric(db, metrics,
                    "Hammadde Stok Defteri",
                    "MCRawStockLedger",
                    "Hammadde stok defteri kayitlari");

                AddMetric(db, metrics,
                    "Mal Kabul Stok Aktarimlari",
                    "RawMaterialReceiptTransfers",
                    "Mal kabulden hammadde stokuna aktarimlar");

                AddMetric(db, metrics,
                    "Palet Kayitlari",
                    "Pallets",
                    "Sistemde kayitli paletler");

                AddMetric(db, metrics,
                    "Hammadde Sayimlari",
                    "RawStockCounts",
                    "Gercek hammadde sayimlari");

                AddMetric(db, metrics,
                    "Hammadde Sayim Onaylari",
                    "RawStockCountApprovals",
                    "Hammadde sayim karar ve onaylari");
                AddMetric(db, metrics,
                    "Banka ve Kasa Hesaplari",
                    "MCBankCashAccounts",
                    "Tanimli finans hesaplari");

                AddMetric(db, metrics,
                    "Banka ve Kasa Hareketleri",
                    "MCBankCashMovements",
                    "Gercek banka ve kasa hareketleri");

                AddMetric(db, metrics,
                    "Cari Hareketler",
                    "MCCariMovements",
                    "Gercek borc ve alacak hareketleri");

                AddMetric(db, metrics,
                    "Alis Faturalari",
                    "MCPurchaseInvoices",
                    "Kayitli alis faturalari");

                AddMetric(db, metrics,
                    "Fatura Cari Aktarimlari",
                    "MCPurchaseInvoiceCariPostings",
                    "Cari hesaba aktarilan faturalar");
                ViewData["FlowIntegrity"] = BuildFlowChecks(db);
            }
            catch (Exception)
            {
                ViewData["Error"] =
                    "Veritabani okuma islemi basarisiz. " +
                    "Baglanti ve tablo yapisi kontrol edilmelidir.";
            }

            ViewData["Metrics"] = metrics;
            ViewData["UpdatedAt"] =
                DateTime.Now.ToString("dd.MM.yyyy HH:mm");

            return View();
        }

        // MC_PHASE7_INTEGRITY_CHECKS

        public record FlowIntegrityResult(
            string Title,
            long? Records,
            long? Problems,
            string Status);

        private static List<FlowIntegrityResult> BuildFlowChecks(
            SqliteConnection db)
        {
            var results = new List<FlowIntegrityResult>();

            bool TableExists(string name)
            {
                using var cmd = db.CreateCommand();

                cmd.CommandText = @"
                    SELECT COUNT(*)
                    FROM sqlite_master
                    WHERE type='table' AND name=@name";

                cmd.Parameters.AddWithValue("@name", name);

                return Convert.ToInt64(cmd.ExecuteScalar()) == 1;
            }

            long CountRows(string sql)
            {
                using var cmd = db.CreateCommand();
                cmd.CommandText = sql;
                return Convert.ToInt64(cmd.ExecuteScalar());
            }

            void Check(
                string title,
                string[] tables,
                string sourceSql,
                string problemsSql)
            {
                if (System.Linq.Enumerable.Any(
                    tables,
                    table => !TableExists(table)))
                {
                    results.Add(new FlowIntegrityResult(
                        title, null, null, "Kaynak tablo eksik"));

                    return;
                }

                try
                {
                    long records = CountRows(sourceSql);

                    if (records == 0)
                    {
                        results.Add(new FlowIntegrityResult(
                            title, 0, null, "Gercek kayit yok"));

                        return;
                    }

                    long problems = CountRows(problemsSql);

                    results.Add(new FlowIntegrityResult(
                        title,
                        records,
                        problems,
                        problems == 0
                            ? "Kayit baglantilari uygun"
                            : "Baglanti sorunu bulundu"));
                }
                catch
                {
                    results.Add(new FlowIntegrityResult(
                        title,
                        null,
                        null,
                        "Kontrol calistirilamadi"));
                }
            }

            Check(
                "Uretim recetesi / hammadde",
                new[]
                {
                    "ProductionRecipeLines",
                    "ProductionRecipes",
                    "RawMaterials"
                },
                @"SELECT COUNT(*)
                  FROM ProductionRecipeLines",
                @"SELECT COUNT(*)
                  FROM ProductionRecipeLines l
                  LEFT JOIN ProductionRecipes r
                    ON r.Id=l.ProductionRecipeId
                  LEFT JOIN RawMaterials m
                    ON m.Id=l.RawMaterialId
                  WHERE r.Id IS NULL OR m.Id IS NULL"
            );

            Check(
                "Malzeme katalogu / stok karti",
                new[]
                {
                    "MCMaterialStockCardLinks",
                    "MCMaterialCatalog",
                    "RawMaterials"
                },
                @"SELECT COUNT(*)
                  FROM MCMaterialStockCardLinks",
                @"SELECT COUNT(*)
                  FROM MCMaterialStockCardLinks l
                  LEFT JOIN MCMaterialCatalog c
                    ON c.Id=l.MaterialCatalogId
                  LEFT JOIN RawMaterials r
                    ON r.Id=l.RawMaterialId
                  WHERE c.Id IS NULL OR r.Id IS NULL"
            );

            Check(
                "Alis faturasi / cari hareket",
                new[]
                {
                    "MCPurchaseInvoiceCariPostings",
                    "MCPurchaseInvoices",
                    "MCCariMovements"
                },
                @"SELECT COUNT(*)
                  FROM MCPurchaseInvoiceCariPostings",
                @"SELECT COUNT(*)
                  FROM MCPurchaseInvoiceCariPostings p
                  LEFT JOIN MCPurchaseInvoices i
                    ON i.Id=p.PurchaseInvoiceId
                  LEFT JOIN MCCariMovements c
                    ON c.Id=p.CariMovementId
                  WHERE i.Id IS NULL
                     OR c.Id IS NULL
                     OR c.CariAccountId<>p.CariAccountId"
            );

            // MC_PHASE7_EXTERNAL_PRODUCT_MATCH_V1

            if (!TableExists("MCExternalSyncLines"))
            {
                results.Add(new FlowIntegrityResult(
                    "Harici siparis / urun kodu",
                    null,
                    null,
                    "Harici siparis tablosu bulunamadi"));
            }
            else if (!TableExists("Products"))
            {
                results.Add(new FlowIntegrityResult(
                    "Harici siparis / urun kodu",
                    null,
                    null,
                    "Urun kartlari tablosu bulunamadi"));
            }
            else
            {
                string? FindColumn(
                    string table,
                    params string[] candidates)
                {
                    using var cmd = db.CreateCommand();

                    cmd.CommandText =
                        "PRAGMA table_info(\"" +
                        table.Replace("\"", "\"\"") +
                        "\")";

                    var fields = new List<string>();

                    using var reader = cmd.ExecuteReader();

                    while (reader.Read())
                        fields.Add(reader.GetString(1));

                    return candidates.FirstOrDefault(
                        candidate => fields.Any(
                            field => string.Equals(
                                field,
                                candidate,
                                StringComparison.OrdinalIgnoreCase)));
                }

                string? externalCode = FindColumn(
                    "MCExternalSyncLines",
                    "ProductCode",
                    "StockCode",
                    "Sku",
                    "ItemCode",
                    "ExternalProductCode");

                string? internalCode = FindColumn(
                    "Products",
                    "Code");

                if (externalCode == null || internalCode == null)
                {
                    results.Add(new FlowIntegrityResult(
                        "Harici siparis / urun kodu",
                        null,
                        null,
                        "Stok kodu sutunu dogrulanamadi"));
                }
                else
                {
                    string SafeColumn(string value) =>
                        "\"" + value.Replace("\"", "\"\"") + "\"";

                    try
                    {
                        long lineCount = CountRows(
                            "SELECT COUNT(*) FROM MCExternalSyncLines");

                        if (lineCount == 0)
                        {
                            results.Add(new FlowIntegrityResult(
                                "Harici siparis / urun kodu",
                                0,
                                null,
                                "Gercek siparis satiri yok"));
                        }
                        else
                        {
                            string externalField =
                                "s." + SafeColumn(externalCode);

                            string internalField =
                                "p." + SafeColumn(internalCode);

                            string query = @"
                                SELECT COUNT(*)
                                FROM MCExternalSyncLines s
                                WHERE " + externalField + @" IS NULL
                                   OR trim(CAST(" + externalField + @" AS TEXT))=''
                                   OR NOT EXISTS
                                   (
                                       SELECT 1
                                       FROM Products p
                                       WHERE upper(trim(CAST(" +
                                               internalField + @" AS TEXT))) =
                                             upper(trim(CAST(" +
                                               externalField + @" AS TEXT)))
                                   )";

                            long unmatched = CountRows(query);

                            results.Add(new FlowIntegrityResult(
                                "Harici siparis / urun kodu",
                                lineCount,
                                unmatched,
                                unmatched == 0
                                    ? "Tum siparis kodlari urun kartlariyla eslesiyor"
                                    : "Eslesmeyen urun kodlari incelenmeli"));
                        }
                    }
                    catch
                    {
                        results.Add(new FlowIntegrityResult(
                            "Harici siparis / urun kodu",
                            null,
                            null,
                            "Siparis urun kodu kontrolu calistirilamadi"));
                    }
                }
            }
            // MC_PHASE7_ORDER_PRODUCTION_SHIPMENT_V1
            // Gercek tablolardaki tanimli FK baglantilarini denetler.
            // FK bulunmuyorsa sonucu basarili olarak gostermez.

            void CheckOperationalStage(
                string title,
                Func<string, bool> selector)
            {
                var stageTables = new List<string>();

                using (var tablesCommand = db.CreateCommand())
                {
                    tablesCommand.CommandText = @"
                        SELECT name
                        FROM sqlite_master
                        WHERE type='table'
                          AND name NOT LIKE 'sqlite_%'
                        ORDER BY name";

                    using var tableReader = tablesCommand.ExecuteReader();

                    while (tableReader.Read())
                    {
                        string name = tableReader.GetString(0);

                        if (selector(name))
                            stageTables.Add(name);
                    }
                }

                if (stageTables.Count == 0)
                {
                    results.Add(new FlowIntegrityResult(
                        title,
                        null,
                        null,
                        "Ilgili kaynak tablo bulunamadi"));

                    return;
                }

                long totalRecords = 0;
                long totalProblems = 0;
                int definedForeignKeys = 0;
                int testedTables = 0;

                try
                {
                    foreach (string table in stageTables)
                    {
                        string quotedTable =
                            "\"" + table.Replace("\"", "\"\"") + "\"";

                        long records = CountRows(
                            "SELECT COUNT(*) FROM " + quotedTable);

                        totalRecords += records;

                        int foreignKeys = 0;

                        using (var fkDefinition = db.CreateCommand())
                        {
                            fkDefinition.CommandText =
                                "PRAGMA foreign_key_list(" +
                                quotedTable + ")";

                            using var fkReader =
                                fkDefinition.ExecuteReader();

                            while (fkReader.Read())
                                foreignKeys++;
                        }

                        definedForeignKeys += foreignKeys;

                        if (foreignKeys == 0)
                            continue;

                        testedTables++;

                        using var fkCheck = db.CreateCommand();

                        fkCheck.CommandText =
                            "PRAGMA foreign_key_check(" +
                            quotedTable + ")";

                        using var issueReader =
                            fkCheck.ExecuteReader();

                        while (issueReader.Read())
                            totalProblems++;
                    }

                    string status;

                    long? verifiedProblems = null;

                    if (totalRecords == 0)
                    {
                        status = "Gercek islem kaydi yok; test bekliyor";
                    }
                    else if (definedForeignKeys == 0)
                    {
                        status =
                            "FK iliskisi tanimli degil; " +
                            "kayit baglantilari dogrulanamadi";
                    }
                    else if (testedTables < stageTables.Count)
                    {
                        status =
                            "Kismi kontrol: " + testedTables +
                            "/" + stageTables.Count +
                            " tabloda FK tanimli";
                    }
                    else
                    {
                        verifiedProblems = totalProblems;

                        status = totalProblems == 0
                            ? "Tanimli FK baglantilarinda sorun bulunmadi"
                            : "Tanimli FK baglantilarinda sorun bulundu";
                    }

                    results.Add(new FlowIntegrityResult(
                        title,
                        totalRecords,
                        verifiedProblems,
                        status));
                }
                catch
                {
                    results.Add(new FlowIntegrityResult(
                        title,
                        null,
                        null,
                        "Baglanti kontrolu calistirilamadi"));
                }
            }

            CheckOperationalStage(
                "Harici siparis veri baglantilari",
                name =>
                    name.StartsWith(
                        "MCExternalSync",
                        StringComparison.OrdinalIgnoreCase));

            CheckOperationalStage(
                "Uretim veri baglantilari",
                name =>
                    name.StartsWith(
                        "Production",
                        StringComparison.OrdinalIgnoreCase));

            CheckOperationalStage(
                "Sevkiyat ve palet veri baglantilari",
                name =>
                    name.Contains(
                        "Shipment",
                        StringComparison.OrdinalIgnoreCase)
                    || name.Contains(
                        "Dispatch",
                        StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith(
                        "Pallet",
                        StringComparison.OrdinalIgnoreCase));
            return results;
        }
        private static void AddMetric(
            SqliteConnection db,
            List<Metric> result,
            string title,
            string table,
            string description)
        {
            using var exists = db.CreateCommand();

            exists.CommandText = @"
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type='table'
                  AND name=@name";

            exists.Parameters.AddWithValue(
                "@name",
                table);

            bool available =
                Convert.ToInt64(exists.ExecuteScalar()) == 1;

            if (!available)
            {
                result.Add(new Metric(
                    title,
                    table,
                    null,
                    description));

                return;
            }

            using var count = db.CreateCommand();

            // Tablo adlari yalnizca kaynak kodda tanimli
            // sabit listeden gelir; kullanici girdisi degildir.
            count.CommandText =
                "SELECT COUNT(*) FROM \"" +
                table.Replace("\"", "\"\"") +
                "\"";

            long total =
                Convert.ToInt64(count.ExecuteScalar());

            result.Add(new Metric(
                title,
                table,
                total,
                description));
        }

        public record Metric(
            string Title,
            string Table,
            long? Count,
            string Description);
    }
}