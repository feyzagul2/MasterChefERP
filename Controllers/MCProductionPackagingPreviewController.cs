using MasterChefTakip.Models;
using System;
using Microsoft.AspNetCore.Mvc;
using MasterChefTakip.Services;

namespace MasterChefTakip.Controllers;

[ApiController]
[Microsoft.AspNetCore.Authorization.Authorize(Policy = AppPermissions.ProductionManage)]
[Route("mc-production-packaging-preview")]
public sealed class MCProductionPackagingPreviewController : ControllerBase
{
    [HttpGet("calculate")]
    public IActionResult Calculate(
        [FromQuery] decimal producedKg,
        [FromQuery] decimal unitGrams,
        [FromQuery] int unitsPerBox,
        [FromQuery] int boxesPerPallet = 84)
    {
        try
        {
            var result = MCProductionPackagingCalculator.Calculate(
                producedKg,
                unitGrams,
                unitsPerBox,
                boxesPerPallet
            );

            return Ok(result);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
        catch (OverflowException)
        {
            return BadRequest(new
            {
                error = "Uretim miktari desteklenen siniri asiyor."
            });
        }
    }
}