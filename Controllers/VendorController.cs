using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VendorPortal.Models;
using VendorPortal.Services;

namespace VendorPortal.Controllers;

[Authorize(Roles = "Vendor")]
public class VendorController : Controller
{
    private readonly ApplicationService _apps;
    public VendorController(ApplicationService apps) => _apps = apps;

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public async Task<IActionResult> Dashboard() => View(await _apps.GetForVendorAsync(UserId));

    [HttpPost]
    public async Task<IActionResult> Upload(DocumentType type, IFormFile? file)
    {
        var error = await _apps.UploadAsync(UserId, type, file);
        TempData[error == null ? "Success" : "Error"] = error ?? $"{type.DisplayName()} uploaded.";
        return RedirectToAction(nameof(Dashboard));
    }

    [HttpPost]
    public async Task<IActionResult> Submit()
    {
        var error = await _apps.SubmitAsync(UserId);
        TempData[error == null ? "Success" : "Error"] = error ?? "Application submitted for review.";
        return RedirectToAction(nameof(Dashboard));
    }
}
