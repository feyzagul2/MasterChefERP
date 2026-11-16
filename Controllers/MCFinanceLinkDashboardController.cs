using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MasterChefTakip.Controllers;

// MC_FINANCE_DASHBOARD_V11
[Authorize(Roles = "Admin")]
[Route("MCFinanceLinkAudit/Dashboard")]
public sealed class MCFinanceLinkDashboardController : Controller
{
    [HttpGet("")]
    public IActionResult Index()
    {
        return View("~/Views/MCFinanceLinkDashboard/Index.cshtml");
    }
}