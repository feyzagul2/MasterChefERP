using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace MasterChefTakip.Controllers;

// MC_CARI_WRITE_CURRENCY_GUARD_V2
public sealed class MCCariCurrencyValidationFilter : IAsyncActionFilter
{
    private readonly IConfiguration _configuration;

    public MCCariCurrencyValidationFilter(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        static object? Arg(ActionExecutingContext ctx, string name)
        {
            foreach (var pair in ctx.ActionArguments)
            {
                if (string.Equals(
                    pair.Key,
                    name,
                    StringComparison.OrdinalIgnoreCase))
                    return pair.Value;
            }

            return null;
        }

        static bool Id(object? value, out long result)
        {
            result = 0;

            return value != null &&
                long.TryParse(
                    Convert.ToString(value, CultureInfo.InvariantCulture),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out result) &&
                result > 0;
        }

        static bool EqualCurrency(string? a, string? b)
        {
            return !string.IsNullOrWhiteSpace(a) &&
                   !string.IsNullOrWhiteSpace(b) &&
                   string.Equals(
                       a.Trim(),
                       b.Trim(),
                       StringComparison.OrdinalIgnoreCase);
        }

        static void Reject(
            ActionExecutingContext ctx,
            string message)
        {
            ctx.Result = new BadRequestObjectResult(message);
        }

        var type = Convert.ToString(
            Arg(context, "movementType"),
            CultureInfo.InvariantCulture)?.Trim();

        bool sale = type == "Sale";
        bool purchase = type == "Purchase";
        bool collection = type == "Collection";
        bool payment = type == "Payment";

        if (!sale && !purchase && !collection && !payment)
        {
            await next();
            return;
        }

        if (!Id(Arg(context, "cariId"), out long cariId))
        {
            Reject(context, "Geçerli cari hesap seçilmelidir.");
            return;
        }

        var connectionString =
            _configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Reject(context, "Veritabanı bağlantısı doğrulanamadı.");
            return;
        }

        try
        {
            var builder = new SqliteConnectionStringBuilder(connectionString)
            {
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private
            };

            await using var db =
                new SqliteConnection(builder.ToString());

            await db.OpenAsync();

            string cariCurrency;

            await using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT Currency, IsActive, IsCustomer, IsSupplier
                    FROM CariAccounts
                    WHERE Id = @id";

                cmd.Parameters.AddWithValue("@id", cariId);

                await using var reader =
                    await cmd.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                {
                    Reject(context, "Cari hesap bulunamadı.");
                    return;
                }

                cariCurrency = reader.IsDBNull(0)
                    ? ""
                    : reader.GetString(0);

                bool active =
                    Convert.ToInt64(reader.GetValue(1)) != 0;

                bool customer =
                    Convert.ToInt64(reader.GetValue(2)) != 0;

                bool supplier =
                    Convert.ToInt64(reader.GetValue(3)) != 0;

                if (!active)
                {
                    Reject(context, "Pasif cari hesapla işlem yapılamaz.");
                    return;
                }

                if ((sale || collection) && !customer)
                {
                    Reject(context, "Müşteri hesabı seçilmelidir.");
                    return;
                }

                if ((purchase || payment) && !supplier)
                {
                    Reject(context, "Tedarikçi hesabı seçilmelidir.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(cariCurrency))
                {
                    Reject(context, "Cari hesap para birimi tanımsız.");
                    return;
                }
            }

            // MC_CARI_MOVEMENT_CURRENCY_GUARD_V3
            // A supplied currency must match the selected cari account.
            // The existing Record action remains responsible for its
            // internally calculated movement currency.
            string? suppliedCurrency = null;
            bool currencySupplied = false;

            foreach (var argument in context.ActionArguments)
            {
                if (string.Equals(
                    argument.Key,
                    "currency",
                    StringComparison.OrdinalIgnoreCase))
                {
                    currencySupplied = true;
                    suppliedCurrency = Convert.ToString(
                        argument.Value,
                        CultureInfo.InvariantCulture);
                    break;
                }
            }

            if (!currencySupplied &&
                context.HttpContext.Request.HasFormContentType)
            {
                var form = await context.HttpContext.Request.ReadFormAsync();

                if (form.TryGetValue("currency", out var submitted))
                {
                    currencySupplied = true;
                    suppliedCurrency = submitted.ToString();
                }
            }

            if (currencySupplied &&
                !EqualCurrency(cariCurrency, suppliedCurrency))
            {
                Reject(
                    context,
                    "Para birimi uyuşmazlığı. Cari hesap: " +
                    cariCurrency.Trim() +
                    ", gönderilen işlem: " +
                    (string.IsNullOrWhiteSpace(suppliedCurrency)
                        ? "tanımsız"
                        : suppliedCurrency.Trim()) +
                    ". Kayıt yapılmadı.");
                return;
            }
            if (collection || payment)
            {
                if (!Id(
                    Arg(context, "bankAccountId"),
                    out long bankId))
                {
                    Reject(context, "Banka/kasa hesabı zorunludur.");
                    return;
                }

                await using var cmd = db.CreateCommand();

                cmd.CommandText = @"
                    SELECT Currency, IsActive
                    FROM MCBankCashAccounts
                    WHERE Id = @id";

                cmd.Parameters.AddWithValue("@id", bankId);

                await using var reader =
                    await cmd.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                {
                    Reject(context, "Banka/kasa hesabı bulunamadı.");
                    return;
                }

                string bankCurrency = reader.IsDBNull(0)
                    ? ""
                    : reader.GetString(0);

                bool active =
                    Convert.ToInt64(reader.GetValue(1)) != 0;

                if (!active)
                {
                    Reject(context, "Pasif banka/kasa hesabı kullanılamaz.");
                    return;
                }

                if (!EqualCurrency(cariCurrency, bankCurrency))
                {
                    Reject(
                        context,
                        "Para birimi uyuşmazlığı. Cari: " +
                        cariCurrency.Trim() +
                        ", Banka/Kasa: " +
                        (string.IsNullOrWhiteSpace(bankCurrency)
                            ? "tanımsız"
                            : bankCurrency.Trim()) +
                        ". Kayıt yapılmadı.");

                    return;
                }
            }

            await next();
        }
        catch (SqliteException)
        {
            Reject(
                context,
                "Finans hesabı doğrulanamadı. İşlem kaydedilmedi.");
        }
        catch (ArgumentException)
        {
            Reject(
                context,
                "Veritabanı bağlantısı geçersiz. İşlem kaydedilmedi.");
        }
    }
}