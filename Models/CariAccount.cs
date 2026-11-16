using System.ComponentModel.DataAnnotations;

namespace MasterChefTakip.Models;

public class CariAccount
{

    // MC_CARI_PRO_2026

    [StringLength(100)]
    public string? AccountGroup { get; set; }

    [StringLength(50)]
    public string? PaymentMethod { get; set; }

    [StringLength(100)]
    public string? PriceListName { get; set; }

    [Range(0, 100)]
    public decimal DefaultDiscountPercent { get; set; }

    [Range(0, 999999999999)]
    public decimal CreditLimit { get; set; }

    [StringLength(30)]
    public string RiskStatus { get; set; } = "Normal";

    [StringLength(150)]
    public string? SalesRepresentative { get; set; }

    [StringLength(1000)]
    public string? PaymentTerms { get; set; }

    [StringLength(30)]
    public string EDocumentType { get; set; } = "Unknown";

    [EmailAddress]
    [StringLength(200)]
    public string? InvoiceEmail { get; set; }

    [StringLength(1000)]
    public string? InvoiceAddress { get; set; }

    public bool IsVatExempt { get; set; }

    [StringLength(300)]
    public string? VatExemptionReason { get; set; }

    [StringLength(1000)]
    public string? DefaultInvoiceNote { get; set; }

    [StringLength(1000)]
    public string? DefaultDispatchNote { get; set; }

    [StringLength(150)]
    public string? PreferredCarrier { get; set; }

    [StringLength(100)]
    public string? DeliveryMethod { get; set; }

    [StringLength(100)]
    public string? DefaultWarehouse { get; set; }

    [StringLength(1000)]
    public string? DeliveryInstructions { get; set; }

    public int Id { get; set; }

    [Required]
    [StringLength(30)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(250)]
    public string CompanyName { get; set; } = string.Empty;

    public bool IsCustomer { get; set; }

    public bool IsSupplier { get; set; }

    [StringLength(150)]
    public string? ContactPerson { get; set; }

    [StringLength(150)]
    public string? TaxOffice { get; set; }

    [StringLength(11)]
    public string? TaxNumber { get; set; }

    [StringLength(30)]
    public string? Phone { get; set; }

    [EmailAddress]
    [StringLength(200)]
    public string? Email { get; set; }

    [StringLength(500)]
    public string? Address { get; set; }

    [StringLength(100)]
    public string? City { get; set; }

    [StringLength(100)]
    public string? District { get; set; }

    [Required]
    [StringLength(3)]
    public string Currency { get; set; } = "TRY";

    [Range(0, 3650)]
    public int PaymentTermDays { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAtUtc { get; set; }

    [Required]
    [StringLength(32)]
    public string ConcurrencyStamp { get; set; }
        = Guid.NewGuid().ToString("N");
}
