using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

// MC_PRODUCTION_ORDER_COVERAGE_V1
public sealed partial class MCExternalOrderStockReviewController
{
    private sealed class MCCoverageProduct
    {
        public long ProductId { get; init; }
        public string ProductCode { get; set; } = "";
        public decimal DemandUnits { get; set; }
        public int VerifiedLines { get; set; }
        public int PendingLines { get; set; }
        public int DuplicateLines { get; set; }
        public int OrderLines { get; set; }
    }

    private sealed record MCCoverageProduction(
        long Count,
        decimal PlannedKg,
        string Status
    );

    private static string MCCoverageNumber(decimal value)
    {
        return value.ToString(
            "N2",
            CultureInfo.GetCultureInfo("tr-TR"));
    }

    private static string MCCoverageIdentifier(string value)
    {
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static bool MCCoverageHas(
        SqliteConnection db,
        string table,
        params string[] required)
    {
        var columns = Columns(db, table);

        return required.All(columns.Contains);
    }

    private static string? MCCoverageProductionTable(
        SqliteConnection db)
    {
        foreach (var name in new[]
        {
            "ProductionOrders",
            "ProductionOrder"
        })
        {
            if (Exists(db, name) &&
                MCCoverageHas(
                    db, name,
                    "Id",
                    "RecipeId",
                    "PlannedQuantityKg",
                    "Status"))
            {
                return name;
            }
        }

        return null;
    }

    private static string? MCCoverageRecipeTable(
        SqliteConnection db)
    {
        foreach (var name in new[]
        {
            "ProductionRecipes",
            "Recipes",
            "ProductionRecipe"
        })
        {
            if (Exists(db, name) &&
                MCCoverageHas(
                    db, name,
                    "Id",
                    "ProductId"))
            {
                return name;
            }
        }

        return null;
    }

    private static MCCoverageProduction MCCoverageExisting(
        SqliteConnection db,
        string productionTable,
        string recipeTable,
        long productId)
    {
        string orders = MCCoverageIdentifier(productionTable);
        string recipes = MCCoverageIdentifier(recipeTable);

        using var cmd = db.CreateCommand();

        cmd.CommandText =
            "SELECT COUNT(*), " +
            "COALESCE(SUM(o.PlannedQuantityKg),0) " +
            "FROM " + orders + " o " +
            "JOIN " + recipes + " r ON r.Id=o.RecipeId " +
            "WHERE r.ProductId=@product " +
            "AND o.Status IS NOT NULL " +
            "AND LOWER(TRIM(o.Status)) NOT IN " +
            "('completed','complete','finished'," +
            "'cancelled','canceled','iptal','tamamlandi')";

        cmd.Parameters.AddWithValue(
            "@product", productId);

        using var reader = cmd.ExecuteReader();

        if (!reader.Read())
            return new MCCoverageProduction(
                0, 0, "URETIM EMRI YOK");

        long count = Convert.ToInt64(reader.GetValue(0));

        decimal kg = Convert.ToDecimal(
            reader.GetValue(1),
            CultureInfo.InvariantCulture);

        return new MCCoverageProduction(
            count,
            kg,
            count > 0
                ? "URETIM EMRI MEVCUT"
                : "URETIM EMRI YOK");
    }

    // MC_PRODUCTION_ORDER_DETAILS_V2
    // Gercek uretim emirlerini salt okunur olarak listeler.
    private static string MCCoverageOrderDetails(
        SqliteConnection db,
        string? productionTable,
        string? recipeTable,
        System.Collections.Generic.IReadOnlyCollection<long> importedProducts)
    {
        var html = new StringBuilder();

        html.Append(
            "<section><h2>Mevcut Uretim Emirleri - Detay</h2>" +
            "<p class='muted'>Acik uretim emirleri, " +
            "mevcut durumlari ve planlanan kilogram miktarlari. " +
            "Bu bilgiler siparislere otomatik tahsis edilmez.</p>");

        if (productionTable == null || recipeTable == null)
        {
            html.Append(
                "<p class='warn'>Uretim emri veya recete " +
                "tablosu dogrulanamadi. Detaylar gosterilemiyor.</p>" +
                "</section>");

            return html.ToString();
        }

        string orders = MCCoverageIdentifier(productionTable);
        string recipes = MCCoverageIdentifier(recipeTable);

        string sql =
            "SELECT o.Id, r.ProductId, o.Status, " +
            "o.PlannedQuantityKg, o.RecipeId " +
            "FROM " + orders + " o " +
            "JOIN " + recipes + " r ON r.Id=o.RecipeId " +
            "WHERE o.Status IS NOT NULL " +
            "AND LOWER(TRIM(o.Status)) NOT IN " +
            "('completed','complete','finished'," +
            "'cancelled','canceled','iptal','tamamlandi') " +
            "ORDER BY o.Id DESC LIMIT 300";

        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;

        using var reader = cmd.ExecuteReader();

        html.Append(
            "<div class='scroll'><table><thead><tr>" +
            "<th>Uretim Emri ID</th>" +
            "<th>Urun Kart ID</th>" +
            "<th>Recete ID</th>" +
            "<th>Uretim Emri Durumu</th>" +
            "<th>Planlanan Miktar (Kg)</th>" +
            "<th>Siparis Iliskisi Kontrolu</th>" +
            "</tr></thead><tbody>");

        int displayed = 0;
        int matched = 0;
        int unrelated = 0;

        while (reader.Read())
        {
            displayed++;

            long orderId = Convert.ToInt64(reader.GetValue(0));
            long productId = Convert.ToInt64(reader.GetValue(1));

            string status = reader.IsDBNull(2)
                ? ""
                : Convert.ToString(
                    reader.GetValue(2),
                    CultureInfo.InvariantCulture) ?? "";

            decimal planned = reader.IsDBNull(3)
                ? 0m
                : Convert.ToDecimal(
                    reader.GetValue(3),
                    CultureInfo.InvariantCulture);

            long recipeId = Convert.ToInt64(reader.GetValue(4));

            bool hasImportedOrder =
                importedProducts.Contains(productId);

            if (hasImportedOrder)
                matched++;
            else
                unrelated++;

            html.Append("<tr>");

            void Cell(object? value)
            {
                html.Append("<td>");
                html.Append(H(value));
                html.Append("</td>");
            }

            Cell(orderId);
            Cell(productId);
            Cell(recipeId);
            Cell(status);

            Cell(planned.ToString(
                "N2",
                CultureInfo.GetCultureInfo("tr-TR")));

            Cell(hasImportedOrder
                ? "AYNI URUNE AIT HARICI SIPARIS VAR"
                : "AKTARILMIS SIPARISLE URUN ESLESMEDI");

            html.Append("</tr>");
        }

        if (displayed == 0)
        {
            html.Append(
                "<tr><td colspan='6'>" +
                "Listeleme kosullarina uyan uretim emri yok." +
                "</td></tr>");
        }

        html.Append("</tbody></table></div>");

        html.Append(
            "<p>Listelenen uretim emri: <strong>" +
            displayed + "</strong></p>");

        html.Append(
            "<p>Harici siparislerde ayni urun bulunan emir: " +
            "<strong>" + matched + "</strong></p>");

        html.Append(
            "<p>Harici siparislerde urun eslesmesi olmayan emir: " +
            "<strong>" + unrelated + "</strong></p>");

        if (displayed == 300)
        {
            html.Append(
                "<p class='warn'>En son 300 emir gosteriliyor. " +
                "Bu liste tum uretim emirlerinin tamami olmayabilir.</p>");
        }

        html.Append(
            "<p class='warn'><strong>Planlama notu:</strong> " +
            "Ayni urunun hem sipariste hem uretim emrinde " +
            "bulunmasi, uretim emrinin o siparise ait " +
            "oldugunu kanitlamaz. Taslak veya beklemede " +
            "durumundaki emirler de burada gorunebilir. " +
            "Planlanan kilogramlar adet hesabindan dusulmez.</p>");

        html.Append("</section>");

        return html.ToString();
    }
    // MC_ORDER_PRODUCTION_LINK_REPORT_V2
    private static string MCOrderProductionLinksReport(
        SqliteConnection db,
        string? productionTable,
        string? recipeTable)
    {
        var html = new StringBuilder();

        html.Append(
            "<section><h2>Siparis - Uretim Emri Baglantilari</h2>" +
            "<p class='muted'>Harici siparislerin kayitli " +
            "uretim emri baglantilari ve guncel durumlari.</p>");

        if (!Exists(db, "MCExternalSyncLines"))
        {
            html.Append("<p>Aktarilmis siparis yok.</p></section>");
            return html.ToString();
        }

        bool hasLinks =
            Exists(db, "MCExternalProductionOrderLinks");

        bool productionSchema =
            productionTable != null &&
            recipeTable != null;

        string linkJoin = hasLinks
            ? " LEFT JOIN MCExternalProductionOrderLinks l " +
              "ON l.ExternalOrderId=s.ExternalOrderId COLLATE NOCASE " +
              "AND l.ProductId=s.ProductId "
            : "";

        string linkId = hasLinks
            ? "l.ProductionOrderId"
            : "NULL";

        string productionJoin = "";
        string statusColumn = "NULL";
        string kgColumn = "NULL";
        string actualProductColumn = "NULL";

        if (hasLinks && productionSchema)
        {
            productionJoin =
                " LEFT JOIN " +
                MCCoverageIdentifier(productionTable!) +
                " p ON p.Id=l.ProductionOrderId " +
                " LEFT JOIN " +
                MCCoverageIdentifier(recipeTable!) +
                " r ON r.Id=p.RecipeId ";

            statusColumn = "p.Status";
            kgColumn = "p.PlannedQuantityKg";
            actualProductColumn = "r.ProductId";
        }

        string sql =
            "SELECT s.ExternalOrderId,s.ProductId,s.LineCount," +
            linkId + "," +
            statusColumn + "," +
            kgColumn + "," +
            actualProductColumn + " " +
            "FROM (" +
            "SELECT ExternalOrderId,ProductId," +
            "COUNT(*) LineCount,MAX(Id) LastLineId " +
            "FROM MCExternalSyncLines " +
            "WHERE ProductId IS NOT NULL " +
            "AND MatchStatus='ESLESTI' " +
            "GROUP BY ExternalOrderId COLLATE NOCASE,ProductId" +
            ") s " +
            linkJoin +
            productionJoin +
            "ORDER BY s.LastLineId DESC LIMIT 300";

        html.Append(
            "<div class='scroll'><table><thead><tr>" +
            "<th>Siparis No</th>" +
            "<th>Urun ID</th>" +
            "<th>Siparis Kalemi</th>" +
            "<th>Uretim Emri ID</th>" +
            "<th>Baglanti Durumu</th>" +
            "<th>Uretim Durumu</th>" +
            "<th>Planlanan Kg</th>" +
            "</tr></thead><tbody>");

        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;

        using var reader = cmd.ExecuteReader();

        int total = 0;
        int linked = 0;
        int unlinked = 0;
        int warnings = 0;

        while (reader.Read())
        {
            total++;

            string orderNo = reader.IsDBNull(0)
                ? ""
                : Convert.ToString(
                    reader.GetValue(0),
                    CultureInfo.InvariantCulture) ?? "";

            long productId = Convert.ToInt64(reader.GetValue(1));
            long lineCount = Convert.ToInt64(reader.GetValue(2));

            long? productionId = reader.IsDBNull(3)
                ? null
                : Convert.ToInt64(reader.GetValue(3));

            string? status = reader.IsDBNull(4)
                ? null
                : Convert.ToString(
                    reader.GetValue(4),
                    CultureInfo.InvariantCulture);

            decimal? kg = reader.IsDBNull(5)
                ? null
                : Convert.ToDecimal(
                    reader.GetValue(5),
                    CultureInfo.InvariantCulture);

            long? actualProduct = reader.IsDBNull(6)
                ? null
                : Convert.ToInt64(reader.GetValue(6));

            string result;

            if (!productionId.HasValue)
            {
                unlinked++;
                result = "URETIM EMRI BAGLANTISI YOK";
            }
            else if (!productionSchema)
            {
                warnings++;
                result = "URETIM KAYDI DOGRULANAMADI";
            }
            else if (!actualProduct.HasValue)
            {
                warnings++;
                result = "URETIM EMRI BULUNAMADI";
            }
            else if (actualProduct.Value != productId)
            {
                warnings++;
                result = "URUN UYUSMAZLIGI";
            }
            else if (string.IsNullOrWhiteSpace(status))
            {
                warnings++;
                result = "URETIM DURUMU BELIRSIZ";
            }
            else if (new[]
            {
                "completed", "complete", "finished",
                "cancelled", "canceled", "iptal", "tamamlandi"
            }.Contains(status.Trim().ToLowerInvariant()))
            {
                warnings++;
                result = "EMIR KAPANMIS - KONTROL";
            }
            else
            {
                linked++;
                result = "ACIK URETIM EMRINE BAGLI";
            }

            html.Append("<tr>");

            void Cell(object? value)
            {
                html.Append("<td>");
                html.Append(H(value));
                html.Append("</td>");
            }

            Cell(orderNo);
            Cell(productId);
            Cell(lineCount);
            Cell(productionId);
            Cell(result);
            Cell(status ?? "-");

            Cell(kg.HasValue
                ? kg.Value.ToString(
                    "N2",
                    CultureInfo.GetCultureInfo("tr-TR"))
                : "-");

            html.Append("</tr>");
        }

        if (total == 0)
        {
            html.Append(
                "<tr><td colspan='7'>" +
                "Urun eslesmesi bulunan siparis yok.</td></tr>");
        }

        html.Append("</tbody></table></div>");

        html.Append(
            "<p>Listelenen siparis-urun grubu: <strong>" +
            total + "</strong></p>" +
            "<p>Acik uretim emrine bagli: <strong>" +
            linked + "</strong></p>" +
            "<p>Baglanti bulunmayan: <strong>" +
            unlinked + "</strong></p>" +
            "<p>Kontrol gerektiren baglanti: <strong>" +
            warnings + "</strong></p>");

        if (total == 300)
        {
            html.Append(
                "<p class='warn'>Son 300 siparis-urun " +
                "grubu gosteriliyor.</p>");
        }

        html.Append(
            "<p class='warn'>Uretim emrine bagli olmasi, " +
            "siparisin uretildigi veya sevke hazir " +
            "oldugu anlamina gelmez. " +
            "Kg/adet donusumu yapilmaz.</p>");

        html.Append(
            "<a class='button' " +
            "href='/MCExternalOrders/productionLinks'>" +
            "Siparis - Uretim Baglantilarini Yonet</a>");

        html.Append("</section>");

        return html.ToString();
    }
    [HttpGet("productionCoverage")]
    public IActionResult ProductionCoverage()
    {
        if (_db.Database.ProviderName?.Contains(
            "Sqlite",
            StringComparison.OrdinalIgnoreCase) != true)
        {
            return StatusCode(
                500, "SQLite veritabani gerekli.");
        }

        var db = (SqliteConnection)
            _db.Database.GetDbConnection();

        bool openedHere =
            db.State != ConnectionState.Open;

        if (openedHere)
            _db.Database.OpenConnection();

        try
        {
            var html = new StringBuilder();

            html.Append(
                "<h1>MASTER CHEF - Siparis ve Uretim Emri Kontrolu</h1>" +
                "<p class='muted'>Siparislerden hesaplanan " +
                "mamul ihtiyaci ile mevcut uretim " +
                "emirlerinin urun bazli karsilastirilmasi.</p>");

            if (!Exists(db, "MCExternalSyncLines"))
            {
                html.Append(
                    "<section>Aktarilmis siparis bulunmuyor.</section>");

                return Content(
                    Page(html.ToString()),
                    "text/html; charset=utf-8");
            }

            var productionTable =
                MCCoverageProductionTable(db);

            var recipeTable =
                MCCoverageRecipeTable(db);

            bool productionAvailable =
                productionTable != null &&
                recipeTable != null;

            var orderLines = AllDemandLines(db);
            var confirmedUnits = ReadUnitRecords(db);
            var repeated = RepeatedOrderNumbers(db);

            var groups =
                new Dictionary<long, MCCoverageProduct>();

            int unmatchedProducts = 0;

            foreach (var line in orderLines)
            {
                if (!line.ProductId.HasValue ||
                    line.MatchStatus != "ESLESTI")
                {
                    unmatchedProducts++;
                    continue;
                }

                long productId = line.ProductId.Value;

                if (!groups.TryGetValue(
                    productId, out var group))
                {
                    group = new MCCoverageProduct
                    {
                        ProductId = productId,
                        ProductCode = line.ProductCode
                    };

                    groups[productId] = group;
                }

                group.OrderLines++;

                if (repeated.Contains(line.OrderNumber))
                {
                    group.DuplicateLines++;
                    continue;
                }

                bool current =
                    confirmedUnits.TryGetValue(
                        line.Id, out var record) &&
                    record != null &&
                    record.ProductId == productId &&
                    record.QuantityText == line.Quantity;

                if (!current ||
                    !TryDemand(
                        line.Quantity,
                        record!.Unit,
                        record.UnitsPerBox,
                        out var demand))
                {
                    group.PendingLines++;
                    continue;
                }

                group.DemandUnits += demand;
                group.VerifiedLines++;
            }

            var stockMapping = DetectStockMapping(db);

            html.Append(
                "<p class='warn'><strong>Onemli:</strong> " +
                "Siparis talebi adet, uretim emirleri " +
                "kilogram olabilir. Bu iki miktar " +
                "birbirinden otomatik dusulmez. " +
                "Acik uretim emirleri mevcut siparislerle " +
                "eslestirilmis kabul edilmez. " +
                "Bu rapor planlama kontroludur, " +
                "otomatik uretim talimati degildir.</p>");

            if (!productionAvailable)
            {
                html.Append(
                    "<p class='warn'>Mevcut uretim emri ve " +
                    "recete tablolari beklenen alanlarla " +
                    "eslestirilemedi. Uretim emri miktarlari " +
                    "bilinmiyor olarak gosterilecek.</p>");
            }

            if (!stockMapping.CanCalculate)
            {
                html.Append(
                    "<p class='warn'>Mamul stok hesaplamasi " +
                    "icin gerekli baglantilar eksik: " +
                    H(stockMapping.Explanation) +
                    "</p>");
            }

            html.Append(
                "<section><h2>Uretim Planlama Karsilastirmasi</h2>" +
                "<div class='scroll'><table>" +
                "<thead><tr>" +
                "<th>Urun Kodu</th>" +
                "<th>Urun ID</th>" +
                "<th>Siparis Satiri</th>" +
                "<th>Dogrulanan Talep (Adet)</th>" +
                "<th>Hesaplanan Stok (Adet)</th>" +
                "<th>Stok Acigi (Adet)</th>" +
                "<th>Acik Uretim Emri</th>" +
                "<th>Planlanan Uretim (Kg)</th>" +
                "<th>Kontrol Durumu</th>" +
                "</tr></thead><tbody>");

            int productionFound = 0;
            int needsReview = 0;
            int calculated = 0;

            foreach (var group in groups.Values
                .OrderBy(x => x.ProductCode)
                .ThenBy(x => x.ProductId))
            {
                var stock = CalculateStock(
                    db,
                    stockMapping,
                    group.ProductId);

                MCCoverageProduction production;

                if (productionAvailable)
                {
                    production = MCCoverageExisting(
                        db,
                        productionTable!,
                        recipeTable!,
                        group.ProductId);
                }
                else
                {
                    production = new MCCoverageProduction(
                        0,
                        0,
                        "URETIM EMIRLERI OKUNAMADI");
                }

                bool demandComplete =
                    group.VerifiedLines == group.OrderLines &&
                    group.PendingLines == 0 &&
                    group.DuplicateLines == 0 &&
                    unmatchedProducts == 0;

                bool stockValid =
                    stock.Units.HasValue &&
                    stock.Units.Value >= 0;

                decimal? shortage = null;

                if (demandComplete && stockValid)
                {
                    shortage = Math.Max(
                        0m,
                        group.DemandUnits -
                        stock.Units!.Value);

                    calculated++;
                }

                if (production.Count > 0)
                    productionFound++;

                string status;

                if (!demandComplete)
                {
                    status = "SIPARIS DOGRULAMASI EKSIK";
                    needsReview++;
                }
                else if (!stockValid)
                {
                    status = "STOK HESABI EKSIK";
                    needsReview++;
                }
                else if (!productionAvailable)
                {
                    status = "URETIM EMIRLERI DOGRULANAMADI";
                    needsReview++;
                }
                else if (production.Count > 0)
                {
                    status = "MEVCUT URETIM EMRINI KONTROL ET";
                    needsReview++;
                }
                else if (shortage.GetValueOrDefault() > 0)
                {
                    status = "URETIM PLANLAMA ADAYI";
                }
                else
                {
                    status = "HESAPLANAN STOK YETERLI";
                }

                html.Append("<tr>");

                void Cell(object? value)
                {
                    html.Append("<td>");
                    html.Append(H(value));
                    html.Append("</td>");
                }

                Cell(group.ProductCode);
                Cell(group.ProductId);
                Cell(group.OrderLines);
                Cell(MCCoverageNumber(group.DemandUnits));

                Cell(stock.Units.HasValue
                    ? MCCoverageNumber(stock.Units.Value)
                    : "BILINMIYOR");

                Cell(shortage.HasValue
                    ? MCCoverageNumber(shortage.Value)
                    : "HESAPLANAMADI");

                Cell(productionAvailable
                    ? production.Count.ToString()
                    : "BILINMIYOR");

                Cell(productionAvailable
                    ? MCCoverageNumber(production.PlannedKg)
                    : "BILINMIYOR");

                Cell(status);

                html.Append("</tr>");
            }

            if (groups.Count == 0)
            {
                html.Append(
                    "<tr><td colspan='9'>" +
                    "Urun kartiyla eslesen siparis bulunamadi." +
                    "</td></tr>");
            }

            html.Append("</tbody></table></div></section>");

            // MC_PRODUCTION_ORDER_DETAILS_V2
            html.Append(
                MCCoverageOrderDetails(
                    db,
                    productionTable,
                    recipeTable,
                    groups.Keys.ToArray()));
            // MC_ORDER_PRODUCTION_LINK_REPORT_V2
            html.Append(
                MCOrderProductionLinksReport(
                    db,
                    productionTable,
                    recipeTable));
            html.Append(
                "<section><h2>Planlama Ozeti</h2>" +
                "<p>Urun grubu: <strong>" +
                groups.Count + "</strong></p>" +
                "<p>Stok acigi hesaplanabilen urun: <strong>" +
                calculated + "</strong></p>" +
                "<p>Mevcut uretim emri bulunan urun: <strong>" +
                productionFound + "</strong></p>" +
                "<p>Kontrol gereken urun: <strong>" +
                needsReview + "</strong></p>" +
                "<p>Urun eslesmesi eksik siparis satiri: <strong>" +
                unmatchedProducts + "</strong></p>" +
                "</section>");

            html.Append(
                "<section><h2>Islem Baglantilari</h2>" +
                "<a class='button' " +
                "href='/MCExternalOrders/stockReview/demand'>" +
                "Uretim Ihtiyaci</a>" +
                "<a class='button' " +
                "href='/MCExternalOrders/stockReview/units'>" +
                "Siparis Miktar Birimleri</a>" +
                "<a class='button' " +
                "href='/MCProductionPlanning/management'>" +
                "Yonetim Merkezi</a>" +
                "<p class='muted'>Bu rapor salt okunurdur. " +
                "Uretim emri, stok, sevkiyat veya " +
                "finans kaydi olusturmaz.</p></section>");

            return Content(
                Page(html.ToString()),
                "text/html; charset=utf-8");
        }
        finally
        {
            if (openedHere)
                _db.Database.CloseConnection();
        }
    }
}