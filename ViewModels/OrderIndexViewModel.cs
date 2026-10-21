using System.ComponentModel.DataAnnotations;
using MasterChefTakip.Models;

namespace MasterChefTakip.ViewModels;

public enum OrderQuantityType
{
    Unit = 1,
    Box = 2
}

public class OrderIndexViewModel
{
    public CreateOrderViewModel Form { get; set; } = new();

    public List<Product> Products { get; set; } = [];

    public List<OrderListRowViewModel> Orders { get; set; } = [];
}

public class CreateOrderViewModel
{
    [Required, StringLength(32, MinimumLength = 32)]
    public string RequestId { get; set; }
        = Guid.NewGuid().ToString("N");

    [Required(ErrorMessage = "Müşteri adını girin.")]
    [MaxLength(200)]
    public string CustomerName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Phone { get; set; }

    [Required(ErrorMessage = "Teslimat adresini girin.")]
    [MaxLength(500)]
    public string DeliveryAddress { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Notes { get; set; }

    [Required, MinLength(1), MaxLength(100)]
    public List<OrderProductRowViewModel> Rows { get; set; }
        = [new()];
}

public class OrderProductRowViewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Ürün seçin.")]
    public int ProductId { get; set; }

    [EnumDataType(
        typeof(OrderQuantityType),
        ErrorMessage = "Adet veya koli seçin.")]
    public OrderQuantityType QuantityType { get; set; }
        = OrderQuantityType.Unit;

    [Required(ErrorMessage = "Miktarı girin.")]
    [Range(
        1,
        int.MaxValue,
        ErrorMessage = "Miktar en az 1 olmalıdır.")]
    public int? Quantity { get; set; }

    // Koli seçiminde kullanıcı değiştirebilir.
    [Range(
        1,
        int.MaxValue,
        ErrorMessage = "Koli içi adet en az 1 olmalıdır.")]
    public int? UnitsPerBox { get; set; }
}

public class OrderListRowViewModel
{
    public SalesOrder Order { get; set; } = null!;

    public int? ShipmentId { get; set; }

    public ShipmentStatus? ShipmentStatus { get; set; }

    public string StatusLabel => ShipmentStatus switch
    {
        MasterChefTakip.Models.ShipmentStatus.Draft
            => "Sevkiyat hazırlanıyor",

        MasterChefTakip.Models.ShipmentStatus.Confirmed
            => "Sevkiyat onaylandı",

        MasterChefTakip.Models.ShipmentStatus.Cancelled
            => "Sevkiyat iptal edildi",

        _ => "Sevkiyat bekliyor"
    };
}