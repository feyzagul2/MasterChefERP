using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MasterChefTakip.Controllers
{
    // MC_FINANCE_ERROR_FILTER_V1
    // Sadece yakalanmamis finans controller hatalarini ele alir.
    // Mevcut transaction, yetki ve is kurallarini degistirmez.

    public sealed class MCFinanceErrorFilter : IExceptionFilter
    {
        private readonly ILogger<MCFinanceErrorFilter> _logger;

        public MCFinanceErrorFilter(
            ILogger<MCFinanceErrorFilter> logger)
        {
            _logger = logger;
        }

        public void OnException(ExceptionContext context)
        {
            var exception = context.Exception;

            _logger.LogError(
                exception,
                "MASTER CHEF finans isleminde beklenmeyen hata. " +
                "Action: {Action}",
                context.ActionDescriptor.DisplayName);

            int status = 500;

            var sqlite = exception as SqliteException
                ?? exception.InnerException as SqliteException;

            if (sqlite != null)
            {
                if (sqlite.SqliteErrorCode == 19)
                    status = 409;

                if (sqlite.SqliteErrorCode == 5 ||
                    sqlite.SqliteErrorCode == 6)
                    status = 503;
            }

            string message = status switch
            {
                409 =>
                    "Finans islemi mevcut kayit veya " +
                    "veritabani kurallariyla cakisti.",

                503 =>
                    "Finans veritabanina su anda erisilemiyor. " +
                    "Lutfen islemi kontrol ederek tekrar deneyin.",

                _ =>
                    "Finans islemi tamamlanamadi. " +
                    "Kayit durumu kontrol edilmelidir."
            };

            var problem = new ProblemDetails
            {
                Status = status,
                Title = "Finans islemi hatasi",
                Detail = message
            };

            context.Result = new ObjectResult(problem)
            {
                StatusCode = status,
                ContentTypes =
                {
                    "application/problem+json"
                }
            };

            context.ExceptionHandled = true;
        }
    }
}