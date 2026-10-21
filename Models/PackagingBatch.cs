using System.ComponentModel.DataAnnotations;

namespace MasterChefTakip.Models;

public class PackagingBatch
{
    [MaxLength(32)]
    public string Id { get; set; } = string.Empty;

    [Required]
    [MaxLength(450)]
    public string CreatedByUserId { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}