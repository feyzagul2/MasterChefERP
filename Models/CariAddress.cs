using System.ComponentModel.DataAnnotations;

namespace MasterChefTakip.Models;

public class CariAddress
{
    public int Id { get; set; }
    public int CariAccountId { get; set; }

    [Required]
    [StringLength(30)]
    public string AddressType { get; set; } = string.Empty;

    [Required]
    [StringLength(150)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(1000)]
    public string AddressLine { get; set; } = string.Empty;

    [StringLength(100)]
    public string? City { get; set; }

    [StringLength(100)]
    public string? District { get; set; }

    [StringLength(20)]
    public string? PostalCode { get; set; }

    [StringLength(100)]
    public string? Country { get; set; }

    [StringLength(150)]
    public string? ContactPerson { get; set; }

    [StringLength(30)]
    public string? Phone { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    [Required, StringLength(32)]
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
}
