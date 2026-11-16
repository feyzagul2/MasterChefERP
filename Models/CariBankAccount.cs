using System.ComponentModel.DataAnnotations;

namespace MasterChefTakip.Models;

public class CariBankAccount
{
    public int Id { get; set; }
    public int CariAccountId { get; set; }

    [Required]
    [StringLength(150)]
    public string BankName { get; set; } = string.Empty;

    [StringLength(150)]
    public string? BranchName { get; set; }

    [Required]
    [StringLength(200)]
    public string AccountHolder { get; set; } = string.Empty;

    [Required]
    [StringLength(34)]
    public string Iban { get; set; } = string.Empty;

    [Required]
    [StringLength(3)]
    public string Currency { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Notes { get; set; }

    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    [Required, StringLength(32)]
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
}
