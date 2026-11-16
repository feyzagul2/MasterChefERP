using MasterChefTakip.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MasterChefTakip.Controllers;

[Authorize(Policy = AppPermissions.FinanceManage)]
[Route("MCFinancePages")]
public class MCFinancePagesController : Controller
{
    private static readonly Dictionary<string, string> Pages = new()
    {
        ["AlacaklarBorclar"] = "Alacaklar ve Borçlar",
        ["VadeTakibi"] = "Vade Takibi",
        ["FinansDenetimi"] = "Finans Denetimi",
        ["BankaKasa"] = "Banka ve Kasa",
        ["CariHareketler"] = "Cari Hareketler",
        ["VadeRaporlari"] = "Vade Raporları"
    };

    [HttpGet("{page}")]
    public IActionResult Index(string page)
    {
        if (!Pages.TryGetValue(page, out var title))
            return NotFound();

        ViewData["Title"] = title;
        ViewData["FinancePage"] = page;

        return View("~/Views/MCFinancePages/Index.cshtml");
    }
}