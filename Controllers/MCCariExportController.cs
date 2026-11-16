using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using MasterChefTakip.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace MasterChefTakip.Controllers;

[Authorize(Roles = AppRoles.Admin)]
[Route("MCCariExport")]
public sealed class MCCariExportController : Controller
{
    private readonly IConfiguration _configuration;

    public MCCariExportController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet("Csv")]
    public async Task<IActionResult> Csv(
        string? search,
        string? movementType,
        string? currency,
        string? dateFrom,
        string? dateTo)
    {
        search = (search ?? "").Trim();
        movementType = (movementType ?? "").Trim();
        currency = (currency ?? "").Trim().ToUpperInvariant();

        if (search.Length > 100 ||
            movementType.Length > 30 ||
            currency.Length > 10)
            return BadRequest("Filtre uzunluğu geçersiz.");

        var allowedTypes = new[]
        {
            "Sale", "Purchase", "Collection", "Payment",
            "OpeningDebit", "OpeningCredit", "Correction"
        };

        if (movementType.Length > 0 &&
            Array.IndexOf(allowedTypes, movementType) < 0)
            return BadRequest("İşlem türü geçersiz.");

        static bool TryDate(string? value, out DateTime date)
        {
            date = default;

            return string.IsNullOrWhiteSpace(value) ||
                DateTime.TryParseExact(
                    value,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out date);
        }

        if (!TryDate(dateFrom, out var from) ||
            !TryDate(dateTo, out var to))
            return BadRequest("Tarih biçimi geçersiz.");

        if (!string.IsNullOrWhiteSpace(dateFrom) &&
            !string.IsNullOrWhiteSpace(dateTo) &&
            from > to)
            return BadRequest("Başlangıç tarihi bitişten büyük.");

        var cs = _configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(cs))
            return StatusCode(503, "Veritabanı bağlantısı bulunamadı.");

        var builder = new SqliteConnectionStringBuilder(cs)
        {
            Mode = SqliteOpenMode.ReadOnly
        };

        await using var db = new SqliteConnection(builder.ToString());

        try
        {
            await db.OpenAsync();

            await using var cmd = db.CreateCommand();

            cmd.CommandText = @"
                SELECT
                    m.Id,
                    m.MovementDate,
                    c.Code,
                    c.CompanyName,
                    m.MovementType,
                    m.Debit,
                    m.Credit,
                    m.Currency,
                    m.DueDate,
                    m.Description,
                    m.ReferenceNumber
                FROM MCCariMovements m
                INNER JOIN CariAccounts c
                    ON c.Id = m.CariAccountId
                WHERE
                    (@type = '' OR m.MovementType = @type)
                    AND (@currency = '' OR UPPER(m.Currency) = @currency)
                    AND (@from = '' OR SUBSTR(m.MovementDate,1,10) >= @from)
                    AND (@to = '' OR SUBSTR(m.MovementDate,1,10) <= @to)
                    AND (
                        @search = ''
                        OR INSTR(LOWER(c.CompanyName), LOWER(@search)) > 0
                        OR INSTR(LOWER(c.Code), LOWER(@search)) > 0
                        OR INSTR(LOWER(m.Description), LOWER(@search)) > 0
                        OR INSTR(LOWER(m.ReferenceNumber), LOWER(@search)) > 0
                    )
                ORDER BY m.MovementDate, m.Id";

            cmd.Parameters.AddWithValue("@type", movementType);
            cmd.Parameters.AddWithValue("@currency", currency);
            cmd.Parameters.AddWithValue("@from", dateFrom ?? "");
            cmd.Parameters.AddWithValue("@to", dateTo ?? "");
            cmd.Parameters.AddWithValue("@search", search);

            await using var reader = await cmd.ExecuteReaderAsync();

            var output = new MemoryStream();

            await using (var writer = new StreamWriter(
                output,
                new UTF8Encoding(true),
                8192,
                leaveOpen: true))
            {
                await writer.WriteLineAsync(
                    "ID;Tarih;Cari Kodu;Cari Hesap;İşlem Türü;" +
                    "Borç;Alacak;Para Birimi;Vade;Açıklama;Referans");

                var count = 0;
                const int maxRows = 100000;

                while (await reader.ReadAsync())
                {
                    if (count >= maxRows)
                    {
                        return BadRequest(
                            "Rapor 100.000 satırı aşıyor. Tarih veya cari hesap filtresi uygulayın.");
                    }

                    var values = new string[11];

                    for (var i = 0; i < values.Length; i++)
                    {
                        values[i] = reader.IsDBNull(i)
                            ? ""
                            : Convert.ToString(
                                reader.GetValue(i),
                                CultureInfo.InvariantCulture) ?? "";
                    }

                    for (var i = 0; i < values.Length; i++)
                    {
                        var value = values[i]
                            .Replace("\r", " ")
                            .Replace("\n", " ")
                            .Trim();

                        // Prevent CSV cells being evaluated as spreadsheet formulas.
                        if (value.Length > 0 &&
                            (value[0] == '=' ||
                             value[0] == '+' ||
                             value[0] == '-' ||
                             value[0] == '@' ||
                             value[0] == '\t'))
                        {
                            value = "'" + value;
                        }

                        values[i] = "\"" + value.Replace("\"", "\"\"") + "\"";
                    }

                    await writer.WriteLineAsync(string.Join(";", values));
                    count++;
                }

                await writer.FlushAsync();
            }

            output.Position = 0;

            var filename =
                "MASTER_CHEF_CARI_RAPOR_" +
                DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture) +
                ".csv";

            return File(
                output,
                "text/csv; charset=utf-8",
                filename);
        }
        catch (SqliteException)
        {
            return StatusCode(503, "Cari kayıtları okunamadı.");
        }
    }
}