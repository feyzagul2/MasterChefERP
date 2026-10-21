namespace MasterChefTakip.Models;

public static class AppRoles
{
    public const string Admin = "Yonetici";
    public const string Packaging = "Paketleme";
    public const string Control = "Kontrol";
    public const string Shipping = "Sevkiyat";

    public static readonly string[] All =
    [
        Admin,
        Packaging,
        Control,
        Shipping
    ];
}