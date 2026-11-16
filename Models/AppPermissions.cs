namespace MasterChefTakip.Models;

public static class AppPermissions
{
    public const string ClaimType = "permission";

    public const string UsersView = "users.view";
    public const string UsersCreate = "users.create";
    public const string RolesManage = "roles.manage";

    public const string ProductsView = "products.view";
    public const string ProductsPrices = "products.prices";
    public const string ProductsCreate = "products.create";
    public const string ProductsManage = "products.manage";

    public const string PackagingManage = "packaging.manage";

    public const string PalletsView = "pallets.view";
    public const string PalletsLocation = "pallets.location";
    public const string PalletsTransfer = "pallets.transfer";
    public const string PalletsManage = "pallets.manage";

    public const string StockManage = "stock.manage";

    public const string PurchaseManage = "purchase.manage";
    public const string ProductionManage = "production.manage";
    public const string CariManage = "cari.manage";
    public const string FinanceManage = "finance.manage";
    public const string QualityManage = "quality.manage";
    public const string RawMaterialManage = "rawmaterial.manage";

    public const string ShipmentsView = "shipments.view";
    public const string ShipmentsManage = "shipments.manage";
    public const string ShipmentsLabels = "shipments.labels";
    public const string ShipmentsCancel = "shipments.cancel";

    public const string ReturnsView = "returns.view";
    public const string ReturnsReceive = "returns.receive";
    public const string ReturnsInspect = "returns.inspect";
    public const string ReturnsDamage = "returns.damage";
    public const string ReturnsCancel = "returns.cancel";

    public const string ReportsView = "reports.view";

    public const string HistoryView = "history.view";
    public const string HistoryAll = "history.all";

    // Eski kodlarla uyumluluk için korunur.
    // Kullanılabilir yetkiler listesinde bulunmaz.
    public const string OrdersManage = "orders.manage";

    public static IReadOnlyList<(string Value, string Label)> All { get; } =
    [
        (UsersView, "Kullanıcıları görüntüleme (yalnızca yönetici)"),
        (UsersCreate, "Kullanıcı oluşturma (yalnızca yönetici)"),
        (RolesManage, "Rol ve yetki yönetimi (yalnızca yönetici)"),

        (ProductsView, "Ürün bilgilerini görüntüleme"),
        (ProductsPrices, "Ürün fiyatlarını görüntüleme"),
        (ProductsCreate, "Yeni ürün tanımlama"),
        (ProductsManage, "Ürün ve fiyat düzenleme, ürün silme"),

        (PackagingManage, "Paketleme kaydı oluşturma"),

        (PalletsView, "Palet ve stok görüntüleme, barkod okutma"),
        (PalletsLocation, "Paletin konumunu ve notunu düzenleme"),
        (PalletsTransfer, "Ürünleri başka palete taşıma"),
        (PalletsManage, "Palet oluşturma ve aktif/pasif yapma"),
        (StockManage, "Doğrudan stok girişi ve gerekçeli giriş düzeltme"),
        (PurchaseManage, "Satın alma ve mal kabul yönetimi"),
        (ProductionManage, "Üretim yönetimi"),
        (CariManage, "Cari hesap yönetimi"),
        (FinanceManage, "Banka, kasa ve finans yönetimi"),
        (QualityManage, "Lot ve kalite yönetimi"),
        (RawMaterialManage, "Hammadde yönetimi"),


        (ShipmentsView, "Sevkiyatları görüntüleme"),
        (ShipmentsManage, "Sevkiyat hazırlama, onaylama, gönderim ve teslim"),
        (ShipmentsLabels, "Sevkiyat etiketlerini yazdırma"),
        (ShipmentsCancel, "Taslak veya onaylı sevkiyatı iptal etme"),

        (ReturnsView, "İade ve hasar kayıtlarını görüntüleme"),
        (ReturnsReceive, "İade kabul kaydı oluşturma"),
        (ReturnsInspect, "İade kontrolü, miktar düzeltme ve stoğa yerleştirme"),
        (ReturnsDamage, "Depo hasarı kaydetme ve stoktan düşme"),
        (ReturnsCancel, "Bekleyen iade kaydını iptal etme"),

        (ReportsView, "Üretim ve stok raporlarını görüntüleme"),
        (HistoryView, "Kendi işlem geçmişini görüntüleme"),
        (HistoryAll, "Bütün personelin işlem geçmişini görüntüleme")
    ];

    public static bool IsAdministration(string value) =>
        value is UsersView or UsersCreate or RolesManage;

    public static string Group(string value) => value switch
    {
        UsersView or UsersCreate or RolesManage =>
            "Yönetim",

        ProductsView or ProductsPrices or
        ProductsCreate or ProductsManage =>
            "Ürünler",

        PackagingManage or PalletsView or
        PalletsTransfer or PalletsLocation or PalletsManage or StockManage =>
            "Üretim ve depo",

        PurchaseManage => "Satın Alma",

        ProductionManage or RawMaterialManage => "Üretim ve depo",

        CariManage or FinanceManage => "Finans",

        QualityManage => "Kalite",

        ShipmentsView or ShipmentsManage or
        ShipmentsLabels or ShipmentsCancel =>
            "Sevkiyat",

        ReturnsView or ReturnsReceive or ReturnsInspect or
        ReturnsDamage or ReturnsCancel =>
            "İade ve hasar",

        HistoryView or HistoryAll => "İşlem geçmişi",
        ReportsView =>
            "Raporlar",

        _ =>
            "Diğer"
    };
}