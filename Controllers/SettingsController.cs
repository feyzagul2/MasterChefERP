using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MasterChefTakip.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class SettingsController : Controller
{
    [HttpGet]
    public IActionResult Index(string? topic = null)
    {
        string[] topics =
        [
            "general",
            "products",
            "pallets",
            "packaging",
            "shipments",
            "returns",
            "history",
            "reports",
            "users"
        ];

        ViewData["HelpTopic"] =
            topic is not null && topics.Contains(topic)
                ? topic
                : "general";

        return View();
    }
}