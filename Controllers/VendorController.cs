
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VendorPortal.Data;
using VendorPortal.Models;
using VendorPortal.Services;
using VendorPortal.ViewModels;

namespace VendorPortal.Controllers;

[Authorize(Roles = "Vendor")]
public class VendorController : Controller
{
    private readonly ApplicationService _apps;
    private readonly ApplicationDbContext _db;

    public VendorController(
        ApplicationService apps,
        ApplicationDbContext db)
    {
        _apps = apps;
        _db = db;
    }

    private string UserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // ---------------- Vendor Dashboard ----------------

    [HttpGet]
    public async Task<IActionResult> Dashboard()
    {
        var application = await _apps.GetForVendorAsync(UserId);

        return View(application);
    }

    // ---------------- Update Certificate Type and Remarks ----------------

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateVendorDetails(
        VendorProfileDetailsViewModel m)
    {
        // Allow only Admin or Material.
        if (m.Cert_Type != "Admin" &&
            m.Cert_Type != "Material")
        {
            TempData["ProfileError"] =
                "Please select Admin or Material as the Certificate Type.";

            return RedirectToAction(nameof(Dashboard));
        }

        // Enforce maximum 100 words on the server.
        var remarks = m.Remarks?.Trim();

        var wordCount = string.IsNullOrWhiteSpace(remarks)
            ? 0
            : Regex.Matches(remarks, @"\S+").Count;

        if (wordCount > 100)
        {
            TempData["ProfileError"] =
                "Remarks cannot exceed 100 words.";

            return RedirectToAction(nameof(Dashboard));
        }

        // Validate ViewModel attributes, including maximum character length.
        if (!ModelState.IsValid)
        {
            TempData["ProfileError"] =
                "Please check the submitted details and try again.";

            return RedirectToAction(nameof(Dashboard));
        }

        // Confirm that the vendor's application is editable.
        var application = await _apps.GetForVendorAsync(UserId);

        if (application == null)
        {
            return NotFound("Vendor application not found.");
        }

        var canEdit = application.Status is
            ApplicationStatus.Draft or
            ApplicationStatus.CorrectionsRequired;

        if (!canEdit)
        {
            TempData["ProfileError"] =
                "Additional details cannot be changed for the current application status.";

            return RedirectToAction(nameof(Dashboard));
        }

        // Get only the logged-in vendor's profile.
        var vendorProfile = await _db.VendorProfiles
            .SingleOrDefaultAsync(p => p.UserId == UserId);

        if (vendorProfile == null)
        {
            return NotFound("Vendor profile not found.");
        }

        // Update the two database columns.
        vendorProfile.Cert_Type = m.Cert_Type;

        vendorProfile.Remarks = string.IsNullOrWhiteSpace(remarks)
            ? null
            : remarks;

        try
        {
            await _db.SaveChangesAsync();

            TempData["ProfileSuccess"] =
                "Certificate Type and Remarks saved successfully.";
        }
        catch (DbUpdateException)
        {
            TempData["ProfileError"] =
                "Unable to save the additional details. Please try again.";
        }

        return RedirectToAction(nameof(Dashboard));
    }

    // ---------------- Document Upload / Replace ----------------

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(
        DocumentType type,
        IFormFile? file)
    {
        var error = await _apps.UploadAsync(UserId, type, file);

        TempData[error == null ? "Success" : "Error"] =
            error ?? $"{type.DisplayName()} uploaded.";

        return RedirectToAction(nameof(Dashboard));
    }

