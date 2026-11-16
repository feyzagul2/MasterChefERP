using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MasterChefTakip.Controllers;

// MC_STOCK_TRANSFER_CONFIRM_V1
[AttributeUsage(AttributeTargets.Method)]
public sealed class MCStockTransferConfirmationAttribute
    : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var request = context.HttpContext.Request;

        if (!HttpMethods.IsPost(request.Method))
        {
            context.Result = new BadRequestObjectResult(
                "Stok aktar?m? yaln?zca POST iste?iyle yap?labilir.");
            return;
        }

        if (!request.HasFormContentType)
        {
            context.Result = new BadRequestObjectResult(
                "Stok aktar?m? i?in form onay? gereklidir.");
            return;
        }

        var confirmation = request.Form["__mcTransferConfirmed"].ToString();

        if (!string.Equals(
                confirmation,
                "yes",
                StringComparison.Ordinal))
        {
            context.Result = new BadRequestObjectResult(
                "Stok aktar?m? onaylanmad?. " +
                "??lemi ger?ekle?tirmek i?in onay kutusunu i?aretleyin.");
            return;
        }

        await next();
    }
}
