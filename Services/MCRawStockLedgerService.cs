using System;
using System.Data;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace MasterChefTakip.Services;

public static class MCRawStockLedgerService
{
    // MC_RAW_STOCK_LEDGER_2026
    // This method must be called inside the caller's SQLite transaction.
    // No independent commit is performed here.

    public static async Task<bool> RecordAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int rawMaterialId,
        int warehouseId,
        decimal quantity,
        string direction,
        string sourceType,
        string sourceId,
        string sourceLineId,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        if (connection.State != ConnectionState.Open)
            throw new InvalidOperationException(
                "Veritabani baglantisi acik olmali.");

        if (transaction.Connection != connection)
            throw new InvalidOperationException(
                "Gecerli bir SQLite transaction gerekli.");

        if (rawMaterialId <= 0 || warehouseId <= 0)
            throw new ArgumentException(
                "Stok karti ve depo secilmelidir.");

        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Miktar sifirdan buyuk olmali.");

        if (direction != "IN" && direction != "OUT")
            throw new ArgumentException(
                "Hareket tipi IN veya OUT olmali.");

        if (string.IsNullOrWhiteSpace(sourceType) ||
            string.IsNullOrWhiteSpace(sourceId) ||
            string.IsNullOrWhiteSpace(sourceLineId))
            throw new ArgumentException(
                "Kaynak belge ve satir bilgileri zorunludur.");

        sourceType = sourceType.Trim();
        sourceId = sourceId.Trim();
        sourceLineId = sourceLineId.Trim();

        if (sourceType.Length > 60 ||
            sourceId.Length > 120 ||
            sourceLineId.Length > 120)
            throw new ArgumentException(
                "Kaynak belge kimligi cok uzun.");

        await using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;

            check.CommandText = @"
                SELECT
                    (SELECT COUNT(*)
                     FROM RawMaterials
                     WHERE Id=@raw AND IsActive=1),
                    (SELECT COUNT(*)
                     FROM RawMaterialWarehouses
                     WHERE Id=@warehouse AND IsActive=1)";

            check.Parameters.AddWithValue("@raw", rawMaterialId);
            check.Parameters.AddWithValue("@warehouse", warehouseId);

            await using var reader =
                await check.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken) ||
                reader.GetInt64(0) != 1 ||
                reader.GetInt64(1) != 1)
                throw new InvalidOperationException(
                    "Aktif stok karti veya depo bulunamadi.");
        }

        await using (var duplicate = connection.CreateCommand())
        {
            duplicate.Transaction = transaction;

            duplicate.CommandText = @"
                SELECT Id, RawMaterialId, WarehouseId,
                       Direction, Quantity
                FROM MCRawStockLedger
                WHERE SourceType=@type
                  AND SourceId=@source
                  AND SourceLineId=@line
                  AND Direction=@direction";

            duplicate.Parameters.AddWithValue("@type", sourceType);
            duplicate.Parameters.AddWithValue("@source", sourceId);
            duplicate.Parameters.AddWithValue("@line", sourceLineId);
            duplicate.Parameters.AddWithValue("@direction", direction);

            await using var reader =
                await duplicate.ExecuteReaderAsync(cancellationToken);

            if (await reader.ReadAsync(cancellationToken))
            {
                var oldQuantity = decimal.Parse(
                    reader.GetString(4),
                    CultureInfo.InvariantCulture);

                if (reader.GetInt64(1) != rawMaterialId ||
                    reader.GetInt64(2) != warehouseId ||
                    reader.GetString(3) != direction ||
                    oldQuantity != quantity)
                    throw new InvalidOperationException(
                        "Ayni belge satiri farkli bilgilerle kaydedilmis.");

                // Identical movement already registered.
                return false;
            }
        }

        if (direction == "OUT")
        {
            decimal balance = 0;

            await using var balanceCommand =
                connection.CreateCommand();

            balanceCommand.Transaction = transaction;

            balanceCommand.CommandText = @"
                SELECT Direction, Quantity
                FROM MCRawStockLedger
                WHERE RawMaterialId=@raw
                  AND WarehouseId=@warehouse";

            balanceCommand.Parameters.AddWithValue(
                "@raw", rawMaterialId);

            balanceCommand.Parameters.AddWithValue(
                "@warehouse", warehouseId);

            await using var reader =
                await balanceCommand.ExecuteReaderAsync(
                    cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var amount = decimal.Parse(
                    reader.GetString(1),
                    CultureInfo.InvariantCulture);

                balance += reader.GetString(0) == "IN"
                    ? amount
                    : -amount;
            }

            if (balance < quantity)
                throw new InvalidOperationException(
                    "Yetersiz hammadde stogu.");
        }

        await using var insert = connection.CreateCommand();

        insert.Transaction = transaction;

        insert.CommandText = @"
            INSERT INTO MCRawStockLedger
            (
                RawMaterialId,
                WarehouseId,
                Direction,
                Quantity,
                SourceType,
                SourceId,
                SourceLineId,
                CreatedBy,
                CreatedAtUtc
            )
            VALUES
            (
                @raw,
                @warehouse,
                @direction,
                @quantity,
                @type,
                @source,
                @line,
                @user,
                @created
            )";

        insert.Parameters.AddWithValue(
            "@raw", rawMaterialId);

        insert.Parameters.AddWithValue(
            "@warehouse", warehouseId);

        insert.Parameters.AddWithValue(
            "@direction", direction);

        insert.Parameters.AddWithValue(
            "@quantity",
            quantity.ToString(CultureInfo.InvariantCulture));

        insert.Parameters.AddWithValue(
            "@type", sourceType);

        insert.Parameters.AddWithValue(
            "@source", sourceId);

        insert.Parameters.AddWithValue(
            "@line", sourceLineId);

        insert.Parameters.AddWithValue(
            "@user", createdBy ?? "");

        insert.Parameters.AddWithValue(
            "@created", DateTime.UtcNow.ToString("O"));

        return await insert.ExecuteNonQueryAsync(
            cancellationToken) == 1;
    }
}
