using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

namespace MasterChefTakip.Controllers;

[Authorize]
[ApiController]
[Route("api/materials")]
public sealed class MCMaterialSearchController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public MCMaterialSearchController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string? q,
        CancellationToken cancellationToken)
    {
        q = (q ?? "").Trim();

        if (q.Length < 2)
            return Ok(Array.Empty<object>());

        if (q.Length > 100)
            return BadRequest("Arama metni en fazla 100 karakter olabilir.");

        var connectionString =
            _configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
            return StatusCode(500, "Veritabani baglantisi tanimli degil.");

        await using var db = new SqliteConnection(connectionString);
        await db.OpenAsync(cancellationToken);

        await using var command = db.CreateCommand();

        command.CommandText = """
            SELECT
                Id,
                Code,
                Name,
                OriginalType,
                GroupCode,
                Category,
                AlternativeName,
                VatRate,
                RequiresClassification
            FROM MCActiveMaterialSearch
            WHERE Code LIKE @search ESCAPE '\'
               OR Name LIKE @search ESCAPE '\'
            ORDER BY
                CASE WHEN Code = @exact THEN 0 ELSE 1 END,
                Code
            LIMIT 30
            """;

        var escaped = q
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");

        command.Parameters.AddWithValue("@search", "%" + escaped + "%");
        command.Parameters.AddWithValue("@exact", q);

        var results = new List<object>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new
            {
                id = reader.GetInt64(0),
                code = reader.IsDBNull(1) ? "" : reader.GetString(1),
                name = reader.IsDBNull(2) ? "" : reader.GetString(2),
                originalType = reader.IsDBNull(3) ? "" : reader.GetString(3),
                group = reader.IsDBNull(4) ? "" : reader.GetString(4),
                category = reader.IsDBNull(5) ? "" : reader.GetString(5),
                alternativeName = reader.IsDBNull(6) ? "" : reader.GetString(6),
                vatRate = reader.IsDBNull(7) ? "" : reader.GetValue(7).ToString(),
                requiresClassification = reader.GetInt64(8) == 1
            });
        }

        return Ok(results);
    }
}