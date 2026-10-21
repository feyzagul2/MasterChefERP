using System.ComponentModel.DataAnnotations;
using MasterChefTakip.Models;

namespace MasterChefTakip.ViewModels;

public enum ShipmentQuantityType
{
    Unit = 1,
    Box = 2
}

public class ShipmentIndexViewModel
{
    public List<Shipment> Shipments { get; set; } = [];
    public Shipment? SelectedShipment { get; set; }

    public List<ShipmentStockOptionViewModel> StockOptions { get; set; } = [];

    public CreateShipmentViewModel Form { get; set; } = new();
    public AddShipmentItemViewModel ItemForm { get; set; } = new();

    public bool ShowForm { get; set; }
}

public class ShipmentStockOptionViewModel
{
    public int Id { get; set; }

    public string Label { get; set; } = string.Empty;

    // Seçilen stok kaydının gerçek koli bilgileri.
    public int? UnitsPerBox { get; set; }

    public int BoxCount { get; set; }

    public int LooseUnitCount { get; set; }

    public int OpenBoxUnitCount { get; set; }
}

public class CreateShipmentViewModel
{
    [Required(ErrorMessage = "Alıcı adını girin.")]
    [MaxLength(200)]
    public string RecipientName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? RecipientPhone { get; set; }

    [MaxLength(500)]
    public string? DeliveryAddress { get; set; }

    [MaxLength(100)]
    public string? CargoCompany { get; set; }

    [MaxLength(100)]
    public string? TrackingNumber { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }
}

public class AddShipmentItemViewModel
{
    [Range(1, int.MaxValue)]
    public int ShipmentId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Ürün ve palet seçin.")]
    public int PalletItemId { get; set; }

    [Required(ErrorMessage = "Adet veya koli seçin.")]
    [EnumDataType(typeof(ShipmentQuantityType))]
    public ShipmentQuantityType? QuantityType { get; set; }

    [Required(ErrorMessage = "Miktarı girin.")]
    [Range(1, int.MaxValue, ErrorMessage = "Miktar en az 1 olmalıdır.")]
    public int? Quantity { get; set; }
}