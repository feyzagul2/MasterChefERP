using System;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MasterChefTakip.Controllers;

// MC_VIRMAN_SERVER_DATE_VALIDATION_V1
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class MCVirmanDateRangeAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var query = context.HttpContext.Request.Query;

        var startText = query["start"].ToString();
        var endText = query["end"].ToString();

        if (!TryReadDate(startText, out var start) ||
            !TryReadDate(endText, out var end))
        {
            context.Result = new BadRequestObjectResult(
                "Tarih biçimi geçersiz. YYYY-AA-GG biçimini kullanın.");

            return;
        }

        if (start.HasValue &&
            end.HasValue &&
            start.Value > end.Value)
        {
            context.Result = new BadRequestObjectResult(
                "Başlangıç tarihi bitiş tarihinden sonra olamaz.");

            return;
        }

        base.OnActionExecuting(context);
    }

    private static bool TryReadDate(
        string? value,
        out DateOnly? result)
    {
        result = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!DateOnly.TryParseExact(
                value.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return false;
        }

        result = parsed;
        return true;
    }
}