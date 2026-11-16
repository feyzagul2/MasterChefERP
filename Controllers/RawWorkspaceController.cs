// MC_CLEAN_RAW_WORKSPACE_2026
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MasterChefTakip.Controllers;

[Authorize]
public class RawWorkspaceController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        ViewData["Title"] = "Hammadde Y\u00f6netimi";
        return View();
    }
}