    // ---------------- Submit Application ----------------


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(
        VendorApplicationSubmitViewModel m)
    {
        // Load the current vendor's application.
        var application = await _apps.GetForVendorAsync(UserId);

        if (application.Status is not (
            ApplicationStatus.Draft or
            ApplicationStatus.CorrectionsRequired))
        {
            TempData["Error"] =
                "This application can no longer be edited.";

            return RedirectToAction(nameof(Dashboard));
        }

        // Validate certificate type.
        if (m.Cert_Type != "Admin" && m.Cert_Type != "Material")
        {
            TempData["Error"] =
                "Please select Admin or Material as the Certificate Type.";

            return RedirectToAction(nameof(Dashboard));
        }

        // Validate Remarks: maximum 100 words.
        var remarks = m.Remarks?.Trim();

        var wordCount = string.IsNullOrWhiteSpace(remarks)
            ? 0
            : Regex.Matches(
                remarks,
                @"[\p{L}\p{N}]+(?:['’][\p{L}\p{N}]+)*"
              ).Count;

        if (wordCount > 100)
        {
            TempData["Error"] = "Remarks cannot exceed 100 words.";
            return RedirectToAction(nameof(Dashboard));
        }

        if (!ModelState.IsValid)
        {
            TempData["Error"] =
                "Please check the submitted application details.";

            return RedirectToAction(nameof(Dashboard));
        }

        // Find the vendor's own profile.
        var vendorProfile = await _db.VendorProfiles
            .SingleOrDefaultAsync(p => p.UserId == UserId);

        if (vendorProfile == null)
            return NotFound("Vendor profile not found.");

        // Keep only files actually selected by the vendor.
        var uploads = (m.Uploads ?? new())
            .Where(u => u.File is { Length: > 0 })
            .ToList();

        // Do not accept duplicate document types in one submission.
        if (uploads
            .GroupBy(u => u.DocumentType)
            .Any(g => g.Count() > 1))
        {
            TempData["Error"] =
                "Please select only one file per document type.";

            return RedirectToAction(nameof(Dashboard));
        }

        // Validate every selected document before uploading any files.
        foreach (var upload in uploads)
        {
            var document = application.Documents.FirstOrDefault(
                d => d.DocumentType == upload.DocumentType);

            if (document == null)
            {
                TempData["Error"] = "Invalid document type.";
                return RedirectToAction(nameof(Dashboard));
            }

            // During corrections, only rejected or missing documents
            // may be uploaded or replaced.
            if (application.Status == ApplicationStatus.CorrectionsRequired &&
                document.Status is DocumentStatus.Pending or DocumentStatus.Approved)
            {
                TempData["Error"] =
                    $"{upload.DocumentType.DisplayName()} cannot be replaced in the current correction cycle.";

                return RedirectToAction(nameof(Dashboard));
            }

            var fileError = FileStorageService.Validate(
                upload.File,
                upload.DocumentType);

            if (fileError != null)
            {
                TempData["Error"] = fileError;
                return RedirectToAction(nameof(Dashboard));
            }
        }

        // Save selected files through the existing application service.
        // This updates VendorDocuments and DocumentVersions.
        foreach (var upload in uploads)
        {
            var uploadError = await _apps.UploadAsync(
                UserId,
                upload.DocumentType,
                upload.File);

            if (uploadError != null)
            {
                TempData["Error"] =
                    $"A document upload failed: {uploadError} " +
                    "Some earlier uploads may already have been saved.";

                return RedirectToAction(nameof(Dashboard));
            }
        }

        // Save Certificate Type and Remarks in VendorProfiles.
        vendorProfile.Cert_Type = m.Cert_Type;
        vendorProfile.Remarks = string.IsNullOrWhiteSpace(remarks)
            ? null
            : remarks;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            TempData["Error"] =
                "The profile details could not be saved. " +
                "Any completed document uploads may already be saved.";

            return RedirectToAction(nameof(Dashboard));
        }

        // Submit only after processing files and profile details.
        var submissionError = await _apps.SubmitAsync(UserId);

        if (submissionError != null)
        {
            TempData["Error"] =
                $"Your details and selected uploads were saved. {submissionError}";

            return RedirectToAction(nameof(Dashboard));
        }

        TempData["Success"] =
            "Application details saved, documents uploaded, and application submitted successfully.";

        return RedirectToAction(nameof(Dashboard));
    }

}
