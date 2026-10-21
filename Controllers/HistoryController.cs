using System.Security.Claims;
using MasterChefTakip.Data;
using MasterChefTakip.Models;
using MasterChefTakip.Services;
using MasterChefTakip.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MasterChefTakip.Controllers;

[Authorize(Policy = AppPermissions.HistoryView)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class HistoryController(
    ApplicationDbContext db,
    ILogger<HistoryController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        string? search = null,
        string? category = null,
        string? userId = null,
        DateOnly? start = null,
        DateOnly? end = null,
        int page = 1)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(currentUserId))
            return Challenge();

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

        var model = new HistoryIndexViewModel
        {
            Start = start ?? today.AddDays(-6),
            End = end ?? today,
            Search = search?.Trim() ?? string.Empty,
            Category = category?.Trim() ?? string.Empty,
            CanViewAll = OperationAuthorization.Can(
                User, AppPermissions.HistoryAll),
            Page = Math.Max(1, page)
        };

        model.UserId = model.CanViewAll
            ? userId ?? string.Empty
            : currentUserId;

        if (model.Start > model.End)
            ModelState.AddModelError("", "Başlangıç tarihi bitişten sonra olamaz.");

        if (model.End > today)
            ModelState.AddModelError("", "Bitiş tarihi bugünden sonra olamaz.");

        if (model.End.DayNumber - model.Start.DayNumber > 365)
            ModelState.AddModelError("", "En fazla 366 günlük aralık seçin.");

        var permitted = db.HistoryEntries.AsNoTracking();

        // Filtreyi değiştirmek başkasının kayıtlarına erişim sağlamaz.
        if (!model.CanViewAll)
            permitted = permitted.Where(r => r.UserId == currentUserId);

        if (model.CanViewAll)
        {
            var people = await permitted
                .Where(r => r.UserId != "")
                .Select(r => new { r.UserId, r.UserName })
                .Distinct()
                .ToListAsync();

            model.People = people
                .GroupBy(p => p.UserId)
                .Select(g => new HistoryPersonOption
                {
                    Id = g.Key,
                    Name = g.First().UserName
                })
                .OrderBy(p => p.Name)
                .ToList();
        }

        if (!ModelState.IsValid)
            return View(model);

        var from = DateTime.SpecifyKind(
            model.Start.ToDateTime(TimeOnly.MinValue).AddHours(-3),
            DateTimeKind.Utc);

        var until = DateTime.SpecifyKind(
            model.End.AddDays(1).ToDateTime(TimeOnly.MinValue).AddHours(-3),
            DateTimeKind.Utc);

        var query = permitted.Where(r =>
            r.CreatedAtUtc >= from &&
            r.CreatedAtUtc < until);

        if (!string.IsNullOrWhiteSpace(model.UserId))
            query = query.Where(r => r.UserId == model.UserId);

        if (!string.IsNullOrWhiteSpace(model.Category))
            query = query.Where(r => r.Category == model.Category);

        foreach (var word in HistoryWriter.Normalize(model.Search)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(10))
        {
            var term = word;
            query = query.Where(r => r.SearchText.Contains(term));
        }

        model.TotalCount = await query.CountAsync();
        model.Page = Math.Min(model.Page, model.TotalPages);

        model.Rows = await query
            .OrderByDescending(r => r.CreatedAtUtc)
            .ThenByDescending(r => r.Id)
            .Skip((model.Page - 1) * 50)
            .Take(50)
            .ToListAsync();

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Import()
    {
        try
        {
            await HistoryWriter.ImportAsync(db);

            TempData["HistoryMessage"] =
                "Mevcut kayıtlar geçmişe aktarıldı. "
                + "Önceden aktarılmış kayıtlar çoğaltılmadı.";
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Geçmiş kayıtları aktarılamadı.");

            TempData["HistoryError"] =
                "Aktarım tamamlanamadı. Tekrar deneyebilirsiniz; "
                + "önceden aktarılan kayıtlar çoğaltılmaz.";
        }

        return RedirectToAction(nameof(Index));
    }
}