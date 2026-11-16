using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MasterChefTakip.Models;

namespace MasterChefTakip.Controllers;

// MC_UNIT_PREVIEW_V1 - Salt okunur hesaplama.
[Authorize(Roles = AppRoles.Admin)]
[Route("MCInventoryUnitPreview")]
public sealed class MCInventoryUnitPreviewController : Controller
{

    // MC_UNIT_NORMALIZATION_V2
    private static string NormalizeUnit(string? source)
    {
        var value = (source ?? "")
            .Trim()
            .ToUpperInvariant();

        return value switch
        {
            "KG" or "KILOGRAM" or "K?LOGRAM" => "KG",
            "GR" or "G" or "GRAM" => "GR",
            "TON" => "TON",

            "L" or "LT" or "LITRE" or "L?TRE"
                or "LITER" => "L",

            "ML" or "MILILITRE" or "M?L?L?TRE"
                or "MILLILITER" => "ML",

            "ADET" => "ADET",
            "METRE" or "M" => "METRE",

            _ => value
        };
    }

    private static readonly Dictionary<string, (string Group, decimal Factor)>
        Units = new(StringComparer.OrdinalIgnoreCase)
        {
            ["KG"] = ("AGIRLIK", 1000m),
            ["GR"] = ("AGIRLIK", 1m),
            ["TON"] = ("AGIRLIK", 1000000m),
            ["L"] = ("HACIM", 1000m),
            ["ML"] = ("HACIM", 1m),
            ["ADET"] = ("ADET", 1m),
            ["METRE"] = ("UZUNLUK", 1m)
        };

    [HttpGet("")]
    public IActionResult Index(
        decimal? miktar,
        string? kaynak,
        string? hedef)
    {
        var model = new MCUnitPreviewPage
        {
            Quantity = miktar,
            From = NormalizeUnit(kaynak),
            To = NormalizeUnit(hedef)
        };

        // Ilk acilista sadece form gosterilir.
        if (!miktar.HasValue &&
            string.IsNullOrWhiteSpace(kaynak) &&
            string.IsNullOrWhiteSpace(hedef))
        {
            return View(model);
        }

        if (!miktar.HasValue ||
            miktar.Value <= 0 ||
            decimal.Round(miktar.Value, 4) != miktar.Value)
        {
            model.Error =
                "Miktar s?f?rdan b?y?k ve en fazla 4 ondal?k basamakl? olmal?d?r.";
            return View(model);
        }

        if (!Units.TryGetValue(model.From, out var from) ||
            !Units.TryGetValue(model.To, out var to))
        {
            model.Error = "Tan?ml? bir ?l?? birimi se?ilmelidir.";
            return View(model);
        }

        if (from.Group != to.Group)
        {
            model.Error =
                "Farkl? ?l?? t?rleri aras?nda d?n???m yap?lamaz.";
            return View(model);
        }

        try
        {
            var calculated = checked(miktar.Value * from.Factor / to.Factor);

            if (calculated <= 0 ||
                decimal.Round(calculated, 4) != calculated)
            {
                model.Error =
                    "Sonu? 4 ondal?k basamak s?n?r?na uymuyor. " +
                    "Otomatik yuvarlama yap?lmad?.";
                return View(model);
            }

            model.Result = calculated;
            model.Success = true;
        }
        catch (OverflowException)
        {
            model.Error = "Miktar hesaplama s?n?r?n? a??yor.";
        }

        return View(model);
    }
}

public sealed class MCUnitPreviewPage
{
    public decimal? Quantity { get; set; }
    public decimal? Result { get; set; }
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Error { get; set; } = "";
    public bool Success { get; set; }
}
