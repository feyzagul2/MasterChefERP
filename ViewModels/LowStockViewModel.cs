namespace MasterChefTakip.ViewModels;

public class LowStockViewModel
{
    public List<LowStockProductViewModel> Products { get; set; } = [];

    public List<LowStockProductViewModel> Alerts =>
        Products.Where(p => p.IsLow).ToList();
}

public class LowStockProductViewModel
{
    public int ProductId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string ConcurrencyStamp { get; set; } = string.Empty;

    public int? Threshold { get; set; }

    // Koli büyüklüğü bilinmiyorsa kesin toplam hesaplanmaz.
    public long? TotalUnits { get; set; }

    public bool IsLow =>
        Threshold.HasValue &&
        TotalUnits.HasValue &&
        TotalUnits.Value <= Threshold.Value;
}