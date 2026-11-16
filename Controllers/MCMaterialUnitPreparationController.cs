using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

namespace MasterChefTakip.Controllers;

[Authorize]
[Route("MCMaterialUnitPreparation")]
public sealed class MCMaterialUnitPreparationController : Controller
{
    private readonly IConfiguration _configuration;
    private readonly Microsoft.AspNetCore.Antiforgery.IAntiforgery _antiforgery;

    public MCMaterialUnitPreparationController(IConfiguration configuration, Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery)
    {
        _configuration = configuration;
        _antiforgery = antiforgery;
    }

    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index(
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        CancellationToken cancellationToken = default)
    {
        q = (q ?? "").Trim();

        if (q.Length > 100)
            return BadRequest("Arama metni cok uzun.");

        page = Math.Clamp(page, 1, 100000);

        var connectionString =
            _configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
            return StatusCode(500, "Veritabani baglantisi bulunamadi.");

        await using var db = new SqliteConnection(connectionString);
        await db.OpenAsync(cancellationToken);

        // MC_SAVED_UNIT_DISPLAY_V4
        await using (var ensure = db.CreateCommand())
        {
            ensure.CommandText = """
                CREATE TABLE IF NOT EXISTS MCMaterialUnits (
                    MaterialCatalogId INTEGER PRIMARY KEY,
                    Unit TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL
                )
                """;

            await ensure.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = db.CreateCommand();

        command.CommandText = """
            SELECT
                Id,
                MaterialCode,
                MaterialName,
                COALESCE(MaterialGroup, ''),
                COALESCE(MaterialCategory, ''),
                COALESCE(VatRate, ''),
                COALESCE(
                    (SELECT u.Unit
                     FROM MCMaterialUnits u
                     WHERE u.MaterialCatalogId = MCMaterialCatalog.Id),
                    ''
                )
            FROM MCMaterialCatalog
            WHERE TRIM(COALESCE(MaterialCode, '')) <> ''
              AND TRIM(COALESCE(MaterialName, '')) <> ''
              AND (
                    @q = ''
                    OR MaterialCode LIKE @like
                    OR MaterialName LIKE @like
                  )
            ORDER BY MaterialCode
            LIMIT 50 OFFSET @offset
            """;

        command.Parameters.AddWithValue("@q", q);
        command.Parameters.AddWithValue(
            "@like",
            "%" + q.Replace("%", "").Replace("_", "") + "%"
        );
        command.Parameters.AddWithValue("@offset", (page - 1) * 50);

        var rows = new List<MaterialRow>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new MaterialRow(
                reader.GetInt64(0),
                reader.IsDBNull(1) ? "" : reader.GetString(1),
                reader.IsDBNull(2) ? "" : reader.GetString(2),
                reader.IsDBNull(3) ? "" : reader.GetString(3),
                reader.IsDBNull(4) ? "" : reader.GetString(4),
                reader.IsDBNull(5) ? "" : reader.GetValue(5).ToString() ?? "",
                reader.IsDBNull(6) ? "" : reader.GetString(6)
            ));
        }

        ViewBag.Query = q;
        ViewBag.Page = page;

