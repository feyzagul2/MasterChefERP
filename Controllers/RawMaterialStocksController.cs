using MasterChefTakip.Models;
// MC_RAW_STOCK_404_FIX_2026
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MasterChefTakip.Controllers;

[Authorize(Policy = AppPermissions.RawMaterialManage)]
public class RawMaterialStocksController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        return RedirectToAction("Index", "RawMaterials");
    }
}
