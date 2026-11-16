using System;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Text;
using MasterChefTakip.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MasterChefTakip.Controllers;

// MC_MANAGEMENT_ORDER_INTEGRATION_FIXED_V2
public static class MCManagementOrderIntegration
{
    private static string H(object? value)
    {
        return WebUtility.HtmlEncode(
            Convert.ToString(
                value,
                CultureInfo.InvariantCulture) ?? "");
    }

    private static bool Exists(
        DbConnection db,
        string table)
    {
        using var cmd = db.CreateCommand();

        cmd.CommandText =
            "SELECT COUNT(*) FROM sqlite_master " +
            "WHERE type='table' AND name=@name";

        var p = cmd.CreateParameter();
        p.ParameterName = "@name";
        p.Value = table;
        cmd.Parameters.Add(p);

        return Convert.ToInt64(
            cmd.ExecuteScalar() ?? 0L) == 1;
    }

    private static long Count(
        DbConnection db,
        string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;

        return Convert.ToInt64(
            cmd.ExecuteScalar() ?? 0L);
    }

    private static string Card(
        string title,
        long value,
        string explanation,
        bool warning = false)
    {
        string bg = warning ? "#fff7ed" : "#f8fafc";
        string border = warning ? "#fdba74" : "#e2e8f0";

        return
            "<div style='padding:18px;background:" + bg +
            ";border:1px solid " + border +
            ";border-radius:10px'>" +
            "<div style='font-size:13px;color:#64748b'>" +
            H(title) + "</div>" +
            "<div style='font-size:29px;font-weight:bold;" +
            "margin:10px 0'>" +
            value.ToString(
                "N0",
                CultureInfo.GetCultureInfo("tr-TR")) +
            "</div>" +
            "<div style='font-size:12px;color:#64748b'>" +
            H(explanation) +
            "</div></div>";
    }

