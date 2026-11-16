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
[Route("MCBankCashExport")]
public sealed class MCBankCashExportController : Controller
{
    private readonly IConfiguration _configuration;

    public MCBankCashExportController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private static string Csv(string? source)
    {
        var value = source ?? "";

        value = value.Replace("\r", " ").Replace("\n", " ");

        // Excel'de formül çalıştırılmasını önle.
        if (value.Length > 0 &&
            ("=+-@\t".Contains(value[0])))
            value = "'" + value;

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        long? accountId,
        string? start,
        string? end)
    {
        var today = DateTime.Today;

        if (string.IsNullOrWhiteSpace(start))
            start = new DateTime(today.Year, today.Month, 1)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (string.IsNullOrWhiteSpace(end))
            end = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (!DateTime.TryParseExact(
            start,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var startDate))
            return BadRequest("Başlangıç tarihi geçersiz.");

        if (!DateTime.TryParseExact(
            end,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var endDate))
            return BadRequest("Bitiş tarihi geçersiz.");

        if (startDate > endDate)
            return BadRequest("Tarih aralığı geçersiz.");

        if (accountId.HasValue && accountId.Value <= 0)
            return BadRequest("Hesap seçimi geçersiz.");

        var cs = _configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(cs))
            return StatusCode(500, "Veritabanı bağlantısı bulunamadı.");

        await using var db = new SqliteConnection(cs);
        await db.OpenAsync();

        await using (var check = db.CreateCommand())
        {
            check.CommandText = @"
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type = 'table'
                  AND name IN (
                    'MCBankCashAccounts',
                    'MCBankCashMovements'
                  )";

            if (Convert.ToInt64(await check.ExecuteScalarAsync()) != 2)
                return NotFound("Henüz kasa-banka hareketi bulunmuyor.");
        }

        if (accountId.HasValue)
        {
            await using var check = db.CreateCommand();

            check.CommandText = @"
                SELECT COUNT(*)
                FROM MCBankCashAccounts
                WHERE Id = @id";

            check.Parameters.AddWithValue("@id", accountId.Value);

            if (Convert.ToInt64(await check.ExecuteScalarAsync()) == 0)
                return BadRequest("Seçilen hesap bulunamadı.");
        }

        await using var cmd = db.CreateCommand();

        cmd.CommandText = @"
            SELECT
                m.MovementDate,
                a.Code,
                a.Name,
                m.Direction,
                m.Amount,
                m.Description,
                m.ReferenceNumber,
                m.RequestToken
            FROM MCBankCashMovements m
            INNER JOIN MCBankCashAccounts a
                ON a.Id = m.AccountId
            WHERE m.MovementDate >= @start
              AND m.MovementDate <= @end
              AND (@accountId IS NULL OR m.AccountId = @accountId)
            ORDER BY m.MovementDate DESC, m.Id DESC";

        cmd.Parameters.AddWithValue("@start", start);
        cmd.Parameters.AddWithValue("@end", end);

        cmd.Parameters.AddWithValue(
            "@accountId",
            accountId.HasValue
                ? (object)accountId.Value
                : DBNull.Value);

        var csv = new StringBuilder();

        csv.AppendLine(
            "Tarih;Hesap Kodu;Hesap Adı;İşlem Türü;Hareket Sınıfı;Tutar (TL);Açıklama;Belge");

        var count = 0;

        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            count++;

            if (count > 50000)
                return BadRequest(
                    "Rapor 50.000 satırı aşıyor. Tarih aralığını daraltın.");

            var rawAmount = reader.GetString(4);

            if (!decimal.TryParse(
                rawAmount,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var amount))
                return StatusCode(
                    500,
                    "Hareket kayıtlarından birinin tutarı geçersiz.");

            var direction = reader.GetString(3);

            var signedAmount = direction switch
            {
                "In" => amount,
                "Out" => -amount,
                _ => throw new InvalidOperationException(
                    "Geçersiz hareket yönü.")
            };

            csv.Append(Csv(reader.GetString(0))).Append(';');
            csv.Append(Csv(reader.GetString(1))).Append(';');
            csv.Append(Csv(reader.GetString(2))).Append(';');

            // MC_EXPORT_TRANSFER_CLASSIFICATION_V1
            var reference = reader.GetString(6);
            var requestToken = reader.GetString(7);

            var isTransfer =
                reference.StartsWith(
                    "VIR-",
                    StringComparison.OrdinalIgnoreCase)
                &&
                (
                    requestToken.EndsWith(
                        ":IN",
                        StringComparison.OrdinalIgnoreCase)
                    ||
                    requestToken.EndsWith(
                        ":OUT",
                        StringComparison.OrdinalIgnoreCase)
                );

            var movementType = isTransfer
                ? (direction == "In" ? "Virman Girişi" : "Virman Çıkışı")
                : (direction == "In" ? "Para Girişi" : "Para Çıkışı");

            csv.Append(Csv(movementType)).Append(';');
            csv.Append(Csv(
                isTransfer ? "Hesaplar Arası Virman" : "Normal Hesap Hareketi"
            )).Append(';');

            csv.Append(Csv(
                signedAmount.ToString(
                    "0.00",
                    CultureInfo.GetCultureInfo("tr-TR"))
            )).Append(';');

            csv.Append(Csv(reader.GetString(5))).Append(';');
            csv.AppendLine(Csv(reader.GetString(6)));
        }

        // Excel Türkçe karakterleri doğru açsın.
        var bom = new UTF8Encoding(true).GetPreamble();
        var content = Encoding.UTF8.GetBytes(csv.ToString());

        using var stream = new MemoryStream();

        stream.Write(bom, 0, bom.Length);
        stream.Write(content, 0, content.Length);

        var filename =
            $"MasterChef_HesapHareketleri_{start}_{end}.csv";

        return File(
            stream.ToArray(),
            "text/csv; charset=utf-8",
            filename);
    }
}