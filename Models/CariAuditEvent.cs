
using System.ComponentModel.DataAnnotations;

namespace MasterChefTakip.Models;

public class CariAuditEvent
{
    public long Id { get; set; }
    public int CariAccountId { get; set; }

    [Required, StringLength(80)]
    public string Action { get; set; } = string.Empty;

    [Required, StringLength(1000)]
    public string Description { get; set; } = string.Empty;

    [StringLength(200)]
    public string? ActorName { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
