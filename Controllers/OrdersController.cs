using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MasterChefTakip.Controllers;

// Sipariş yönetimi diğer CRM'de yapılır.
// Bu controller eski adreslere erişimi kapatır.
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class OrdersController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        return NotFound();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Create()
    {
        return NotFound();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult CreateShipment()
    {
        return NotFound();
    }
}