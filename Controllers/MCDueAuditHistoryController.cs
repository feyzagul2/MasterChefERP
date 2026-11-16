using System;
using System.Collections.Generic;
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
[Route("MCDueAuditHistory")]
public sealed class MCDueAuditHistoryController : Controller
{
    private readonly IConfiguration _configuration;

    public MCDueAuditHistoryController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private async Task<SqliteConnection> OpenReadOnlyAsync()
    {
        var cs = _configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException("Veritabanı bağlantısı yok.");

        var builder = new SqliteConnectionStringBuilder(cs)
        {
            Mode = SqliteOpenMode.ReadOnly
        };

        var db = new SqliteConnection(builder.ToString());

        try
        {
            await db.OpenAsync();
            return db;
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }

    private static async Task<bool> TableExists(SqliteConnection db)
    {
        await using var cmd = db.CreateCommand();

        cmd.CommandText = @"
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type='table'
              AND name='MCCariDueDateAudit'";

        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) == 1;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var rows = new List<DueAuditHistoryRow>();
        var warning = "";

        try
        {
            await using var db = await OpenReadOnlyAsync();

            if (await TableExists(db))
            {
                await using var cmd = db.CreateCommand();

                cmd.CommandText = @"
                    SELECT
                        a.Id,
                        a.MovementId,
                        a.OldDueDate,
                        a.NewDueDate,
                        a.ChangedAtUtc,
                        a.ChangedBy,
                        COALESCE(c.CompanyName,''),
                        COALESCE(m.MovementType,'')
                    FROM MCCariDueDateAudit a
                    LEFT JOIN MCCariMovements m
                        ON m.Id=a.MovementId
                    LEFT JOIN CariAccounts c
                        ON c.Id=m.CariAccountId
                    ORDER BY a.Id DESC
                    LIMIT 200";

                await using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    rows.Add(new DueAuditHistoryRow
                    {
                        Id = reader.GetInt64(0),
                        MovementId = reader.GetInt64(1),
                        OldDate = reader.IsDBNull(2) ? "" : reader.GetString(2),
                        NewDate = reader.GetString(3),
                        ChangedAt = reader.GetString(4),
                        ChangedBy = reader.GetString(5),
                        Company = reader.GetString(6),
                        MovementType = reader.GetString(7)
                    });
                }
            }
            else
            {
                warning = "Henüz kayıtlı vade değişiklik geçmişi yok.";
            }
        }
        catch (Exception ex) when (
            ex is SqliteException ||
            ex is InvalidOperationException ||
            ex is FormatException)
        {
            warning = "Denetim geçmişi okunamadı.";
        }

        ViewBag.Warning = warning;
        return View("~/Views/MCDueAuditHistory/Index.cshtml", rows);
    }

    [HttpGet("Csv")]
    public async Task<IActionResult> Csv()
    {
        try
        {
            await using var db = await OpenReadOnlyAsync();

            if (!await TableExists(db))
                return NotFound("Henüz denetim kaydı bulunmuyor.");

            await using var cmd = db.CreateCommand();

            cmd.CommandText = @"
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
                    ON m.Id=a.MovementId
                LEFT JOIN CariAccounts c
                    ON c.Id=m.CariAccountId
                ORDER BY a.Id DESC
                LIMIT 100001";

            await using var reader = await cmd.ExecuteReaderAsync();

            using var stream = new MemoryStream();

            using (var writer = new StreamWriter(
                stream,
                new UTF8Encoding(true),
                8192,
                leaveOpen: true))
            {
                await writer.WriteLineAsync(
                    "Denetim ID;Hareket ID;Cari Hesap;İşlem;" +
                    "Eski Vade;Yeni Vade;Değişiklik Zamanı UTC;Kullanıcı");

                var count = 0;

                while (await reader.ReadAsync())
                {
                    if (++count > 100000)
                        return BadRequest("100.000 kayıt sınırı aşıldı.");

                    var values = new string[8];

                    for (var i = 0; i < values.Length; i++)
                    {
                        var value = reader.IsDBNull(i)
                            ? ""
                            : Convert.ToString(
                                reader.GetValue(i),
                                CultureInfo.InvariantCulture) ?? "";

                        value = value.Replace("\r", " ")
                                     .Replace("\n", " ")
                                     .Trim();

                        if (value.Length > 0 &&
                            "=+-@".Contains(value[0]))
                        {
                            value = "'" + value;
                        }

                        values[i] = "\"" +
                            value.Replace("\"", "\"\"") + "\"";
                    }

                    await writer.WriteLineAsync(
                        string.Join(";", values));
                }

                await writer.FlushAsync();
            }

            return File(
                stream.ToArray(),
                "text/csv; charset=utf-8",
                "MASTER_CHEF_VADE_DENETIM_" +
                    DateTime.Today.ToString("yyyyMMdd") + ".csv");
        }
        catch (Exception ex) when (
            ex is SqliteException ||
            ex is InvalidOperationException)
        {
            return StatusCode(503, "Denetim raporu hazırlanamadı.");
        }
    }
}

public sealed class DueAuditHistoryRow
{
    public long Id { get; set; }
    public long MovementId { get; set; }
    public string Company { get; set; } = "";
    public string MovementType { get; set; } = "";
    public string OldDate { get; set; } = "";
    public string NewDate { get; set; } = "";
    public string ChangedAt { get; set; } = "";
    public string ChangedBy { get; set; } = "";
}