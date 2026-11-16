using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace MasterChefTakip.Services;

public static class MCRawReceiptStockService
{
    // MC_REAL_RAW_RECEIPT_STOCK_2026
    //
    // Uses the existing RawMaterialStockMovements table.
    // Must run inside the receipt approval transaction.
    // Does not commit or create its own transaction.
    //
    // movementType must be supplied using the existing
    // application's approved inbound movement type.

    public static async Task<bool> RecordReceiptAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long receiptLineId,
        long rawMaterialId,
        long warehouseId,
        decimal quantity,
        string unit,
        string movementType,
        string referenceNumber,
        string createdByUserId,
        CancellationToken cancellationToken = default)
    {
        if (connection.State != System.Data.ConnectionState.Open)
            throw new InvalidOperationException(
                "SQLite baglantisi acik olmali.");

        if (transaction.Connection != connection)
            throw new InvalidOperationException(
                "Mal kabul transaction bilgisi gerekli.");

        if (receiptLineId <= 0 ||
            rawMaterialId <= 0 ||
            warehouseId <= 0)
            throw new ArgumentException(
                "Mal kabul satiri, stok karti ve depo zorunlu.");

        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Giris miktari sifirdan buyuk olmali.");

        if (string.IsNullOrWhiteSpace(unit) ||
            string.IsNullOrWhiteSpace(movementType) ||
            string.IsNullOrWhiteSpace(createdByUserId))
            throw new ArgumentException(
                "Birim, hareket tipi ve kullanici zorunlu.");

        var operationNumber =
            "MC-RECEIPT-LINE-" +
            receiptLineId.ToString(CultureInfo.InvariantCulture);

        // Confirm that this receipt line is matched to
        // exactly this raw-material stock card.
        await using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;

            check.CommandText = @"
                SELECT COUNT(*)
                FROM MCMaterialReceiptLinks rl
                JOIN MCMaterialStockCardLinks sl
                  ON sl.MaterialCatalogId = rl.MaterialCatalogId
                JOIN RawMaterials r
                  ON r.Id = sl.RawMaterialId
                JOIN RawMaterialWarehouses w
                  ON w.Id = @warehouse
                WHERE rl.PurchaseGoodsReceiptLineId = @line
                  AND sl.RawMaterialId = @raw
                  AND r.IsActive = 1
                  AND w.IsActive = 1";

            check.Parameters.AddWithValue("@line", receiptLineId);
            check.Parameters.AddWithValue("@raw", rawMaterialId);
            check.Parameters.AddWithValue("@warehouse", warehouseId);

            var result = await check.ExecuteScalarAsync(
                cancellationToken);

            if (Convert.ToInt64(result) != 1)
                throw new InvalidOperationException(
                    "Mal kabul, stok karti ve aktif depo "
                    + "eslestirmesi dogrulanamadi.");
        }

        // Idempotency: the same receipt line must not
        // increase stock a second time.
        await using (var duplicate = connection.CreateCommand())
        {
            duplicate.Transaction = transaction;

            duplicate.CommandText = @"
                SELECT COUNT(*)
                FROM RawMaterialStockMovements
                WHERE OperationNumber = @operation";

            duplicate.Parameters.AddWithValue(
                "@operation", operationNumber);

            if (Convert.ToInt64(
                await duplicate.ExecuteScalarAsync(
                    cancellationToken)) > 0)
                return false;
        }

        await using var insert = connection.CreateCommand();

        insert.Transaction = transaction;

        insert.CommandText = @"
            INSERT INTO RawMaterialStockMovements
            (
                RawMaterialId,
                WarehouseId,
                MovementType,
                Quantity,
                Unit,
                OperationNumber,
                ReferenceNumber,
                Notes,
                CreatedByUserId,
                CreatedAtUtc
            )
            VALUES
            (
                @raw,
                @warehouse,
                @type,
                @quantity,
                @unit,
                @operation,
                @reference,
                @notes,
                @user,
                @time
            )";

        insert.Parameters.AddWithValue("@raw", rawMaterialId);
        insert.Parameters.AddWithValue("@warehouse", warehouseId);
        insert.Parameters.AddWithValue("@type", movementType);
        insert.Parameters.AddWithValue("@quantity", quantity);
        insert.Parameters.AddWithValue("@unit", unit.Trim());

        insert.Parameters.AddWithValue(
            "@operation", operationNumber);

        insert.Parameters.AddWithValue(
            "@reference", referenceNumber ?? "");

        insert.Parameters.AddWithValue(
            "@notes", "Mal kabul stok girisi");

        insert.Parameters.AddWithValue(
            "@user", createdByUserId);

        insert.Parameters.AddWithValue(
            "@time", DateTime.UtcNow.ToString("O"));

        try
        {
            return await insert.ExecuteNonQueryAsync(
                cancellationToken) == 1;
        }
        catch (SqliteException ex)
            when (ex.SqliteErrorCode == 19)
        {
            // A unique-index conflict can also mean a different
            // constraint failed. Do not silently treat it
            // as a duplicate; let the caller roll back.
            throw new InvalidOperationException(
                "Mal kabul stok kaydi kisit ihlali. "
                + "Islem geri alinmali.", ex);
        }
    }
}