    public static string Render(IServiceProvider services)
    {
        var html = new StringBuilder();

        html.Append(
            "<section style='background:white;padding:24px;" +
            "border:1px solid #e2e8f0;border-radius:12px;" +
            "margin:24px 0'>" +
            "<h2>Siparis Entegrasyonu - Canli Durum</h2>" +
            "<p>Mevcut siparis sisteminden aktarilan " +
            "kayitlarin guncel durumu.</p>");

        var context =
            services.GetRequiredService<ApplicationDbContext>();

        if (context.Database.ProviderName?.Contains(
                "Sqlite",
                StringComparison.OrdinalIgnoreCase) != true)
        {
            html.Append(
                "<p>Bu rapor SQLite veritabani gerektiriyor.</p>" +
                "</section>");
            return html.ToString();
        }

        var db = context.Database.GetDbConnection();

        bool openedHere = db.State != ConnectionState.Open;

        if (openedHere)
            context.Database.OpenConnection();

        try
        {
            if (!Exists(db, "MCExternalSyncImports") ||
                !Exists(db, "MCExternalSyncLines"))
            {
                html.Append(
                    "<p style='background:#fff7ed;padding:14px'>" +
                    "Henuz kaydedilmis siparis aktarimi yok." +
                    "</p>" +
                    "<p><a href='/MCExternalOrders/import'>" +
                    "Siparis aktarimina git</a></p>" +
                    "</section>");

                return html.ToString();
            }

            long imports = Count(db,
                "SELECT COUNT(*) FROM MCExternalSyncImports");

            long lines = Count(db,
                "SELECT COUNT(*) FROM MCExternalSyncLines");

            long orders = Count(db,
                "SELECT COUNT(DISTINCT ExternalOrderId) " +
                "FROM MCExternalSyncLines");

            long matched = Count(db, """
                SELECT COUNT(*)
                FROM MCExternalSyncLines
                WHERE MatchStatus='ESLESTI'
                AND ProductId IS NOT NULL
                """);

            long unmatched = Count(db, """
                SELECT COUNT(*)
                FROM MCExternalSyncLines
                WHERE MatchStatus<>'ESLESTI'
                OR ProductId IS NULL
                """);

            long duplicates = Count(db, """
                SELECT COUNT(*)
                FROM (
                    SELECT ExternalOrderId,ProductCode
                    FROM MCExternalSyncLines
                    GROUP BY
                        ExternalOrderId COLLATE NOCASE,
                        ProductCode COLLATE NOCASE
                    HAVING COUNT(DISTINCT ImportId)>1
                )
                """);

            long conflicts = Count(db, """
                SELECT COUNT(*)
                FROM (
                    SELECT ExternalOrderId,ProductCode
                    FROM MCExternalSyncLines
                    GROUP BY
                        ExternalOrderId COLLATE NOCASE,
                        ProductCode COLLATE NOCASE
                    HAVING COUNT(DISTINCT Quantity)>1
                )
                """);

            html.Append(
                "<div style='display:grid;" +
                "grid-template-columns:repeat(" +
                "auto-fit,minmax(170px,1fr));" +
                "gap:12px;margin:20px 0'>");

            html.Append(Card(
                "Aktarim Dosyasi", imports,
                "Kaydedilen CSV dosyalari"));

            html.Append(Card(
                "Siparis Satiri", lines,
                "Toplam aktarilan satir"));

            html.Append(Card(
                "Farkli Siparis", orders,
                "Siparis numaralari"));

            html.Append(Card(
                "Eslesen Urun", matched,
                "Stok kartiyla eslesen satir"));

            html.Append(Card(
                "Eslesmeyen Urun", unmatched,
                "Kontrol gereken satir", unmatched > 0));

            html.Append(Card(
                "Tekrar Adayi", duplicates,
                "Farkli dosyalarda tekrar",
                duplicates > 0));

            html.Append(Card(
                "Miktar Farki", conflicts,
                "Ayni urunde miktar uyusmazligi",
                conflicts > 0));

            html.Append("</div>");

            html.Append(
                "<div style='display:flex;gap:12px;" +
                "flex-wrap:wrap;margin-top:18px'>");

            void Link(string url, string title)
            {
                html.Append(
                    "<a href='" + H(url) +
                    "' style='padding:12px 16px;" +
                    "background:#183b5a;color:white;" +
                    "border-radius:7px;text-decoration:none'>" +
                    H(title) + "</a>");
            }

            Link(
                "/MCExternalOrders/reconcile",
                "Siparis Mutabakati");

            Link(
                "/MCExternalOrders/import",
                "Siparis Aktarimi");

            Link(
                "/MCExternalOrders/refreshMatches",
                "Urun Eslestirme");
            Link(
                "/MCExternalOrders/dispatchQueue",
                "Sevkiyat Hazirlik Listesi");
            Link(
                "/MCExternalOrders/stockReview",
                "Siparis Stok Karsilastirma");
            Link(
                "/MCExternalOrders/stockReview/units",
                "Siparis Miktar Birimleri");
            Link(
                "/MCExternalOrders/stockReview/demand",
                "Uretim Ihtiyaci Raporu");
            Link(
                "/MCExternalOrders/stockReview/productionCoverage",
                "Mevcut Uretim Emirleri Kontrolu");
            Link(
                "/MCExternalOrders/productionLinks",
                "Siparis Uretim Emri Baglantilari");
            Link(
                "/MCExternalOrders/productionTrace",
                "Siparis Uretim Paketleme Takibi");
            Link(
                "/MCExternalOrders/shipmentTrace",
                "Siparis Sevkiyat Hareketleri");
            Link(
                "/MCExternalOrders/shipmentLinks",
                "Siparis Sevkiyat Baglantilari");










            html.Append("</div>");

            html.Append(
                "<p style='font-size:12px;color:#64748b;" +
                "margin-top:18px'>" +
                "Bu gostergeler kayitli entegrasyon " +
                "verilerini yansitir; satis geliri veya " +
                "sevk edilen siparis sayisi degildir." +
                "</p></section>");

            return html.ToString();
        }
        finally
        {
            if (openedHere)
                context.Database.CloseConnection();
        }
    }
}