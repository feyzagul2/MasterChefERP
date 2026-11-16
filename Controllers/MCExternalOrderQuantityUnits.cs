using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MasterChefTakip.Controllers;

// MC_ORDER_QUANTITY_UNIT_V1
public sealed partial class MCExternalOrderStockReviewController
{
    private sealed record UnitRecord(
        long LineId,
        long ProductId,
        string QuantityText,
        string Unit,
        long? UnitsPerBox);

    private static Dictionary<long, UnitRecord> ReadUnitRecords(
        SqliteConnection db)
    {
        var result = new Dictionary<long, UnitRecord>();

        if (!Exists(db, "MCExternalOrderQuantityUnits"))
            return result;

        using var cmd = db.CreateCommand();

        cmd.CommandText =
            "SELECT LineId,ProductId,QuantityText,Unit,UnitsPerBox " +
            "FROM MCExternalOrderQuantityUnits";

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            var row = new UnitRecord(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt64(4));

            result[row.LineId] = row;
        }

        return result;
    }

    private static bool TryWholeQuantity(
        string text,
        out decimal value)
    {
        value = 0;

        if (!decimal.TryParse(
            text,
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var parsed))
        {
            return false;
        }

        if (parsed <= 0 ||
            parsed != decimal.Truncate(parsed) ||
            parsed > 1000000000m)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryDemand(
        string quantity,
        string unit,
        long? unitsPerBox,
        out decimal demand)
    {
        demand = 0;

        if (!TryWholeQuantity(quantity, out var number))
            return false;

        if (unit == "ADET")
        {
            demand = number;
            return true;
        }

        if (unit == "KOLI" &&
            unitsPerBox.HasValue &&
            unitsPerBox.Value > 0 &&
            unitsPerBox.Value <= 1000000)
        {
            demand = number * unitsPerBox.Value;
            return true;
        }

        return false;
    }

    [HttpGet("units")]
    public IActionResult QuantityUnits()
    {
        var db = (SqliteConnection)_db.Database.GetDbConnection();
        bool openedHere = db.State != ConnectionState.Open;

        if (openedHere)
            _db.Database.OpenConnection();

        try
        {
            var html = new StringBuilder();

            html.Append(
                "<h1>MASTER CHEF - Siparis Miktar Birimleri</h1>" +
                "<p class='muted'>Kaynak siparis miktarlarinin " +
                "ADET veya KOLI olarak dogrulanmasi.</p>");

            if (!Exists(db, "MCExternalSyncLines"))
            {
                html.Append(
                    "<section>Henuz aktarilmis siparis yok.</section>");

                return Content(
                    Page(html.ToString()),
                    "text/html; charset=utf-8");
            }

            var lines = Orders(db);
            var records = ReadUnitRecords(db);

            var token = HttpContext.RequestServices
                .GetRequiredService<IAntiforgery>()
                .GetAndStoreTokens(HttpContext)
                .RequestToken ?? "";

            int confirmed = 0;
            int waiting = 0;
            int invalid = 0;

            html.Append(
                "<p class='warn'>Birim secimi manuel dogrulamadir. " +
                "Koli ici adet sayisini gercek ambalaj bilgisine " +
                "gore girin. Bu islem stok tahsisi veya " +
                "sevkiyat onayi vermez.</p>");

            html.Append(
                "<section><h2>Siparis Birim Kontrolu</h2>" +
                "<div class='scroll'><table><thead><tr>" +
                "<th>Siparis</th>" +
                "<th>Urun</th>" +
                "<th>Kaynak Miktar</th>" +
                "<th>Eslesme</th>" +
                "<th>Birim</th>" +
                "<th>Koli Ici Adet</th>" +
                "<th>Talep (Adet)</th>" +
                "<th>Islem</th>" +
                "</tr></thead><tbody>");

            foreach (var line in lines)
            {
                bool matched =
                    line.ProductId.HasValue &&
                    line.MatchStatus == "ESLESTI";

                bool validQuantity =
                    TryWholeQuantity(
                        line.SourceQuantity,
                        out var parsedQuantity);

                bool hasRecord = records.TryGetValue(
                    line.Id, out var record);

                bool current =
                    matched &&
                    validQuantity &&
                    hasRecord &&
                    record != null &&
                    record.ProductId == line.ProductId &&
                    record.QuantityText == line.SourceQuantity;

                decimal demand = 0;

                bool verified =
                    current &&
                    TryDemand(
                        line.SourceQuantity,
                        record!.Unit,
                        record.UnitsPerBox,
                        out demand);

                if (verified)
                    confirmed++;
                else if (!matched || !validQuantity)
                    invalid++;
                else
                    waiting++;

                html.Append("<tr>");

                void Cell(object? value)
                {
                    html.Append("<td>");
                    html.Append(H(value));
                    html.Append("</td>");
                }

                Cell(line.OrderNumber);
                Cell(line.ProductCode);
                Cell(line.SourceQuantity);
                Cell(matched ? "ESLESTI" : "ESLESMEDI");

                if (matched && validQuantity)
                {
                    string formId = "unit_" + line.Id;

                    html.Append("<td><select form='");
                    html.Append(formId);
                    html.Append("' name='unit'>");

                    foreach (var unit in new[] { "ADET", "KOLI" })
                    {
                        html.Append("<option value='");
                        html.Append(unit);
                        html.Append("'");

                        if (current && record!.Unit == unit)
                            html.Append(" selected");

                        html.Append(">");
                        html.Append(unit);
                        html.Append("</option>");
                    }

                    html.Append("</select></td>");

                    html.Append("<td><input form='");
                    html.Append(formId);
                    html.Append("' name='unitsPerBox' type='number'");
                    html.Append(" min='1' max='1000000' style='width:95px'");

                    if (current &&
                        record!.UnitsPerBox.HasValue)
                    {
                        html.Append(" value='");
                        html.Append(record.UnitsPerBox.Value);
                        html.Append("'");
                    }

                    html.Append("></td>");

                    Cell(verified
                        ? demand.ToString(
                            "N0",
                            CultureInfo.GetCultureInfo("tr-TR"))
                        : "DOGRULANMADI");

                    html.Append("<td><form id='");
                    html.Append(formId);
                    html.Append("' method='post' action='");
                    html.Append("/MCExternalOrders/stockReview/units'>");
                    html.Append("<input type='hidden' ");
                    html.Append("name='__RequestVerificationToken' value='");
                    html.Append(H(token));
                    html.Append("'>");
                    html.Append("<input type='hidden' name='lineId' value='");
                    html.Append(line.Id);
                    html.Append("'>");
                    html.Append("<button type='submit'>");
                    html.Append("Birim Kaydet</button></form></td>");
                }
                else
                {
                    html.Append(
                        "<td colspan='2'>Birim girilemez</td>");

                    Cell("DOGRULANMADI");

                    html.Append("<td>Urun veya miktar kontrolu gerekli</td>");
                }

                html.Append("</tr>");
            }

            if (lines.Count == 0)
                html.Append(
                    "<tr><td colspan='8'>Kayit bulunamadi.</td></tr>");

            html.Append("</tbody></table></div></section>");

            html.Append(
                "<section><h2>Kontrol Ozeti</h2>" +
                "<p>Gosterilen satir: <strong>" +
                lines.Count + "</strong></p>" +
                "<p>Birimi dogrulanan: <strong>" +
                confirmed + "</strong></p>" +
                "<p>Birim bekleyen: <strong>" +
                waiting + "</strong></p>" +
                "<p>Urun veya miktar sorunu: <strong>" +
                invalid + "</strong></p>" +
                "<p class='muted'>Son 300 aktarim satiri " +
                "gosterilir. Bu ekran siparis bakiyesini " +
                "veya teslim edilmeyen miktari hesaplamaz.</p>" +
                "</section>");

            html.Append(
                "<section><a class='button' " +
                "href='/MCExternalOrders/stockReview'>" +
                "Mamul Stok Karsilastirma</a>" +
                "<a class='button' " +
                "href='/MCExternalOrders/dispatchQueue'>" +
                "Sevkiyat Hazirlik Listesi</a>" +
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

    [HttpPost("units")]
    [Microsoft.AspNetCore.Mvc.ValidateAntiForgeryToken]
    public IActionResult SaveQuantityUnit(
        long lineId,
        string unit,
        long? unitsPerBox)
    {
        if (lineId <= 0)
            return BadRequest("Gecersiz siparis satiri.");

        unit = (unit ?? "").Trim().ToUpperInvariant();

        if (unit != "ADET" && unit != "KOLI")
            return BadRequest("Yalnizca ADET veya KOLI kabul edilir.");

        if (unit == "ADET")
            unitsPerBox = null;

        if (unit == "KOLI" &&
            (!unitsPerBox.HasValue ||
             unitsPerBox.Value < 1 ||
             unitsPerBox.Value > 1000000))
        {
            return BadRequest(
                "KOLI icin koli ici adet sayisi gerekli.");
        }

        var db = (SqliteConnection)_db.Database.GetDbConnection();
        bool openedHere = db.State != ConnectionState.Open;

        if (openedHere)
            _db.Database.OpenConnection();

        try
        {
            using var tx = db.BeginTransaction();

            long productId;
            string quantity;

            using (var read = db.CreateCommand())
            {
                read.Transaction = tx;
                read.CommandText =
                    "SELECT ProductId,Quantity,MatchStatus " +
                    "FROM MCExternalSyncLines WHERE Id=@id";

                read.Parameters.AddWithValue("@id", lineId);

                using var reader = read.ExecuteReader();

                if (!reader.Read())
                    return NotFound("Siparis satiri bulunamadi.");

                if (reader.IsDBNull(0) ||
                    reader.GetString(2) != "ESLESTI")
                {
                    return BadRequest(
                        "Once urun karti eslestirilmeli.");
                }

                productId = reader.GetInt64(0);

                quantity = Convert.ToString(
                    reader.GetValue(1),
                    CultureInfo.InvariantCulture) ?? "";
            }

            if (!TryDemand(
                quantity, unit, unitsPerBox, out var demand))
            {
                return BadRequest(
                    "Kaynak miktar pozitif tam sayi olmali. " +
                    "Virgullu veya belirsiz miktarlar " +
                    "otomatik donusturulmez.");
            }

            using (var create = db.CreateCommand())
            {
                create.Transaction = tx;

                create.CommandText = """
                    CREATE TABLE IF NOT EXISTS
                    MCExternalOrderQuantityUnits (
                        LineId INTEGER PRIMARY KEY,
                        ProductId INTEGER NOT NULL,
                        QuantityText TEXT NOT NULL,
                        Unit TEXT NOT NULL
                            CHECK(Unit IN ('ADET','KOLI')),
                        UnitsPerBox INTEGER,
                        ConfirmedAtUtc TEXT NOT NULL,
                        CHECK(
                            (Unit='ADET' AND UnitsPerBox IS NULL)
                            OR
                            (Unit='KOLI'
                             AND UnitsPerBox BETWEEN 1 AND 1000000)
                        )
                    )
                    """;

                create.ExecuteNonQuery();
            }

            using (var save = db.CreateCommand())
            {
                save.Transaction = tx;

                save.CommandText = """
                    INSERT INTO MCExternalOrderQuantityUnits
                    (
                        LineId,
                        ProductId,
                        QuantityText,
                        Unit,
                        UnitsPerBox,
                        ConfirmedAtUtc
                    )
                    VALUES
                    (
                        @line,
                        @product,
                        @quantity,
                        @unit,
                        @box,
                        @date
                    )
                    ON CONFLICT(LineId) DO UPDATE SET
                        ProductId=excluded.ProductId,
                        QuantityText=excluded.QuantityText,
                        Unit=excluded.Unit,
                        UnitsPerBox=excluded.UnitsPerBox,
                        ConfirmedAtUtc=excluded.ConfirmedAtUtc
                    """;

                save.Parameters.AddWithValue("@line", lineId);
                save.Parameters.AddWithValue("@product", productId);
                save.Parameters.AddWithValue("@quantity", quantity);
                save.Parameters.AddWithValue("@unit", unit);

                save.Parameters.AddWithValue(
                    "@box",
                    unitsPerBox.HasValue
                        ? (object)unitsPerBox.Value
                        : DBNull.Value);

                save.Parameters.AddWithValue(
                    "@date",
                    DateTime.UtcNow.ToString("O"));

                save.ExecuteNonQuery();
            }

            tx.Commit();

            TempData["UnitSaved"] =
                "Siparis satiri miktar birimi dogrulandi.";

            return Redirect(
                "/MCExternalOrders/stockReview/units");
        }
        finally
        {
            if (openedHere)
                _db.Database.CloseConnection();
        }
    }
}