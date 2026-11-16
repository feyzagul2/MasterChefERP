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

// MC_EXTERNAL_PRODUCTION_DEMAND_V1
public sealed partial class MCExternalOrderStockReviewController
{
    private sealed class DemandGroup
    {
        public long ProductId { get; init; }
        public string ProductCode { get; set; } = "";
        public decimal VerifiedUnits { get; set; }
        public long VerifiedLines { get; set; }
        public long PendingLines { get; set; }
        public long DuplicateLines { get; set; }
        public long TotalLines { get; set; }
    }

    private sealed record DemandLine(
        long Id,
        string OrderNumber,
        string ProductCode,
        string Quantity,
        long? ProductId,
        string MatchStatus
    );

    private static List<DemandLine> AllDemandLines(
        SqliteConnection db)
    {
        var result = new List<DemandLine>();

        using var cmd = db.CreateCommand();

        cmd.CommandText = """
            SELECT
                Id,
                ExternalOrderId,
                ProductCode,
                Quantity,
                ProductId,
                MatchStatus
            FROM MCExternalSyncLines
            ORDER BY Id
            """;

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            result.Add(new DemandLine(
                Convert.ToInt64(reader.GetValue(0)),
                reader.IsDBNull(1) ? "" :
                    Convert.ToString(
                        reader.GetValue(1),
                        CultureInfo.InvariantCulture) ?? "",
                reader.IsDBNull(2) ? "" :
                    Convert.ToString(
                        reader.GetValue(2),
                        CultureInfo.InvariantCulture) ?? "",
                reader.IsDBNull(3) ? "" :
                    Convert.ToString(
                        reader.GetValue(3),
                        CultureInfo.InvariantCulture) ?? "",
                reader.IsDBNull(4) ? null :
                    Convert.ToInt64(reader.GetValue(4)),
                reader.IsDBNull(5) ? "" :
                    Convert.ToString(
                        reader.GetValue(5),
                        CultureInfo.InvariantCulture) ?? ""
            ));
        }

