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
[Route("MCFinanceDueExport")]
public sealed class MCFinanceDueExportController : Controller
{
    private readonly IConfiguration _configuration;

    public MCFinanceDueExportController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet("Csv")]
    public async Task<IActionResult> Csv(string period = "all")
    {
        if (period != "all" &&
            period != "overdue" &&
            period != "next7" &&
            period != "next30")
        {
            return BadRequest("Geçersiz dönem.");
        }

        var cs = _configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(cs))
            return StatusCode(503, "Veritabanı bağlantısı bulunamadı.");

        var builder = new SqliteConnectionStringBuilder(cs)
        {
            Mode = SqliteOpenMode.ReadOnly
        };

        try
        {
            await using var db =
                new SqliteConnection(builder.ToString());

            await db.OpenAsync();

            await using var cmd = db.CreateCommand();

            var today = DateTime.Today;

            cmd.CommandText = @"
                SELECT
                    m.Id,
                    c.Code,
                    c.CompanyName,
                    m.MovementType,
                    m.MovementDate,
                    m.DueDate,
                    m.Debit,
                    m.Credit,
                    m.Currency,
                    m.ReferenceNumber,
                    m.Description
                FROM MCCariMovements m
                INNER JOIN CariAccounts c
                    ON c.Id = m.CariAccountId
                WHERE m.MovementType IN ('Sale','Purchase')
                  AND m.DueDate IS NOT NULL
                  AND LENGTH(TRIM(m.DueDate)) = 10
                  AND (
                    @period = 'all'
                    OR (
                        @period = 'overdue'
                        AND m.DueDate < @today
                    )
                    OR (
                        @period = 'next7'
                        AND m.DueDate >= @today
                        AND m.DueDate <= @next7
                    )
                    OR (
                        @period = 'next30'
                        AND m.DueDate >= @today
                        AND m.DueDate <= @next30
                    )
                  )
                ORDER BY m.DueDate, m.Id";

            cmd.Parameters.AddWithValue("@period", period);
            cmd.Parameters.AddWithValue(
                "@today",
                today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue(
                "@next7",
                today.AddDays(7).ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue(
                "@next30",
                today.AddDays(30).ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture));

            await using var reader = await cmd.ExecuteReaderAsync();

            var output = new MemoryStream();

            static string CsvCell(string? input)
            {
                var value = (input ?? "")
                    .Replace("\r", " ")
                    .Replace("\n", " ")
                    .Trim();

                if (value.Length > 0)
                {
                    var first = value[0];

                    if (first == '=' ||
                        first == '+' ||
                        first == '-' ||
                        first == '@' ||
                        first == '\t')
                    {
                        value = "'" + value;
                    }
                }

                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            await using (var writer = new StreamWriter(
                output,
                new UTF8Encoding(true),
                8192,
                leaveOpen: true))
            {
                await writer.WriteLineAsync(
                    "Hareket ID;Cari Kodu;Cari Hesap;İşlem;" +
                    "İşlem Tarihi;Vade Tarihi;Gün Farkı;" +
                    "Kayıt Tutarı;Para Birimi;Referans;Açıklama");

                var count = 0;

                while (await reader.ReadAsync())
                {
                    if (++count > 100000)
                    {
                        return BadRequest(
                            "100.000 kayıt sınırı aşıldı. " +
                            "Daha dar dönem seçin.");
                    }

                    var dueText = reader.IsDBNull(5)
                        ? ""
                        : reader.GetString(5);

                    if (!DateTime.TryParseExact(
                        dueText,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var dueDate))
                    {
                        continue;
                    }

                    var type = reader.GetString(3);

                    var amountIndex = type == "Sale" ? 6 : 7;

                    var amountText = Convert.ToString(
                        reader.GetValue(amountIndex),
                        CultureInfo.InvariantCulture) ?? "";

                    if (!decimal.TryParse(
                        amountText,
                        NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture,
                        out var amount) ||
                        amount <= 0)
                    {
                        continue;
                    }

                    var days = (dueDate.Date - today).Days;

                    string ReadText(int index) =>
                        reader.IsDBNull(index)
                            ? ""
                            : Convert.ToString(
                                reader.GetValue(index),
                                CultureInfo.InvariantCulture) ?? "";

                    var cells = new[]
                    {
                        ReadText(0),
                        ReadText(1),
                        ReadText(2),
                        type == "Sale" ? "Satış" : "Alış",
                        ReadText(4),
                        dueDate.ToString("yyyy-MM-dd"),
                        days.ToString(CultureInfo.InvariantCulture),
                        amount.ToString("0.00", CultureInfo.InvariantCulture),
                        ReadText(8),
                        ReadText(9),
                        ReadText(10)
                    };

                    for (var i = 0; i < cells.Length; i++)
                        cells[i] = CsvCell(cells[i]);

                    await writer.WriteLineAsync(string.Join(";", cells));
                }

                await writer.FlushAsync();
            }

            output.Position = 0;

            var filename =
                "MASTER_CHEF_VADE_" +
                period.ToUpperInvariant() + "_" +
                today.ToString("yyyyMMdd", CultureInfo.InvariantCulture) +
                ".csv";

            return File(
                output,
                "text/csv; charset=utf-8",
                filename);
        }
        catch (SqliteException)
        {
            return StatusCode(503, "Vade kayıtları okunamadı.");
        }
    }
}