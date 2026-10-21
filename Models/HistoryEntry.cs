using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Models;

[Index(nameof(EventKey), IsUnique = true)]
[Index(nameof(CreatedAtUtc))]
[Index(nameof(UserId), nameof(CreatedAtUtc))]
public class HistoryEntry
{
    public int Id { get; set; }

    [Required, MaxLength(150)]
    public string EventKey { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string ProductCode { get; set; } = string.Empty;

    public string Quantity { get; set; } = string.Empty;

    public string Reference { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public string BeforeValue { get; set; } = string.Empty;

    public string AfterValue { get; set; } = string.Empty;

    public string SearchText { get; set; } = string.Empty;

    // Bağlantılar silinse de geçmiş kaydı korunur.
    public int? PalletId { get; set; }

    public int? ShipmentId { get; set; }

    public int? ReturnReceiptId { get; set; }
}