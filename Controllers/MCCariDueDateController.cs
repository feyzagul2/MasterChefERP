using System;
using System.Globalization;
using System.Threading.Tasks;
using MasterChefTakip.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace MasterChefTakip.Controllers;

[Authorize(Roles = AppRoles.Admin)]
[Route("MCCariDueDate")]
public sealed class MCCariDueDateController : Controller
{
    private readonly IConfiguration _configuration;

    public MCCariDueDateController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    // MC_DUE_AUDIT_V1
    [HttpPost("Save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(long movementId, string dueDate)
    {
        if (movementId <= 0)
            return BadRequest("Geçersiz hareket numarası.");

        if (!DateTime.TryParseExact(
            dueDate,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsedDate))
        {
            return BadRequest("Vade tarihi geçersiz.");
        }

        if (parsedDate.Year < 2000 || parsedDate.Year > 2100)
            return BadRequest("Vade tarihi izin verilen aralıkta değil.");

        var connectionString =
            _configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
            return StatusCode(503, "Veritabanı bağlantısı bulunamadı.");

        try
        {
            await using var db = new SqliteConnection(connectionString);
            await db.OpenAsync();

            await using var transaction =
                await db.BeginTransactionAsync();

            // Audit table and financial change share the same transaction.
            await using (var create = db.CreateCommand())
            {
                create.Transaction = (SqliteTransaction)transaction;

                create.CommandText = @"
                    CREATE TABLE IF NOT EXISTS MCCariDueDateAudit (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        MovementId INTEGER NOT NULL,
                        OldDueDate TEXT NULL,
                        NewDueDate TEXT NOT NULL,
                        ChangedAtUtc TEXT NOT NULL,
                        ChangedBy TEXT NOT NULL,
                        FOREIGN KEY (MovementId)
                            REFERENCES MCCariMovements(Id)
                    )";

                await create.ExecuteNonQueryAsync();
            }

            string? oldDueDate;

            await using (var check = db.CreateCommand())
            {
                check.Transaction = (SqliteTransaction)transaction;

                check.CommandText = @"
                    SELECT DueDate
                    FROM MCCariMovements
                    WHERE Id = @id
                      AND MovementType IN ('Sale','Purchase')";

                check.Parameters.AddWithValue("@id", movementId);

                var existing = await check.ExecuteScalarAsync();

                if (existing is null)
                {
                    await transaction.RollbackAsync();

                    TempData["MCFinanceDueMessage"] =
                        "İşlem bulunamadı veya vade düzenlemesine uygun değil.";

                    return RedirectToAction("Index", "MCFinanceOverview");
                }

                oldDueDate = existing == DBNull.Value
                    ? null
                    : Convert.ToString(
                        existing,
                        CultureInfo.InvariantCulture);

                if (!string.IsNullOrWhiteSpace(oldDueDate))
                {
                    await transaction.RollbackAsync();

                    TempData["MCFinanceDueMessage"] =
                        "Bu işlemin vade tarihi daha önce kaydedilmiş. Değişiklik yapılmadı.";

                    return RedirectToAction("Index", "MCFinanceOverview");
                }
            }

            var newDueDate = parsedDate.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture);

            await using (var update = db.CreateCommand())
            {
                update.Transaction = (SqliteTransaction)transaction;

                update.CommandText = @"
                    UPDATE MCCariMovements
                    SET DueDate = @due
                    WHERE Id = @id
                      AND MovementType IN ('Sale','Purchase')
                      AND (DueDate IS NULL OR TRIM(DueDate) = '')";

                update.Parameters.AddWithValue("@due", newDueDate);
                update.Parameters.AddWithValue("@id", movementId);

                var affected = await update.ExecuteNonQueryAsync();

                if (affected != 1)
                {
                    await transaction.RollbackAsync();

                    TempData["MCFinanceDueMessage"] =
                        "Vade değiştirilemedi. Kayıt yeniden kontrol edilmeli.";

                    return RedirectToAction("Index", "MCFinanceOverview");
                }
            }

            var username = User.Identity?.Name;

            if (string.IsNullOrWhiteSpace(username))
                username = "Admin";

            await using (var audit = db.CreateCommand())
            {
                audit.Transaction = (SqliteTransaction)transaction;

                audit.CommandText = @"
                    INSERT INTO MCCariDueDateAudit
                    (
                        MovementId,
                        OldDueDate,
                        NewDueDate,
                        ChangedAtUtc,
                        ChangedBy
                    )
                    VALUES
                    (
                        @movement,
                        @old,
                        @new,
                        @timestamp,
                        @user
                    )";

                audit.Parameters.AddWithValue("@movement", movementId);

                audit.Parameters.AddWithValue(
                    "@old",
                    (object?)oldDueDate ?? DBNull.Value);

                audit.Parameters.AddWithValue("@new", newDueDate);

                audit.Parameters.AddWithValue(
                    "@timestamp",
                    DateTime.UtcNow.ToString(
                        "O",
                        CultureInfo.InvariantCulture));

                audit.Parameters.AddWithValue("@user", username);

                var recorded = await audit.ExecuteNonQueryAsync();

                if (recorded != 1)
                {
                    await transaction.RollbackAsync();
                    return StatusCode(500, "Denetim kaydı oluşturulamadı.");
                }
            }

            await transaction.CommitAsync();

            TempData["MCFinanceDueMessage"] =
                "Hareket #" + movementId +
                " için vade tarihi kaydedildi: " +
                parsedDate.ToString("dd.MM.yyyy") +
                ". Değişiklik denetim kaydına işlendi.";

            return RedirectToAction("Index", "MCFinanceOverview");
        }
        catch (SqliteException)
        {
            return StatusCode(
                503,
                "Vade değişikliği kaydedilemedi. Veritabanı işlemi başarısız.");
        }
    }
}