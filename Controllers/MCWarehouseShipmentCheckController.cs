using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using MasterChefTakip.Data;
using MasterChefTakip.Models;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

// MC_WAREHOUSE_SHIPMENT_CHECK_V2
[Authorize(Roles = AppRoles.Admin)]
[Route("MCWarehouseShipmentCheck")]
public sealed class MCWarehouseShipmentCheckController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IAntiforgery _antiforgery;

    public MCWarehouseShipmentCheckController(
        ApplicationDbContext db,
        IAntiforgery antiforgery)
    {
        _db = db;
        _antiforgery = antiforgery;
    }

    private static string H(object? value) =>
        WebUtility.HtmlEncode(
            Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");

    private static long Units(StockMovement movement)
    {
        if (!movement.UnitsPerBox.HasValue ||
            movement.UnitsPerBox.Value <= 0)
            throw new InvalidOperationException(
                "Koli ici adet tanimsiz veya gecersiz.");

        return checked(
            (long)movement.BoxCount * movement.UnitsPerBox.Value +
            movement.LooseUnitCount
        );
    }

    private static int Sign(StockMovementType type) =>
        type switch
        {
            StockMovementType.Receipt => 1,
            StockMovementType.Return => 1,
            StockMovementType.Shipment => -1,
            StockMovementType.Damage => -1,
            StockMovementType.ReceiptCancellation => -1,
            StockMovementType.ShipmentCancellation => 1,
            _ => 0
        };

    private string FormToken()
    {
        var token = _antiforgery
            .GetAndStoreTokens(HttpContext)
            .RequestToken;

        return "<input type='hidden' " +
               "name='__RequestVerificationToken' value='" +
               H(token) + "'>";
    }

    [HttpGet("")]
    public IActionResult Index() => Page(null);

    [HttpPost("check")]
    [ValidateAntiForgeryToken]
    public IActionResult Check(
        int palletItemId,
        int boxCount,
        int looseUnitCount)
    {
        if (palletItemId <= 0 ||
            boxCount < 0 ||
            looseUnitCount < 0 ||
            (boxCount == 0 && looseUnitCount == 0))
        {
            return BadRequest("Sevkiyat miktari gecersiz.");
        }

        try
        {
            var movements = _db.Set<StockMovement>()
                .AsNoTracking()
                .Where(x => x.PalletItemId == palletItemId)
                .ToList();

            if (movements.Count == 0)
                return Page(
                    "Bu palet kalemi icin stok hareketi bulunamadi.");

            var unitsPerBoxValues = movements
                .Where(x => x.UnitsPerBox.HasValue &&
                            x.UnitsPerBox.Value > 0)
                .Select(x => x.UnitsPerBox!.Value)
                .Distinct()
                .ToList();

            if (unitsPerBoxValues.Count != 1 ||
                movements.Any(x => !x.UnitsPerBox.HasValue ||
                                   x.UnitsPerBox.Value <= 0))
            {
                return Page(
                    "Koli ici adet bilgileri eksik veya uyusmuyor. " +
                    "Sevkiyat kontrolu durduruldu.");
            }

            var unitsPerBox = unitsPerBoxValues[0];

            long balance = 0;

            foreach (var movement in movements)
            {
                if (movement.BoxCount < 0 ||
                    movement.LooseUnitCount < 0)
                {
                    return Page(
                        "Gecersiz stok hareketi bulundu. " +
                        "Sevkiyat kontrolu durduruldu.");
                }

                var sign = Sign(movement.Type);

                if (sign == 0)
                    return Page(
                        "Taninmayan stok hareket turu bulundu.");

                balance = checked(
                    balance + sign * Units(movement));
            }

            long requested = checked(
                (long)boxCount * unitsPerBox + looseUnitCount);

            long difference = checked(balance - requested);

            var result = new StringBuilder();

            result.Append("<h2>Stok Kontrol Sonucu</h2><table>");

            void Row(string label, object? value)
            {
                result.Append("<tr><th>");
                result.Append(H(label));
                result.Append("</th><td>");
                result.Append(H(value));
                result.Append("</td></tr>");
            }

            Row("Palet Kalem ID", palletItemId);
            Row("Koli İçi Adet", unitsPerBox);
            Row("Mevcut Stok (Adet)", balance);
            Row("İstenen Koli", boxCount);
            Row("İstenen Tekil Adet", looseUnitCount);
            Row("Toplam İstenen Adet", requested);
            Row("Sevkiyat Sonrası Tahmini Bakiye", difference);

            result.Append("</table>");

            if (balance < 0)
            {
                result.Append(
                    "<p class='bad'>STOK TUTARSIZLIGI: " +
                    "Mevcut bakiye negatif.</p>");
            }
            else if (requested <= balance)
            {
                result.Append(
                    "<p class='good'>STOK YETERLI. " +
                    "Sevkiyat miktari karsilanabilir.</p>");
            }
            else
            {
                result.Append(
                    "<p class='bad'>STOK YETERSIZ. " +
                    "Istenen miktar mevcut stogu asiyor.</p>");
            }

            result.Append(
                "<p>Bu ekran stok ayirmaz ve sevkiyat olusturmaz. " +
                "Gercek sevkiyat sirasinda stok tekrar " +
                "kontrol edilmelidir.</p>");

            return Page(result.ToString());
        }
        catch (OverflowException)
        {
            return Page(
                "Stok miktari hesaplama sinirini asti.");
        }
        catch (InvalidOperationException ex)
        {
            return Page(H(ex.Message));
        }
    }

    private IActionResult Page(string? result)
    {
        var html = new StringBuilder();

        html.Append("""
        <!doctype html>
        <html lang="tr">
        <head>
        <meta charset="utf-8">
        <meta name="viewport"
              content="width=device-width,initial-scale=1">
        <title>MASTER CHEF - Sevkiyat Stok Kontrolü</title>
        <style>
        *{box-sizing:border-box}
        body{
            background:#f1f5f9;
            font-family:Arial,sans-serif;
            margin:0;
            padding:30px;
            color:#1e293b
        }
        main{max-width:850px;margin:auto}
        section{
            background:white;
            border-radius:12px;
            padding:25px;
            margin-top:20px;
            border:1px solid #e2e8f0
        }
        label{display:block;margin:15px 0 6px}
        input{
            width:100%;
            padding:12px;
            border:1px solid #cbd5e1;
            border-radius:7px
        }
        button{
            background:#183b5a;
            color:white;
            border:0;
            padding:13px 22px;
            border-radius:7px;
            margin-top:20px;
            cursor:pointer
        }
        table{
            width:100%;
            border-collapse:collapse;
            margin-top:15px
        }
        th,td{
            padding:12px;
            border-bottom:1px solid #e2e8f0;
            text-align:left
        }
        .good{
            color:#166534;
            background:#dcfce7;
            padding:15px;
            border-radius:7px
        }
        .bad{
            color:#991b1b;
            background:#fee2e2;
            padding:15px;
            border-radius:7px
        }
        a{color:#1d4ed8}
        </style>
        </head>
        <body><main>
        <h1>MASTER CHEF</h1>
        <h2>Depo ve Sevkiyat Stok Kontrolü</h2>
        <p>Sevkiyat öncesinde gerçek stok bakiyesini kontrol edin.</p>
        <section>
        <form method="post"
              action="/MCWarehouseShipmentCheck/check">
        """);

        html.Append(FormToken());

        html.Append("""
        <label>Palet Kalem ID</label>
        <input type="number" name="palletItemId" min="1" required>

        <label>Sevk Edilecek Koli Sayısı</label>
        <input type="number" name="boxCount"
               min="0" value="0" required>

        <label>Sevk Edilecek Tekil Adet</label>
        <input type="number" name="looseUnitCount"
               min="0" value="0" required>

        <button type="submit">
        Stok Uygunluğunu Kontrol Et
        </button>
        </form>
        </section>
        """);

        if (!string.IsNullOrWhiteSpace(result))
        {
            html.Append("<section>");
            html.Append(result);
            html.Append("</section>");
        }

        html.Append("""
        <section>
        <h3>Diğer Depo İşlemleri</h3>
        <p><a href="/MCProductionPlanning/warehouseDispatch">
        Sevkiyat hareketleri</a></p>
        <p><a href="/MCProductionPlanning/warehouseCount">
        Fiziksel stok sayımı</a></p>
        <p><a href="/MCProductionPlanning/management">
        Yönetim merkezi</a></p>
        </section>
        </main></body></html>
        """);

        return Content(
            html.ToString(),
            "text/html; charset=utf-8");
    }
}