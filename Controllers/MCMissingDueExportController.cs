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
[Route("MCMissingDueExport")]
public sealed class MCMissingDueExportController : Controller
{
    private readonly IConfiguration _configuration;

    public MCMissingDueExportController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet("Csv")]
    public async Task<IActionResult> Csv()
    {
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

            await using var command = db.CreateCommand();

            command.CommandText = @"
                SELECT
                    m.Id,
                    c.Code,
                    c.CompanyName,
                    m.MovementType,
                    m.MovementDate,
                    m.Currency,
                    m.Debit,
                    m.Credit,
                    m.ReferenceNumber,
                    m.Description
                FROM MCCariMovements m
                INNER JOIN CariAccounts c
                    ON c.Id = m.CariAccountId
                WHERE m.MovementType IN ('Sale','Purchase')
                  AND (
                      m.DueDate IS NULL
                      OR TRIM(m.DueDate) = ''
                  )
                ORDER BY m.MovementDate DESC, m.Id DESC
                LIMIT 100001";

            await using var reader = await command.ExecuteReaderAsync();

            var output = new MemoryStream();

            static string Cell(string? input)
            {
                var value = (input ?? "")
                    .Replace("\r", " ")
                    .Replace("\n", " ")
                    .Trim();

                if (value.Length > 0 &&
                    (value[0] == '=' ||
                     value[0] == '+' ||
                     value[0] == '-' ||
                     value[0] == '@' ||
                     value[0] == '\t'))
                {
                    value = "'" + value;
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
                    "Hareket ID;Cari Kodu;Cari Hesap;İşlem Türü;" +
                    "İşlem Tarihi;Para Birimi;Tutar;" +
                    "Referans;Açıklama;Durum");

                var count = 0;

                while (await reader.ReadAsync())
                {
                    count++;

                    if (count > 100000)
                    {
                        return BadRequest(
                            "100.000 kayıt sınırı aşıldı. " +
                            "Rapor bölünerek alınmalıdır.");
                    }

                    string Read(int index)
                    {
                        return reader.IsDBNull(index)
                            ? ""
                            : Convert.ToString(
                                reader.GetValue(index),
                                CultureInfo.InvariantCulture) ?? "";
                    }

                    var type = Read(3);

                    var rawAmount =
                        type == "Sale" ? Read(6) : Read(7);

                    var validAmount = decimal.TryParse(
                        rawAmount,
                        NumberStyles.AllowDecimalPoint |
                        NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture,
                        out var amount);

                    var values = new[]
                    {
                        Read(0),
                        Read(1),
                        Read(2),
                        type == "Sale" ? "Satış" : "Alış",
                        Read(4),
                        Read(5),
                        validAmount && amount > 0
                            ? amount.ToString(
                                "0.00",
                                CultureInfo.InvariantCulture)
                            : "TUTAR KONTROL GEREKLI",
                        Read(8),
                        Read(9),
                        "VADE TARIHI EKSIK"
                    };

                    for (var i = 0; i < values.Length; i++)
                        values[i] = Cell(values[i]);

                    await writer.WriteLineAsync(
                        string.Join(";", values));
                }

                await writer.FlushAsync();
            }

            output.Position = 0;

            var filename =
                "MASTER_CHEF_EKSIK_VADE_" +
                DateTime.Today.ToString(
                    "yyyyMMdd",
                    CultureInfo.InvariantCulture) +
                ".csv";

            return File(
                output,
                "text/csv; charset=utf-8",
                filename);
        }
        catch (SqliteException)
        {
            return StatusCode(
                503,
                "Eksik vade kayıtları okunamadı.");
        }
    }
}