using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MasterChefTakip.Controllers
{
    [Authorize]
    public class ModuleHubController : Controller
    {
        [HttpGet]
        public IActionResult Index(string? section)
        {
            // MC_PRODUCTION_SERVER_REDIRECT_2026
            if (string.Equals(
                section,
                "uretim",
                StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction("Index", "Production");
            }


            // MC_QUALITY_SERVER_REDIRECT_2026
            if (string.Equals(
                section,
                "kalite",
                StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction("Index", "LotQuality");
            }

            ViewData["Title"] = "Mod\u00fcl Y\u00f6netimi";
            return View();
        }
    }
}
