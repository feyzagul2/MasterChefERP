using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Mvc;

// MC_SHIPMENT_INTEGRATION_EXPORT_FIXED_V2
public sealed partial class MCProductionPlanningController
{
    private static string ShipmentCsvCell(object? value)
    {
        var text = Convert.ToString(
            value, CultureInfo.InvariantCulture) ?? "";

        if (text.Length > 0 && "=+-@\t\r\n".Contains(text[0]))
            text = "'" + text;

        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }

    [HttpGet("shipmentExport")]
    public IActionResult ExportShipments(string? shipmentSearch = null)
    {
        using var db = Open();

        var required = new[]
        {
            "Id",
            "RelatedShipmentId",
            "PalletItemId",
            "Type",
            "BoxCount",
            "LooseUnitCount",
            "UnitsPerBox"
        };

        var columns = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        using (var schemaCmd = db.CreateCommand())
        {
            schemaCmd.CommandText =
                "PRAGMA table_info(\"StockMovements\")";

            using var schemaReader = schemaCmd.ExecuteReader();

            while (schemaReader.Read())
                columns.Add(schemaReader.GetString(1));
        }

        if (!required.All(columns.Contains))
            return BadRequest(
                "Stok hareketi tablosunun gerekli alanlari bulunamadi.");

        shipmentSearch = (shipmentSearch ?? "").Trim();

        if (shipmentSearch.Length > 40)
            return BadRequest("Arama bilgisi cok uzun.");

        var csv = new StringBuilder();

        csv.AppendLine(
            "SevkiyatId,HareketId,PaletKalemiId,HareketTuru," +
            "Koli,AcikAdet,KolideAdet,ToplamAdet,NetEtkiAdet,Durum");

        using var movementCmd = db.CreateCommand();

        movementCmd.CommandText = """
            SELECT
                RelatedShipmentId,
                Id,
                PalletItemId,
                Type,
                BoxCount,
                LooseUnitCount,
                UnitsPerBox
            FROM StockMovements
            WHERE RelatedShipmentId IS NOT NULL
              AND Type IN (2,3,6)
              AND (
                  $search = ''
                  OR CAST(RelatedShipmentId AS TEXT)
                     LIKE '%' || $search || '%'
              )
            ORDER BY RelatedShipmentId DESC, Id DESC
            LIMIT 50000
            """;

        movementCmd.Parameters.AddWithValue(
            "$search", shipmentSearch);

        using var movementReader = movementCmd.ExecuteReader();

        while (movementReader.Read())
        {
            decimal boxes;
            decimal loose;
            decimal perBox;
            int movementType;

            try
            {
                movementType =
                    Convert.ToInt32(movementReader.GetValue(3));

                boxes = movementReader.IsDBNull(4)
                    ? -1m
                    : Convert.ToDecimal(movementReader.GetValue(4));

                loose = movementReader.IsDBNull(5)
                    ? -1m
                    : Convert.ToDecimal(movementReader.GetValue(5));

                perBox = movementReader.IsDBNull(6)
                    ? 0m
                    : Convert.ToDecimal(movementReader.GetValue(6));
            }
            catch
            {
                movementType = 0;
                boxes = -1m;
                loose = -1m;
                perBox = -1m;
            }

            var valid =
                boxes >= 0m &&
                loose >= 0m &&
                (boxes == 0m || perBox > 0m) &&
                (movementType == 2 ||
                 movementType == 3 ||
                 movementType == 6);

            decimal totalUnits = 0m;

            if (valid)
            {
                try
                {
                    totalUnits = checked(boxes * perBox + loose);
                }
                catch
                {
                    valid = false;
                }
            }

            var movementName = movementType switch
            {
                2 => "SEVKIYAT",
                3 => "IADE",
                6 => "SEVKIYAT_IPTALI",
                _ => "BILINMIYOR"
            };

            var netEffect = movementType == 2
                ? -totalUnits
                : totalUnits;

            object?[] row =
            {
                movementReader.GetValue(0),
                movementReader.GetValue(1),
                movementReader.GetValue(2),
                movementName,
                valid ? boxes : "",
                valid ? loose : "",
                valid ? perBox : "",
                valid ? totalUnits : "",
                valid ? netEffect : "",
                valid ? "OK" : "MIKTAR_HATASI"
            };

            csv.AppendLine(
                string.Join(",", row.Select(ShipmentCsvCell)));
        }

        var bytes = new UTF8Encoding(true).GetBytes(
            csv.ToString());

        return File(
            bytes,
            "text/csv; charset=utf-8",
            "MASTER_CHEF_Sevkiyat_Aktarim.csv");
    }
}