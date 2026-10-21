using System.Globalization;

namespace MasterChefTakip.ViewModels;

public class ProductionDashboardViewModel
{
    public DateTime LocalDate { get; set; }

    public bool CanViewStock { get; set; }
    public bool CanViewPackaging { get; set; }
    public bool CanViewShipments { get; set; }
    public bool CanViewReturns { get; set; }
    public bool CanViewProducts { get; set; }

    public DashboardQuantity Stock { get; set; } = new();
    public DashboardQuantity Prepared { get; set; } = new();
    public DashboardQuantity TodayWarehouseDamage { get; set; } = new();

    public int DraftShipments { get; set; }
    public int ReadyShipments { get; set; }
    public int ShippedShipments { get; set; }
    public int DeliveredShipments { get; set; }

    public int PendingReturnLines { get; set; }
    public long PendingReturnUnits { get; set; }
    public long TodayInspectedDamageUnits { get; set; }

    public int LowStockProductCount { get; set; }
    public int UnknownStockProductCount { get; set; }

    public List<DashboardProductRow> Products { get; set; } = [];
    public List<DashboardShipmentRow> PendingShipments { get; set; } = [];
    public List<DashboardReturnRow> PendingReturns { get; set; } = [];
}

public class DashboardQuantity
{
    public long Boxes { get; set; }
    public long OpenUnits { get; set; }
    public long LooseUnits { get; set; }
    public long KnownUnits { get; set; }
    public long UnknownCapacityBoxes { get; set; }

    public bool IsComplete => UnknownCapacityBoxes == 0;

    public void Add(
        int boxes,
        int looseUnits,
        int? capacity,
        int openUnits = 0)
    {
        Boxes += boxes;
        LooseUnits += looseUnits;
        OpenUnits += openUnits;
        KnownUnits += (long)looseUnits + openUnits;

        if (boxes > 0)
        {
            if (capacity is > 0)
                KnownUnits += (long)boxes * capacity.Value;
            else
                UnknownCapacityBoxes += boxes;
        }
    }

    public string Display
    {
        get
        {
            var culture = CultureInfo.GetCultureInfo("tr-TR");

            return IsComplete
                ? KnownUnits.ToString("N0", culture) + " adet"
                : KnownUnits.ToString("N0", culture)
                    + " adet + "
                    + UnknownCapacityBoxes.ToString("N0", culture)
                    + " koli";
        }
    }
}

public class DashboardProductRow
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;

    public DashboardQuantity Stock { get; set; } = new();
    public DashboardQuantity Prepared { get; set; } = new();

    public int? LowStockThresholdUnits { get; set; }

    public bool IsLowStock =>
        LowStockThresholdUnits.HasValue &&
        Stock.IsComplete &&
        Stock.KnownUnits < LowStockThresholdUnits.Value;
}

public class DashboardShipmentRow
{
    public int Id { get; set; }
    public string RecipientName { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

public class DashboardReturnRow
{
    public int Id { get; set; }
    public string RecipientName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ShipmentNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int ReceivedUnits { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}