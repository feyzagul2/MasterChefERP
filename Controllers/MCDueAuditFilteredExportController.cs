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
[Route("MCDueAuditFilteredExport")]
public sealed class MCDueAuditFilteredExportController : Controller
{
    private readonly IConfiguration _configuration;

    public MCDueAuditFilteredExportController(
        IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet("Csv")]
    public async Task<IActionResult> Csv(
        string? search,
        string? username,
        string? dateFrom,
        string? dateTo)
    {
        search = (search ?? "").Trim();
        username = (username ?? "").Trim();

        if (search.Length > 100 || username.Length > 200)
            return BadRequest("Filtre uzunluğu geçersiz.");

        static bool ValidDate(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ||
                DateTime.TryParseExact(
                    value,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out _);
        }

        if (!ValidDate(dateFrom) || !ValidDate(dateTo))
            return BadRequest("Geçersiz tarih biçimi.");

        if (!string.IsNullOrWhiteSpace(dateFrom) &&
            !string.IsNullOrWhiteSpace(dateTo) &&
            string.CompareOrdinal(dateFrom, dateTo) > 0)
        {
            return BadRequest("Başlangıç tarihi bitişten büyük.");
        }

        var connectionString =
            _configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
            return StatusCode(503, "Veritabanı bağlantısı bulunamadı.");

        try
        {
            var builder = new SqliteConnectionStringBuilder(
                connectionString)
            {
                Mode = SqliteOpenMode.ReadOnly
            };

            await using var db =
                new SqliteConnection(builder.ToString());

            await db.OpenAsync();

            await using (var check = db.CreateCommand())
            {
                check.CommandText = @"
                    SELECT COUNT(*)
                    FROM sqlite_master
                    WHERE type='table'
                      AND name IN (
                        'MCCariDueDateAudit',
                        'MCCariMovements',
                        'CariAccounts'
                      )";

                if (Convert.ToInt64(
                    await check.ExecuteScalarAsync()) != 3)
                {
                    return StatusCode(
                        503,
                        "Vade denetim tabloları henüz hazır değil.");
                }
            }

            await using var command = db.CreateCommand();

            command.CommandText = @"
                SELECT
                    a.Id,
                    a.MovementId,
                    COALESCE(c.CompanyName,''),
                    COALESCE(m.MovementType,''),
                    COALESCE(a.OldDueDate,''),
                    a.NewDueDate,
                    a.ChangedAtUtc,
                    a.ChangedBy
                FROM MCCariDueDateAudit a
                LEFT JOIN MCCariMovements m
                    ON m.Id = a.MovementId
                LEFT JOIN CariAccounts c
                    ON c.Id = m.CariAccountId
                WHERE
                    (
                        @search = ''
                        OR INSTR(LOWER(COALESCE(c.CompanyName,'')),
                                 LOWER(@search)) > 0
                        OR INSTR(CAST(a.MovementId AS TEXT),
                                 @search) > 0
                    )
                    AND (
                        @username = ''
                        OR a.ChangedBy = @username
                    )
                    AND (
                        @dateFrom = ''
                        OR SUBSTR(a.ChangedAtUtc,1,10) >= @dateFrom
                    )
                    AND (
                        @dateTo = ''
                        OR SUBSTR(a.ChangedAtUtc,1,10) <= @dateTo
                    )
                ORDER BY a.Id DESC
                LIMIT 100001";

            command.Parameters.AddWithValue("@search", search);
            command.Parameters.AddWithValue("@username", username);
            command.Parameters.AddWithValue(
                "@dateFrom", dateFrom ?? "");
            command.Parameters.AddWithValue(
                "@dateTo", dateTo ?? "");

            await using var reader =
                await command.ExecuteReaderAsync();

            using var output = new MemoryStream();

            using (var writer = new StreamWriter(
                output,
                new UTF8Encoding(true),
                8192,
                leaveOpen: true))
            {
                await writer.WriteLineAsync(
                    "Denetim ID;Hareket ID;Cari Hesap;İşlem;" +
                    "Eski Vade;Yeni Vade;UTC Değişiklik Zamanı;Kullanıcı");

                var count = 0;

                while (await reader.ReadAsync())
                {
                    count++;

                    if (count > 100000)
                    {
                        return BadRequest(
                            "100.000 kayıt sınırı aşıldı. " +
                            "Filtreleri daraltın.");
                    }

                    var cells = new string[8];

                    for (var i = 0; i < cells.Length; i++)
                    {
                        var value = reader.IsDBNull(i)
                            ? ""
                            : Convert.ToString(
                                reader.GetValue(i),
                                CultureInfo.InvariantCulture) ?? "";

                        value = value
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

                        cells[i] =
                            "\"" + value.Replace("\"", "\"\"") + "\"";
                    }

                    await writer.WriteLineAsync(
                        string.Join(";", cells));
                }

                await writer.FlushAsync();
            }

            return File(
                output.ToArray(),
                "text/csv; charset=utf-8",
                "MASTER_CHEF_TUM_GECMIS_FILTRELI_" +
                DateTime.Today.ToString(
                    "yyyyMMdd",
                    CultureInfo.InvariantCulture) +
                ".csv");
        }
        catch (SqliteException)
        {
            return StatusCode(
                503,
                "Vade denetim kayıtları okunamadı.");
        }
    }
}