namespace MasterChefTakip.ViewModels;

public class ReportIndexViewModel
{
    public DateOnly Start { get; set; }
    public DateOnly End { get; set; }
    public string Group { get; set; } = "day";

    public ReportQuantity Prepared { get; set; } = new();
    public ReportQuantity Receipts { get; set; } = new();
    public ReportQuantity Shipments { get; set; } = new();
    public ReportQuantity Returns { get; set; } = new();
    public ReportQuantity Damage { get; set; } = new();
    public ReportQuantity ReceiptCancellations { get; set; } = new();
    public ReportQuantity ShipmentCancellations { get; set; } = new();
    public ReportQuantity CurrentStock { get; set; } = new();
    public List<ReportProductViewModel> CurrentStockProducts { get; set; } = [];

    public int MovementCount { get; set; }

    public List<ReportBucketViewModel> Buckets { get; set; } = [];
    public List<ReportProductViewModel> TopProducts { get; set; } = [];

    public int UnrankedProductCount { get; set; }
}

public class ReportQuantity
{
    public long BoxCount { get; set; }
    public long IndividualCount { get; set; }
    public long KnownUnits { get; set; }
    public long UnknownBoxCount { get; set; }

    public bool IsComplete => UnknownBoxCount == 0;

    public string Display => IsComplete
        ? $"{KnownUnits:N0} adet"
        : $"{KnownUnits:N0} bilinen adet + "
            + $"{UnknownBoxCount:N0} koli (adet bilgisi yok)";

    public void Add(
        int boxCount,
        int individualCount,
        int? unitsPerBox)
    {
        BoxCount = checked(BoxCount + boxCount);
        IndividualCount = checked(IndividualCount + individualCount);
        KnownUnits = checked(KnownUnits + individualCount);

        if (boxCount > 0)
        {
            if (unitsPerBox is > 0)
            {
                KnownUnits = checked(
                    KnownUnits + (long)boxCount * unitsPerBox.Value);
            }
            else
            {
                UnknownBoxCount = checked(UnknownBoxCount + boxCount);
            }
        }
    }
}

public class ReportBucketViewModel
{
    public DateOnly Start { get; set; }
    public string Label { get; set; } = string.Empty;

    public ReportQuantity Prepared { get; set; } = new();
    public ReportQuantity Receipts { get; set; } = new();
    public ReportQuantity Shipments { get; set; } = new();
    public ReportQuantity Returns { get; set; } = new();
    public ReportQuantity Damage { get; set; } = new();
    public ReportQuantity ReceiptCancellations { get; set; } = new();
    public ReportQuantity ShipmentCancellations { get; set; } = new();
}

public class ReportProductViewModel
{
    public int ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;

    public ReportQuantity Quantity { get; set; } = new();
}