using System;
using System.Collections.Generic;
using System.Data;
using MasterChefTakip.Data;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

// MC_ATOMIC_SHIPMENT_SQLITE_V1
public static class MCShipmentAtomicSqliteGuard
{
    private const string TriggerName =
        "MC_Shipment_NoOversell_V1";

    public static void Install(ApplicationDbContext db)
    {
        if (db.Database.ProviderName?.Contains(
                "Sqlite",
                StringComparison.OrdinalIgnoreCase) != true)
        {
            throw new InvalidOperationException(
                "Atomik sevkiyat korumasi SQLite gerektiriyor.");
        }

        var connection = db.Database.GetDbConnection();

        bool openedHere =
            connection.State != ConnectionState.Open;

        if (openedHere)
            db.Database.OpenConnection();

        try
        {
            var columns = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            using (var schemaCommand =
                   connection.CreateCommand())
            {
                schemaCommand.CommandText =
                    "PRAGMA table_info(\"StockMovements\")";

                using var schemaReader =
                    schemaCommand.ExecuteReader();

                while (schemaReader.Read())
                {
                    columns.Add(schemaReader.GetString(1));
                }
            }

            string[] required =
            {
                "PalletItemId",
                "Type",
                "BoxCount",
                "LooseUnitCount",
                "UnitsPerBox"
            };

            foreach (var name in required)
            {
                if (!columns.Contains(name))
                {
                    throw new InvalidOperationException(
                        "SQLite stok tablosunda gerekli alan yok: " +
                        name);
                }
            }

            const string sql = """
                CREATE TRIGGER IF NOT EXISTS
                MC_Shipment_NoOversell_V1

                BEFORE INSERT ON StockMovements

                WHEN NEW.Type = 2

                BEGIN

                    SELECT CASE

                        WHEN NEW.PalletItemId IS NULL
                          OR NEW.PalletItemId <= 0

                          OR NEW.BoxCount IS NULL
                          OR NEW.BoxCount < 0

                          OR NEW.LooseUnitCount IS NULL
                          OR NEW.LooseUnitCount < 0

                          OR NEW.UnitsPerBox IS NULL
                          OR NEW.UnitsPerBox <= 0

                          OR (
                              NEW.BoxCount * NEW.UnitsPerBox +
                              NEW.LooseUnitCount
                          ) <= 0

                        THEN RAISE(
                            ABORT,
                            'MC: GECERSIZ SEVKIYAT MIKTARI'
                        )

                    END;

                    SELECT CASE

                        WHEN EXISTS (
                            SELECT 1
                            FROM StockMovements

                            WHERE PalletItemId =
                                  NEW.PalletItemId

                              AND (
                                  UnitsPerBox IS NULL

                                  OR UnitsPerBox <>
                                     NEW.UnitsPerBox

                                  OR BoxCount IS NULL
                                  OR BoxCount < 0

                                  OR LooseUnitCount IS NULL
                                  OR LooseUnitCount < 0
                              )
                        )

                        THEN RAISE(
                            ABORT,
                            'MC: STOK HAREKETLERI TUTARSIZ'
                        )

                    END;

                    SELECT CASE

                        WHEN COALESCE(
                            (
                                SELECT SUM(

                                    CASE
                                        WHEN Type IN (1,3,6)
                                            THEN 1

                                        WHEN Type IN (2,4,5)
                                            THEN -1

                                        ELSE 0
                                    END

                                    *

                                    (
                                        BoxCount * UnitsPerBox +
                                        LooseUnitCount
                                    )

                                )

                                FROM StockMovements

                                WHERE PalletItemId =
                                      NEW.PalletItemId
                            ),
                            0
                        )

                        <

                        (
                            NEW.BoxCount * NEW.UnitsPerBox +
                            NEW.LooseUnitCount
                        )

                        THEN RAISE(
                            ABORT,
                            'MC: YETERSIZ PALET STOKU'
                        )

                    END;

                END;
                """;

            using var triggerCommand =
                connection.CreateCommand();

            triggerCommand.CommandText = sql;

            triggerCommand.ExecuteNonQuery();
        }
        finally
        {
            if (openedHere)
                db.Database.CloseConnection();
        }
    }
}