        return result;
    }

    private static HashSet<string> RepeatedOrderNumbers(
        SqliteConnection db)
    {
        var result = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        using var cmd = db.CreateCommand();

        cmd.CommandText = """
            SELECT ExternalOrderId
            FROM MCExternalSyncLines
            GROUP BY ExternalOrderId COLLATE NOCASE
            HAVING COUNT(DISTINCT ImportId)>1
            """;

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            if (!reader.IsDBNull(0))
                result.Add(reader.GetString(0));
        }

        return result;
    }

    [HttpGet("demand")]
    public IActionResult ProductionDemand()
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

        bool openedHere = db.State != ConnectionState.Open;

        if (openedHere)
            _db.Database.OpenConnection();

        try
        {
            var html = new StringBuilder();

            html.Append(
                "<h1>MASTER CHEF - Uretim Ihtiyaci</h1>" +
                "<p class='muted'>Harici siparis talebi ile " +
                "mamul stok hareketlerinin urun bazli " +
                "karsilastirilmasi.</p>");

            if (!Exists(db, "MCExternalSyncLines"))
            {
                html.Append(
                    "<section>Henuz aktarilmis siparis yok." +
                    "<p><a class='button' " +
                    "href='/MCExternalOrders/import'>" +
                    "Siparis Aktarimi</a></p></section>");

                return Content(
                    Page(html.ToString()),
                    "text/html; charset=utf-8");
            }

            var lines = AllDemandLines(db);
            var units = ReadUnitRecords(db);
            var repeatedOrders = RepeatedOrderNumbers(db);
            var stockMapping = DetectStockMapping(db);

            var groups = new Dictionary<long, DemandGroup>();

            long unresolvedProductLines = 0;
            long pendingUnitLines = 0;
            long duplicateLines = 0;

            foreach (var line in lines)
            {
                if (!line.ProductId.HasValue ||
                    line.MatchStatus != "ESLESTI")
                {
                    unresolvedProductLines++;
                    continue;
                }

                long productId = line.ProductId.Value;

                if (!groups.TryGetValue(productId, out var group))
                {
                    group = new DemandGroup
                    {
                        ProductId = productId,
                        ProductCode = line.ProductCode
                    };

                    groups[productId] = group;
                }

                group.TotalLines++;

                // Daha eski aktarimlardan kalmis tekrarlar
                // eksik uretim hesabina dahil edilmez.
                if (repeatedOrders.Contains(line.OrderNumber))
                {
                    group.DuplicateLines++;
                    duplicateLines++;
                    continue;
                }

                bool unitCurrent =
                    units.TryGetValue(line.Id, out var record) &&
                    record != null &&
                    record.ProductId == productId &&
                    record.QuantityText == line.Quantity;

                if (!unitCurrent ||
                    !TryDemand(
                        line.Quantity,
                        record!.Unit,
                        record.UnitsPerBox,
                        out decimal demand))
                {
                    group.PendingLines++;
                    pendingUnitLines++;
                    continue;
                }

                group.VerifiedUnits += demand;
                group.VerifiedLines++;
            }

            html.Append(
                "<p class='warn'><strong>Planlama uyarisi:</strong> " +
                "Bu rapor yalnizca aktarilmis siparisleri esas alir. " +
                "Siparislerin iptal, kapanis ve onceki " +
                "teslimat durumlari harici sistemden " +
                "dogrulanmadikca uretim emri olusturulmaz. " +
                "Hesaplanan aciklar planlama gostergesidir.</p>");

            html.Append(
                "<section><h2>Genel Ozet</h2>" +
                "<p>Toplam aktarim satiri: <strong>" +
                lines.Count + "</strong></p>" +
                "<p>Urun karti eslesmeyen satir: <strong>" +
                unresolvedProductLines + "</strong></p>" +
                "<p>Miktar birimi bekleyen satir: <strong>" +
                pendingUnitLines + "</strong></p>" +
                "<p>Tekrarlanan siparis kaynakli satir: <strong>" +
                duplicateLines + "</strong></p>" +
                "<p>Urun karti bulunan farkli urun: <strong>" +
                groups.Count + "</strong></p>" +
                "</section>");

            if (!stockMapping.CanCalculate)
            {
                html.Append(
                    "<p class='warn'>Stok hesaplama semasi " +
                    "dogrulanamadi: " +
                    H(stockMapping.Explanation) +
                    ". Uretim acigi hesaplanmayacak.</p>");
            }

            html.Append(
                "<section><h2>Urun Bazli Uretim Ihtiyaci</h2>" +
                "<p class='muted'>Tum aktarim satirlari " +
                "incelenir. Dogrulanmamis miktarlar " +
                "toplam talebe eklenmez. Stok bilgisi " +
                "eksikse acik hesabi yapilmaz.</p>" +
                "<div class='scroll'><table><thead><tr>" +
                "<th>Urun Kart ID</th>" +
                "<th>Urun Kodu</th>" +
                "<th>Siparis Satiri</th>" +
                "<th>Dogrulanan Satir</th>" +
                "<th>Bekleyen Birim</th>" +
                "<th>Tekrar Kontrolu</th>" +
                "<th>Dogrulanan Talep (Adet)</th>" +
                "<th>Hesaplanan Stok (Adet)</th>" +
                "<th>Tahmini Uretim Acigi (Adet)</th>" +
                "<th>Durum</th>" +
                "</tr></thead><tbody>");

            decimal verifiedTotal = 0;
            decimal knownShortage = 0;

            long completeGroups = 0;
            long blockedGroups = 0;

            foreach (var group in groups.Values
                .OrderBy(x => x.ProductCode)
                .ThenBy(x => x.ProductId))
            {
                verifiedTotal += group.VerifiedUnits;

                var stock = CalculateStock(
                    db, stockMapping, group.ProductId);

                bool fullyVerified =
                    group.PendingLines == 0 &&
                    group.DuplicateLines == 0 &&
                    group.VerifiedLines == group.TotalLines;

                bool canCalculate =
                    fullyVerified &&
                    stock.Units.HasValue &&
                    stock.Units.Value >= 0 &&
                    unresolvedProductLines == 0;

                decimal? shortage = null;
                string status;

                if (!fullyVerified)
                {
                    status = "SIPARIS DOGRULAMASI EKSIK";
                    blockedGroups++;
                }
                else if (unresolvedProductLines > 0)
                {
                    status = "ESLESMEYEN SIPARISLER VAR";
                    blockedGroups++;
                }
                else if (!stock.Units.HasValue)
                {
                    status = "STOK HESAPLANAMIYOR";
                    blockedGroups++;
                }
                else if (stock.Units.Value < 0)
                {
                    status = "NEGATIF STOK - KONTROL";
                    blockedGroups++;
                }
                else if (canCalculate)
                {
                    shortage = Math.Max(
                        0m,
                        group.VerifiedUnits - stock.Units.Value);

                    knownShortage += shortage.Value;
                    completeGroups++;

                    status = shortage.Value > 0
                        ? "URETIM ACIGI VAR"
                        : "HESAPLANAN STOK YETERLI";
                }
                else
                {
                    status = "KONTROL GEREKLI";
                    blockedGroups++;
                }

                html.Append("<tr>");

                void Cell(object? value)
                {
                    html.Append("<td>");
                    html.Append(H(value));
                    html.Append("</td>");
                }

                string Format(decimal value) =>
                    value.ToString(
                        "N0",
                        CultureInfo.GetCultureInfo("tr-TR"));

                Cell(group.ProductId);
                Cell(group.ProductCode);
                Cell(group.TotalLines);
                Cell(group.VerifiedLines);
                Cell(group.PendingLines);
                Cell(group.DuplicateLines);
                Cell(Format(group.VerifiedUnits));

                Cell(stock.Units.HasValue
                    ? Format(stock.Units.Value)
                    : "HESAPLANAMADI");

                Cell(shortage.HasValue
                    ? Format(shortage.Value)
                    : "BELIRLENEMEDI");

                Cell(status);

                html.Append("</tr>");
            }

            if (groups.Count == 0)
            {
                html.Append(
                    "<tr><td colspan='10'>" +
                    "Urun kartiyla eslesen siparis bulunmuyor." +
                    "</td></tr>");
            }

            html.Append("</tbody></table></div></section>");

            html.Append(
                "<section><h2>Uretim Planlama Ozeti</h2>" +
                "<p>Dogrulanmis toplam siparis talebi: <strong>" +
                verifiedTotal.ToString(
                    "N0",
                    CultureInfo.GetCultureInfo("tr-TR")) +
                " adet</strong></p>" +
                "<p>Eksiksiz hesaplanabilen urun grubu: <strong>" +
                completeGroups + "</strong></p>" +
                "<p>Kontrol bekleyen urun grubu: <strong>" +
                blockedGroups + "</strong></p>" +
                "<p>Hesaplanabilen gruplardaki toplam acik: <strong>" +
                knownShortage.ToString(
                    "N0",
                    CultureInfo.GetCultureInfo("tr-TR")) +
                " adet</strong></p>");

            html.Append(
                "<p class='warn'>Bu toplam tum siparislerin " +
                "kesin uretim ihtiyaci degildir. " +
                "Eksik eslesmeler, tekrarlar, miktar " +
                "birimleri ve harici siparis durumlari " +
                "tamamlanmadan kesin planlama yapilmaz.</p>");

            html.Append("</section>");

            html.Append(
                "<section><h2>Islem Baglantilari</h2>" +
                "<a class='button' " +
                "href='/MCExternalOrders/stockReview/units'>" +
                "Siparis Miktar Birimleri</a>" +
                "<a class='button' " +
                "href='/MCExternalOrders/stockReview'>" +
                "Mamul Stok Karsilastirma</a>" +
                "<a class='button' " +
                "href='/MCExternalOrders/dispatchQueue'>" +
                "Sevkiyat Hazirlik Listesi</a>" +
                "<a class='button' " +
                "href='/MCProductionPlanning/management'>" +
                "Yonetim Merkezi</a>" +
                "<p class='muted'>Rapor salt okunurdur. " +
                "Uretim emri, stok hareketi, sevkiyat " +
                "veya finans kaydi olusturmaz.</p>" +
                "</section>");

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