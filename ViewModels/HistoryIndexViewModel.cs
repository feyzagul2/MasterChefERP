using MasterChefTakip.Models;

namespace MasterChefTakip.ViewModels;

public class HistoryIndexViewModel
{
    public DateOnly Start { get; set; }
    public DateOnly End { get; set; }

    public string Search { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;

    public bool CanViewAll { get; set; }

    public int Page { get; set; } = 1;
    public int TotalCount { get; set; }

    public int TotalPages =>
        Math.Max(1, (TotalCount + 49) / 50);

    public List<HistoryEntry> Rows { get; set; } = [];

    public List<HistoryPersonOption> People { get; set; } = [];
}

public class HistoryPersonOption
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}