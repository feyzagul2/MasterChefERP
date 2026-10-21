using System.ComponentModel.DataAnnotations;
using MasterChefTakip.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MasterChefTakip.ViewModels;

public class ReturnsIndexViewModel
{
    // Eski ekranın kullandığı form.
    public CreateReturnViewModel Form { get; set; } = new();

    // Yeni çok ürünlü iade formu.
    public CreateReturnReceiptViewModel ReceiptForm { get; set; } = new();

    public WarehouseDamageViewModel DamageForm { get; set; } = new();

    public List<SelectListItem> ShipmentOptions { get; set; } = [];
    public List<SelectListItem> PalletOptions { get; set; } = [];
    public List<SelectListItem> StockOptions { get; set; } = [];

    // Gerçek sevkiyat ve ürün kayıtlarından doldurulacak.
    public List<ReturnShipmentOptionViewModel> AvailableShipments
        { get; set; } = [];

    // Eski iade kayıtları korunur.
    public List<ReturnInspection> Returns { get; set; } = [];

    // Yeni iadeler, ürünleriyle birlikte gösterilir.
    public List<ReturnReceipt> Receipts { get; set; } = [];

    // Hata olduğunda kullanıcının girdiklerini korur.
    public InspectReturnItemViewModel? InspectionForm { get; set; }
    public PlaceReturnItemsViewModel? PlacementForm { get; set; }

    public bool CanInspect { get; set; }
    public bool CanManage { get; set; }
    public bool CanReceive { get; set; }
    public bool CanRecordDamage { get; set; }

    public string? Search { get; set; }
    public ReturnReceiptStatus? StatusFilter { get; set; }

    [DataType(DataType.Date)]
    public DateOnly? StartDate { get; set; }

    [DataType(DataType.Date)]
    public DateOnly? EndDate { get; set; }

    public int Page { get; set; } = 1;
    public int TotalCount { get; set; }
    public int PageSize { get; set; } = 20;
}

// Yeni iade başlığı ve ürün satırları.
public class CreateReturnReceiptViewModel
{
    [Required, StringLength(32, MinimumLength = 32)]
    public string RequestId { get; set; }
        = Guid.NewGuid().ToString("N");

    [Range(1, int.MaxValue)]
    public int ShipmentId { get; set; }

    // Dış paket sayısıdır; stok hesabına katılmaz.
    [Range(0, int.MaxValue)]
    public int PackageCount { get; set; }

    [Required, MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    [Required, MinLength(1), MaxLength(100)]
    public List<CreateReturnReceiptRowViewModel> Rows
        { get; set; } = [new()];
}

public class CreateReturnReceiptRowViewModel
{
    // Ürün, seçilen gerçek sevkiyat satırından alınır.
    [Range(1, int.MaxValue)]
    public int ShipmentItemId { get; set; }

    // Yalnızca aynı üründen oluşan tam koliler.
    [Range(0, int.MaxValue)]
    public int BoxCount { get; set; }

    [Range(1, int.MaxValue)]
    public int? UnitsPerBox { get; set; }

    // Karışık veya eksik kolideki gerçek ürün adedi.
    [Range(0, int.MaxValue)]
    public int AdditionalUnitCount { get; set; }
}

// Sevkiyat seçicisine gönderilecek gerçek veriler.
public class ReturnShipmentOptionViewModel
{
    public int Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string RecipientName { get; set; } = string.Empty;
    public DateTime? ConfirmedAtUtc { get; set; }

    public List<ReturnShipmentLineViewModel> Items
        { get; set; } = [];
}

public class ReturnShipmentLineViewModel
{
    public int ShipmentItemId { get; set; }
    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;

    public int? UnitsPerBox { get; set; }

    // Koli kapasitesi bilinmiyorsa toplam da bilinmeyebilir.
    public long? ShippedUnits { get; set; }
    public long AlreadyReturnedUnits { get; set; }

