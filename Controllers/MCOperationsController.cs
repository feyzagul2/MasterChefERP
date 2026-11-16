using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MasterChefTakip.Controllers;

// MC_OPERATIONS_HUB_V1
[Authorize]
[Route("MCOperations")]
public sealed class MCOperationsController : Controller
{
    [HttpGet("")]
    public IActionResult Index()
    {
        return View();
    }
}