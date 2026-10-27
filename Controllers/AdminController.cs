using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VendorPortal.Data;
using VendorPortal.Models;
using VendorPortal.Services;

namespace VendorPortal.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ApplicationService _apps;

    public AdminController(ApplicationDbContext db, ApplicationService apps) { _db = db; _apps = apps; }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public async Task<IActionResult> Index(string? search, ApplicationStatus? status)
    {
        var q = _db.VendorApplications
            .Include(a => a.VendorProfile).ThenInclude(p => p.User)
            .AsQueryable();

        q = status.HasValue ? q.Where(a => a.Status == status) : q.Where(a => a.Status != ApplicationStatus.Draft);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(a => a.VendorProfile.FullName.Contains(s) || a.VendorProfile.PanNumber.Contains(s)
                          || a.VendorProfile.GstNumber.Contains(s) || a.VendorProfile.User.Email!.Contains(s));
        }

        ViewData["Search"] = search;
        ViewData["Status"] = status;
        return View(await q.OrderByDescending(a => a.SubmittedAt).ToListAsync());
    }

    public async Task<IActionResult> Details(int id)
    {
        var app = await _apps.GetForAdminAsync(id);
        return app == null ? NotFound() : View(app);
    }

    [HttpPost]
    public async Task<IActionResult> ReviewDocument(int applicationId, int vendorDocumentId, ReviewDecision decision, string? remarks)
    {
        var error = await _apps.ReviewDocumentAsync(UserId, vendorDocumentId, decision, remarks);
        TempData[error == null ? "Success" : "Error"] = error ?? $"Document marked {decision}.";
        return RedirectToAction(nameof(Details), new { id = applicationId });
    }

    [HttpPost]
    public async Task<IActionResult> FinalDecision(int applicationId, string action, string? remarks)
    {
        var error = await _apps.FinalDecisionAsync(UserId, applicationId, action, remarks);
        TempData[error == null ? "Success" : "Error"] = error ?? "Decision recorded.";
        return RedirectToAction(nameof(Details), new { id = applicationId });
    }
}
