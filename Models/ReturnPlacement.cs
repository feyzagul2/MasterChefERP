using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Models;

[Index(nameof(RequestId), IsUnique = true)]
public class ReturnPlacement
{
    public int Id { get; set; }

    // Aynı yerleştirme isteğinin tekrar kaydedilmesini engeller.
    [Required, MaxLength(32)]
    public string RequestId { get; set; }
        = Guid.NewGuid().ToString("N");

    public int ReturnInspectionId { get; set; }

    public ReturnInspection ReturnInspection { get; set; } = null!;

    public int PalletId { get; set; }

    public Pallet Pallet { get; set; } = null!;

    [Range(0, int.MaxValue)]
    public int BoxCount { get; set; }

    [Range(1, int.MaxValue)]
    public int? UnitsPerBox { get; set; }

    [Range(0, int.MaxValue)]
    public int LooseUnitCount { get; set; }

    // Bu yerleştirmeyle oluşan stok kaydı.
    public int PalletItemId { get; set; }

    public PalletItem PalletItem { get; set; } = null!;

    [Required, MaxLength(450)]
    public string CreatedByUserId { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
        = DateTime.UtcNow;

    [MaxLength(500)]
    public string? Notes { get; set; }

    [NotMapped]
    public long TotalUnits =>
        (long)BoxCount * (UnitsPerBox ?? 0)
        + LooseUnitCount;
}