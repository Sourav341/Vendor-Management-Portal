using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VendorPortal.Data;
using VendorPortal.Services;

namespace VendorPortal.Controllers;

[Authorize]
public class DocumentsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly FileStorageService _files;

    public DocumentsController(ApplicationDbContext db, FileStorageService files) { _db = db; _files = files; }

    // Streams a private file only to its owner or an Admin.
    public async Task<IActionResult> Get(int id)
    {
        var v = await _db.DocumentVersions
            .Include(x => x.VendorDocument).ThenInclude(d => d.Application).ThenInclude(a => a.VendorProfile)
            .FirstOrDefaultAsync(x => x.DocumentVersionId == id);
        if (v == null) return NotFound();

        var ownerId = v.VendorDocument.Application.VendorProfile.UserId;
        if (!User.IsInRole("Admin") && ownerId != User.FindFirstValue(ClaimTypes.NameIdentifier))
            return Forbid();

        var stream = _files.Open(v.StorageKey);
        if (stream == null) return NotFound();

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(stream, v.ContentType);
    }
}