    public long? RemainingUnits => ShippedUnits.HasValue
        ? Math.Max(
            0L,
            ShippedUnits.Value - AlreadyReturnedUnits)
        : null;
}

// Kontrol ile stok yerleştirmesi ayrı işlemlerdir.
// Kontrol ve palete yerleştirme aynı formda tamamlanır.
public class InspectReturnItemViewModel
{
    [Range(1, int.MaxValue)]
    public int Id { get; set; }

    [Required, StringLength(32, MinimumLength = 32)]
    public string ConcurrencyStamp { get; set; } = string.Empty;

    [Required, Range(0, int.MaxValue)]
    public int? GoodUnits { get; set; }

    [Required, Range(0, int.MaxValue)]
    public int? DamagedUnits { get; set; }

    // Sağlam ürün varsa controller palet seçimini zorunlu tutar.
    [Range(1, int.MaxValue)]
    public int? DestinationPalletId { get; set; }

    // Yalnızca gerçekten kapatılmış sağlam koliler.
    [Range(0, int.MaxValue)]
    public int GoodBoxCount { get; set; }

    [Range(1, int.MaxValue)]
    public int? PlacementUnitsPerBox { get; set; }

    [MaxLength(500)]
    public string? InspectionNotes { get; set; }

    // Eski kodlarla uyumluluk için korunur.
    public bool CompleteInspection { get; set; } = true;
}

// Bir ürünün sağlam miktarı birden fazla palete dağıtılabilir.
public class PlaceReturnItemsViewModel
{
    [Required, StringLength(32, MinimumLength = 32)]
    public string RequestId { get; set; }
        = Guid.NewGuid().ToString("N");

    [Range(1, int.MaxValue)]
    public int ReturnInspectionId { get; set; }

    [Required, StringLength(32, MinimumLength = 32)]
    public string ConcurrencyStamp { get; set; } = string.Empty;

    [Required, MinLength(1), MaxLength(100)]
    public List<ReturnPlacementRowViewModel> Rows
        { get; set; } = [new()];

    [MaxLength(500)]
    public string? Notes { get; set; }
}

public class ReturnPlacementRowViewModel
{
    [Range(1, int.MaxValue)]
    public int PalletId { get; set; }

    [Range(0, int.MaxValue)]
    public int BoxCount { get; set; }

    [Range(1, int.MaxValue)]
    public int? UnitsPerBox { get; set; }

    [Range(0, int.MaxValue)]
    public int LooseUnitCount { get; set; }
}

public class CancelReturnReceiptViewModel
{
    [Range(1, int.MaxValue)]
    public int Id { get; set; }

    [Required, StringLength(32, MinimumLength = 32)]
    public string ConcurrencyStamp { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}

// Geçiş sırasında eski controller ve ekran için korunur.
public class CreateReturnViewModel
{
    [Required, StringLength(32, MinimumLength = 32)]
    public string RequestId { get; set; }
        = Guid.NewGuid().ToString("N");

    [Range(1, int.MaxValue)]
    public int ShipmentItemId { get; set; }

    [Range(1, int.MaxValue)]
    public int ReceivedUnits { get; set; }

    [Required, MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}

public class InspectReturnViewModel
{
    [Range(1, int.MaxValue)]
    public int Id { get; set; }

    [Required, MaxLength(32)]
    public string ConcurrencyStamp { get; set; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int GoodBoxCount { get; set; }

    [Range(0, int.MaxValue)]
    public int GoodUnitCount { get; set; }

    [Range(0, int.MaxValue)]
    public int DamagedUnitCount { get; set; }

    [Range(1, int.MaxValue)]
    public int? DestinationPalletId { get; set; }

    [Required, MaxLength(500)]
    public string InspectionNotes { get; set; } = string.Empty;
}

public class WarehouseDamageViewModel
{
    [Required, StringLength(32, MinimumLength = 32)]
    public string RequestId { get; set; }
        = Guid.NewGuid().ToString("N");

    [Range(1, int.MaxValue)]
    public int PalletItemId { get; set; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }

    [Required, MaxLength(400)]
    public string Reason { get; set; } = string.Empty;
}