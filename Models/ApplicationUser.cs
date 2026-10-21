using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace MasterChefTakip.Models;

public class ApplicationUser : IdentityUser
{
    [Required]
    [MaxLength(100)]
    public string FullName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}