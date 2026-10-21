using MasterChefTakip.Models;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

public partial class ShipmentsController
{
    // Mevcut controller çağrılarıyla uyumluluk için metot adı korunur.
    // Sipariş yönetimi diğer CRM'dedir.
    private async Task<string?> ValidateOrderContentsAsync(
        Shipment shipment,
        IEnumerable<(int StockId, long Boxes, long Units)> outgoing)
    {
        var lines = outgoing.ToList();

        if (lines.Count == 0)
            return "Sevkiyata en az bir ürün ekleyin.";

        if (lines.Any(line =>
            line.StockId <= 0 ||
            line.Boxes < 0 ||
            line.Units < 0 ||
            line.Boxes > int.MaxValue ||
            line.Units > int.MaxValue ||
            (line.Boxes == 0 && line.Units == 0)))
        {
            return "Geçerli ürün ve miktarlar girin.";
        }

        var stockIds = lines
            .Select(line => line.StockId)
            .Distinct()
            .ToList();

        var stocks = await db.PalletItems
            .AsNoTracking()
            .Where(item => stockIds.Contains(item.Id))
            .Select(item => new
            {
                item.Id,
                item.UnitsPerBox,
                ProductIsActive = item.Product.IsActive,
                PalletIsActive = item.Pallet.IsActive,
                PalletIsCancelled = item.Pallet.IsCancelled
            })
            .ToDictionaryAsync(item => item.Id);

        foreach (var line in lines)
        {
            if (!stocks.TryGetValue(line.StockId, out var stock))
                return "Seçilen stok kaydı bulunamadı.";

            if (!stock.ProductIsActive ||
                !stock.PalletIsActive ||
                stock.PalletIsCancelled)
            {
                return "Aktif bir paletten aktif ürün seçin.";
            }

            if (line.Boxes > 0 && stock.UnitsPerBox is not > 0)
            {
                return "Tam koli çıkışı için stok kaydının "
                    + "koli içi adet bilgisi gerekli.";
            }

            var units =
                line.Units +
                line.Boxes * (long)(stock.UnitsPerBox ?? 0);

            if (units <= 0 || units > int.MaxValue)
            {
                return "Satırdaki toplam ürün adedi "
                    + "desteklenen sınırı aşıyor.";
            }
        }

        // Stok yeterliliği ve aynı stoktan yapılan toplam çıkış,
        // mevcut onay metotlarında ayrıca kontrol edilir.
        return null;
    }
}