        var token = _antiforgery.GetAndStoreTokens(HttpContext).RequestToken ?? "";
        return Content(Render(rows, q, page, token), "text/html; charset=utf-8");
    }

    private static string Render(
        List<MaterialRow> rows, string q, int page, string antiforgeryToken)
    {
        static string H(string value) =>
            System.Net.WebUtility.HtmlEncode(value);

        var html = new System.Text.StringBuilder();

        html.Append("""
            <!doctype html>
            <html lang="tr">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <title>MASTER CHEF - Malzeme Kartlari</title>
            <style>
            body{font-family:Arial,sans-serif;background:#f5f7fb;color:#172033;margin:0;padding:25px}
            main{max-width:1250px;margin:auto;background:white;padding:24px;border-radius:14px}
            h1{font-size:25px;margin-top:0}
            form{display:flex;gap:10px;margin:20px 0}
            input{padding:12px;border:1px solid #cbd5e1;border-radius:8px;flex:1}
            button,.btn{padding:12px 18px;background:#164e8c;color:white;border:0;border-radius:8px;text-decoration:none}
            table{border-collapse:collapse;width:100%}
            th,td{border-bottom:1px solid #e5e7eb;padding:10px;text-align:left}
            th{background:#eef3f9}
            .notice{background:#fff7df;padding:12px;border-radius:8px;margin-bottom:16px}
            .paging{display:flex;gap:15px;margin-top:22px}
            </style>
            </head>
            <body><main>
            <h1>MASTER CHEF - Malzeme Kartlari</h1>
            <div class="notice">
            Bu ekran mevcut malzemeleri listeler.
            Birimler henuz dogrulanmadigi icin stok girisi yapilamaz.
            </div>
            <form method="get">
            <input name="q" placeholder="Malzeme kodu veya adi" value="
            """);

        html.Append(H(q));

        html.Append("""
            ">
            <button type="submit">Ara</button>
            </form>
            <table>
            <thead><tr>
            <th>Stok Kodu</th><th>Malzeme Adi</th>
            <th>Grup</th><th>Kategori</th><th>Kaynak KDV</th><th>Birim</th>
            </tr></thead><tbody>
            """);

        foreach (var row in rows)
        {
            html.Append("<tr><td>");
            html.Append(H(row.Code));
            html.Append("</td><td>");
            html.Append(H(row.Name));
            html.Append("</td><td>");
            html.Append(H(row.Group));
            html.Append("</td><td>");
            html.Append(H(row.Category));
            html.Append("</td><td>");
            html.Append(H(row.Vat));
                        // MC_MATERIAL_UNIT_UI_V3
            html.Append("</td><td>");

            html.Append("<form method='post' action='/MCMaterialUnitPreparation/SaveUnit' style='display:flex;gap:5px;margin:0'>");
            html.Append("<input type='hidden' name='materialId' value='");
            html.Append(row.Id);
            html.Append("'>");

            html.Append("<input type='hidden' name='__RequestVerificationToken' value='");
            html.Append(H(antiforgeryToken));
            html.Append("'>");

            html.Append("<select name='unit' required style='padding:7px;border:1px solid #cbd5e1;border-radius:6px'>");
            html.Append("<option value=''>Birim seciniz</option>");

            foreach (var unit in new[]
            {
                "Kg", "Gr", "Adet", "Litre", "Ml",
                "Koli", "Paket", "Metre", "Rulo", "Takim"
            })
            {
                html.Append("<option value='");
                html.Append(H(unit));
                html.Append("'");

                if (string.Equals(
                    row.Unit,
                    unit,
                    StringComparison.OrdinalIgnoreCase))
                {
                    html.Append(" selected");
                }

                html.Append(">");
                html.Append(H(unit));
                html.Append("</option>");
            }

            html.Append("</select>");
            html.Append("<button type='submit'>Kaydet</button>");
            html.Append("</form>");

                        // MC_STOCK_CARD_UNIT_GUARD_V1
            if (!string.IsNullOrWhiteSpace(row.Unit)
                && string.Equals(row.Group, "HM", StringComparison.OrdinalIgnoreCase)
                && string.Equals(row.Category, "HAMMADDE", StringComparison.OrdinalIgnoreCase))
            {
// MC_MATERIAL_STOCK_BUTTON_V2
            html.Append("<form method='post' action='/MCMaterialUnitPreparation/CreateStockCard' style='margin-top:5px'>");
            html.Append("<input type='hidden' name='materialId' value='");
            html.Append(row.Id);
            html.Append("'>");
            html.Append("<input type='hidden' name='__RequestVerificationToken' value='");
            html.Append(H(antiforgeryToken));
            html.Append("'>");
            html.Append("<button type='submit' style='background:#15803d'>Stok Karti Olustur</button>");
            html.Append("</form>");
            }
            else
            {
                html.Append("<div style='font-size:12px;color:#b45309;margin-top:5px'>");
                html.Append("Once malzeme birimini secip kaydediniz.");
                html.Append("</div>");
            }
            html.Append("</td></tr>");
        }

        html.Append("</tbody></table><div class='paging'>");

        if (page > 1)
        {
            html.Append("<a class='btn' href='?q=");
            html.Append(Uri.EscapeDataString(q));
            html.Append("&page=");
            html.Append(page - 1);
            html.Append("'>Onceki</a>");
        }

        if (rows.Count == 50)
        {
            html.Append("<a class='btn' href='?q=");
            html.Append(Uri.EscapeDataString(q));
            html.Append("&page=");
            html.Append(page + 1);
            html.Append("'>Sonraki</a>");
        }

        html.Append("</div></main></body></html>");

        return html.ToString();
    }

    // MC_MATERIAL_UNIT_SAVE_V1
    [HttpPost("SaveUnit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveUnit(
        [FromForm] long materialId,
        [FromForm] string unit,
        CancellationToken cancellationToken)
    {
        var allowed = new[]
        {
            "Kg", "Gr", "Adet", "Litre", "Ml",
            "Koli", "Paket", "Metre", "Rulo", "Takim"
        };

        if (!allowed.Contains(unit, StringComparer.OrdinalIgnoreCase))
            return BadRequest("Gecersiz birim.");

        var connectionString =
            _configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
            return StatusCode(500, "Veritabani baglantisi bulunamadi.");

        await using var db = new SqliteConnection(connectionString);
        await db.OpenAsync(cancellationToken);

        await using var transaction =
            await db.BeginTransactionAsync(cancellationToken);

        await using (var create = db.CreateCommand())
        {
            create.Transaction = (SqliteTransaction)transaction;

            create.CommandText = """
                CREATE TABLE IF NOT EXISTS MCMaterialUnits (
                    MaterialCatalogId INTEGER PRIMARY KEY,
                    Unit TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL
                )
                """;

            await create.ExecuteNonQueryAsync(cancellationToken);
        }

                // MC_UNIT_STOCK_GUARD_V2
        await using (var unitCheck = db.CreateCommand())
        {
            unitCheck.Transaction = (SqliteTransaction)transaction;
            unitCheck.CommandText = """
                SELECT COUNT(*)
                FROM MCMaterialCatalog c
                INNER JOIN MCMaterialStockCardLinks l
                    ON l.MaterialCatalogId = c.Id
                INNER JOIN RawMaterials r
                    ON r.Id = l.RawMaterialId
                WHERE c.Id = @id
                  AND UPPER(TRIM(r.Unit)) <> UPPER(TRIM(@unit))
                """;
            unitCheck.Parameters.AddWithValue("@id", materialId);
            unitCheck.Parameters.AddWithValue("@unit", unit);

            var conflicts = Convert.ToInt64(
                await unitCheck.ExecuteScalarAsync(cancellationToken) ?? 0L);

            if (conflicts > 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Conflict("Stok karti bulunan malzemenin birimi degistirilemez.");
            }
        }
await using (var save = db.CreateCommand())
        {
            save.Transaction = (SqliteTransaction)transaction;

            save.CommandText = """
                INSERT INTO MCMaterialUnits
                    (MaterialCatalogId, Unit, UpdatedAtUtc)
                SELECT Id, @unit, @now
                FROM MCMaterialCatalog
                WHERE Id = @id
                ON CONFLICT(MaterialCatalogId)
                DO UPDATE SET
                    Unit = excluded.Unit,
                    UpdatedAtUtc = excluded.UpdatedAtUtc
                """;

            save.Parameters.AddWithValue("@id", materialId);
            save.Parameters.AddWithValue("@unit", unit);
            save.Parameters.AddWithValue(
                "@now", DateTime.UtcNow.ToString("O"));

            var affected =
                await save.ExecuteNonQueryAsync(cancellationToken);

            if (affected != 1)
                return NotFound("Malzeme bulunamadi.");
        }

        await transaction.CommitAsync(cancellationToken);

        return RedirectToAction(nameof(Index));
    }
    // MC_MATERIAL_STOCK_CARD_V2
    [HttpPost("CreateStockCard")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateStockCard(
        [FromForm] long materialId,
        CancellationToken cancellationToken)
    {
        var connectionString =
            _configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
            return StatusCode(500, "Veritabani baglantisi bulunamadi.");

        await using var db = new SqliteConnection(connectionString);
        await db.OpenAsync(cancellationToken);

        await using var transaction =
            await db.BeginTransactionAsync(cancellationToken);

        string code;
        string name;
        string unit;
        string category;

        await using (var check = db.CreateCommand())
        {
            check.Transaction = (SqliteTransaction)transaction;

            check.CommandText = """
                SELECT
                    TRIM(c.MaterialCode),
                    TRIM(c.MaterialName),
                    TRIM(u.Unit),
                    COALESCE(c.MaterialCategory, '')
                FROM MCMaterialCatalog c
                INNER JOIN MCMaterialUnits u
                    ON u.MaterialCatalogId = c.Id
                WHERE c.Id = @id
                  -- MC_RAW_MATERIAL_CATEGORY_GUARD_V1
                  AND UPPER(TRIM(COALESCE(c.MaterialType, ''))) = 'HM'
                  AND UPPER(TRIM(COALESCE(c.MaterialCategory, ''))) = 'HAMMADDE'
                  AND TRIM(COALESCE(c.MaterialCode, '')) <> ''
                  AND TRIM(COALESCE(c.MaterialName, '')) <> ''
                  AND TRIM(COALESCE(u.Unit, '')) <> ''
                """;

            check.Parameters.AddWithValue("@id", materialId);

            await using var reader =
                await check.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
                return BadRequest(
                    "Malzeme bulunamadi veya birimi tanimlanmamis.");

            code = reader.GetString(0);
            name = reader.GetString(1);
            unit = reader.GetString(2);
            category = reader.GetString(3);
        }

        await using (var insert = db.CreateCommand())
        {
            insert.Transaction = (SqliteTransaction)transaction;

            insert.CommandText = """
                INSERT INTO RawMaterials
                (
                    Code,
                    Name,
                    Unit,
                    MinimumStock,
                    Category,
                    Notes,
                    IsActive,
                    CreatedAtUtc
                )
                SELECT
                    @code,
                    @name,
                    @unit,
                    '0',
                    @category,
                    @notes,
                    1,
                    @now
                WHERE NOT EXISTS
                (
                    SELECT 1
                    FROM RawMaterials
                    WHERE UPPER(TRIM(Code)) = UPPER(TRIM(@code))
                )
                """;

            insert.Parameters.AddWithValue("@code", code);
            insert.Parameters.AddWithValue("@name", name);
            insert.Parameters.AddWithValue("@unit", unit);
            insert.Parameters.AddWithValue("@category", category);
            insert.Parameters.AddWithValue(
                "@notes", "Katalog kaydi: " + materialId);
            insert.Parameters.AddWithValue(
                "@now", DateTime.UtcNow.ToString("O"));

            var affected =
                await insert.ExecuteNonQueryAsync(cancellationToken);

            if (affected != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Conflict(
                    "Bu kodla stok karti zaten bulunuyor.");
            }
        }

                // MC_STOCK_CARD_LINK_V3
        // Connect the newly created raw material to its catalog record.
        await using (var link = db.CreateCommand())
        {
            link.Transaction = (SqliteTransaction)transaction;

            link.CommandText = """
                INSERT INTO MCMaterialStockCardLinks
                    (
                        MaterialCatalogId,
                        RawMaterialId,
                        MatchedBy,
                        MatchedAtUtc
                    )
                SELECT
                    @catalogId,
                    r.Id,
                    @matchedBy,
                    @matchedAt
                FROM RawMaterials r
                WHERE UPPER(TRIM(r.Code)) = UPPER(TRIM(@code))
                  AND r.Unit = @unit
                  AND NOT EXISTS
                  (
                      SELECT 1
                      FROM MCMaterialStockCardLinks x
                      WHERE x.MaterialCatalogId = @catalogId
                         OR x.RawMaterialId = r.Id
                  )
                """;

            link.Parameters.AddWithValue("@catalogId", materialId);
            link.Parameters.AddWithValue("@code", code);
            link.Parameters.AddWithValue("@unit", unit);

            // MC_STOCK_CARD_LINK_FIELDS_V4
            link.Parameters.AddWithValue(
                "@matchedBy",
                User.Identity?.Name ?? "AuthenticatedUser"
            );

            link.Parameters.AddWithValue(
                "@matchedAt",
                DateTime.UtcNow.ToString("O")
            );

            var linked =
                await link.ExecuteNonQueryAsync(cancellationToken);

            if (linked != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Conflict(
                    "Stok karti katalog baglantisi olusturulamadi.");
            }
        }
await transaction.CommitAsync(cancellationToken);

        TempData["Success"] =
            "Stok karti olusturuldu: " + code;

        return RedirectToAction(nameof(Index));
    }
    private sealed record MaterialRow(
        long Id,
        string Code,
        string Name,
        string Group,
        string Category,
        string Vat, string Unit);
}