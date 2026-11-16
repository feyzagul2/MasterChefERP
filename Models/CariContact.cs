using System.ComponentModel.DataAnnotations;

namespace MasterChefTakip.Models;

public class CariContact
{
    public int Id { get; set; }
    public int CariAccountId { get; set; }

    [Required]
    [StringLength(150)]
    public string FullName { get; set; } = string.Empty;

    [StringLength(150)]
    public string? JobTitle { get; set; }

    [StringLength(30)]
    public string? Phone { get; set; }

    [StringLength(200)]
    public string? Email { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    [Required, StringLength(32)]
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
}
