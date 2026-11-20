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

    private Task<MasterWorkType?> FindMasterWorkTypeAsync(string? workType, string? subWorkType)
    {
        if (string.IsNullOrWhiteSpace(workType) || string.IsNullOrWhiteSpace(subWorkType))
            return Task.FromResult<MasterWorkType?>(null);

        var normalizedWorkType = workType.Trim();
        var normalizedSubWorkType = subWorkType.Trim();

        return _db.MasterWorkTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.WorkType == normalizedWorkType &&
                x.SubWorkType == normalizedSubWorkType);
    }

    // ---------------- Vendor Dashboard ----------------

    [HttpGet]
    public async Task<IActionResult> Dashboard()
    {
        var application = await _apps.GetForVendorAsync(UserId);

        ViewBag.CertificateRevalidationCycle = await _db.CertificateRevalidationCycles
            .AsNoTracking()
            .Where(c => c.ApplicationId == application.ApplicationId)
            .OrderByDescending(c => c.RequestedAtUtc)
            .ThenByDescending(c => c.CertificateRevalidationCycleId)
            .Include(c => c.RequestedByAdmin)
            .Include(c => c.ApprovedByAdmin)
            .Include(c => c.Documents).ThenInclude(d => d.VendorDocument)
            .Include(c => c.Documents).ThenInclude(d => d.SubmittedVersion).ThenInclude(v => v!.UploadedBy)
            .Include(c => c.Documents).ThenInclude(d => d.RequestedByAdmin)
            .Include(c => c.Documents).ThenInclude(d => d.ReviewedByAdmin)
            .AsSplitQuery()
            .FirstOrDefaultAsync();

        return View(application);
    }

    [HttpGet]
    public async Task<IActionResult> GetSubWorkTypes(string? workType)
    {
        if (string.IsNullOrWhiteSpace(workType))
            return Json(Array.Empty<string>());

        var subWorkTypes = await _db.MasterWorkTypes
            .AsNoTracking()
            .Where(x => x.WorkType == workType.Trim())
            .Select(x => x.SubWorkType)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();

        return Json(subWorkTypes);
    }

    // ---------------- Additional Details ----------------

    [HttpGet]
    public async Task<IActionResult> AdditionalDetails()
    {
        var application = await _apps.GetForVendorAsync(UserId);
        var profileId = application.VendorProfile.VendorProfileId;

        var requests = await _db.VendorAdditionalDetailRequests
            .AsNoTracking()
            .Include(x => x.ReviewedByAdmin)
            .Where(x => x.VendorProfileId == profileId)
            .OrderByDescending(x => x.SubmittedAtUtc)
            .ThenByDescending(x => x.VendorAdditionalDetailRequestId)
            .ToListAsync();

        var correctionBatch = requests
            .Where(x => x.Status is AdditionalDetailStatus.CorrectionsRequired or AdditionalDetailStatus.Rejected)
            .GroupBy(x => x.RequestBatchId)
            .OrderByDescending(group => group.Max(row => row.SubmittedAtUtc))
            .FirstOrDefault();

        ViewBag.Districts = await _db.MasterDistricts
            .AsNoTracking()
            .Select(x => x.DistrictName)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();

        ViewBag.WorkTypes = await _db.MasterWorkTypes
            .AsNoTracking()
            .Select(x => x.WorkType)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();

        ViewBag.ApplicationStatus = application.Status;
        ViewBag.HasPendingRequest = requests.Any(x => x.Status == AdditionalDetailStatus.PendingReview);
        ViewBag.CanSubmitAdditionalDetails =
            (application.Status is ApplicationStatus.Draft or
                ApplicationStatus.CorrectionsRequired or
                ApplicationStatus.PendingReview or
                ApplicationStatus.UnderReview or
                ApplicationStatus.Approved) &&
            !ViewBag.HasPendingRequest;

        return View(new AdditionalDetailsPageViewModel
        {
            Entries = correctionBatch is null
                ? new() { new() }
                : correctionBatch.Select(x => new AdditionalDetailEntryInput
                {
                    District = x.District,
                    WorkType = x.WorkType,
                    SubWorkType = x.SubWorkType,
                    Remarks = x.Remarks
                }).ToList(),
            ReplacesRequestBatchId = correctionBatch?.Key,
            Requests = requests
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitAdditionalDetails(AdditionalDetailsPageViewModel model)
    {
        var application = await _apps.GetForVendorAsync(UserId);
        var profileId = application.VendorProfile.VendorProfileId;

        if (application.Status is not (
                ApplicationStatus.Draft or
                ApplicationStatus.CorrectionsRequired or
                ApplicationStatus.PendingReview or
                ApplicationStatus.UnderReview or
                ApplicationStatus.Approved))
        {
            TempData["AdditionalDetailsError"] =
                "Additional details can be submitted only before final approval or after the vendor is approved.";
            return RedirectToAction(nameof(AdditionalDetails));
        }

        if (await _db.VendorAdditionalDetailRequests.AnyAsync(x =>
                x.VendorProfileId == profileId &&
                x.Status == AdditionalDetailStatus.PendingReview))
        {
            TempData["AdditionalDetailsError"] =
                "An additional details request is already waiting for Admin review.";
            return RedirectToAction(nameof(AdditionalDetails));
        }

        List<VendorAdditionalDetailRequest> correctionRows = new();
        if (model.ReplacesRequestBatchId is Guid replacesBatchId)
        {
            correctionRows = await _db.VendorAdditionalDetailRequests
                .Where(x => x.VendorProfileId == profileId &&
                            x.RequestBatchId == replacesBatchId &&
                            (x.Status == AdditionalDetailStatus.CorrectionsRequired ||
                             x.Status == AdditionalDetailStatus.Rejected))
                .ToListAsync();

            if (correctionRows.Count == 0)
            {
                TempData["AdditionalDetailsError"] =
                    "The correction request could not be found. Refresh the page and try again.";
                return RedirectToAction(nameof(AdditionalDetails));
            }
        }
        else if (await _db.VendorAdditionalDetailRequests.AnyAsync(x =>
                     x.VendorProfileId == profileId &&
                     (x.Status == AdditionalDetailStatus.CorrectionsRequired ||
                      x.Status == AdditionalDetailStatus.Rejected)))
        {
            TempData["AdditionalDetailsError"] =
                "Please edit and resubmit the Additional Details rejected or returned by Admin.";
            return RedirectToAction(nameof(AdditionalDetails));
        }

        var entries = (model.Entries ?? new())
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.District) ||
                !string.IsNullOrWhiteSpace(x.WorkType) ||
                !string.IsNullOrWhiteSpace(x.SubWorkType) ||
                !string.IsNullOrWhiteSpace(x.Remarks))
            .ToList();

        if (entries.Count == 0)
        {
            TempData["AdditionalDetailsError"] = "Add at least one district and work type.";
            return RedirectToAction(nameof(AdditionalDetails));
        }

        if (entries.Any(x =>
                string.IsNullOrWhiteSpace(x.District) ||
                string.IsNullOrWhiteSpace(x.WorkType) ||
                string.IsNullOrWhiteSpace(x.SubWorkType) ||
                x.District.Trim().Length > 100 ||
                x.WorkType.Trim().Length > 100 ||
                x.SubWorkType.Trim().Length > 150 ||
                (x.Remarks?.Length ?? 0) > 4000))
        {
            TempData["AdditionalDetailsError"] = "Complete each row and keep the values within the allowed lengths.";
            return RedirectToAction(nameof(AdditionalDetails));
        }

        var districtNames = await _db.MasterDistricts
            .AsNoTracking()
            .Select(x => x.DistrictName)
            .Distinct()
            .ToListAsync();
        var validDistricts = districtNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seenRows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var newRows = new List<VendorAdditionalDetailRequest>();

        foreach (var entry in entries)
        {
            var district = entry.District.Trim();
            var workType = entry.WorkType.Trim();
            var subWorkType = entry.SubWorkType.Trim();
            var remarks = entry.Remarks?.Trim();

            if (!validDistricts.Contains(district))
            {
                TempData["AdditionalDetailsError"] = $"'{district}' is not a valid district.";
                return RedirectToAction(nameof(AdditionalDetails));
            }

            var workTypeRow = await FindMasterWorkTypeAsync(workType, subWorkType);
            if (workTypeRow is null)
            {
                TempData["AdditionalDetailsError"] =
                    "Select a valid work type and sub work type from the available options.";
                return RedirectToAction(nameof(AdditionalDetails));
            }

            var wordCount = string.IsNullOrWhiteSpace(remarks)
                ? 0
                : Regex.Matches(remarks, @"\S+").Count;
            if (wordCount > 100)
            {
                TempData["AdditionalDetailsError"] = "Remarks cannot exceed 100 words per row.";
                return RedirectToAction(nameof(AdditionalDetails));
            }

            var rowKey = $"{district}\u001f{workTypeRow.WorkType}\u001f{workTypeRow.SubWorkType}";
            if (!seenRows.Add(rowKey))
            {
                TempData["AdditionalDetailsError"] =
                    "The same district and work type cannot appear more than once in a request.";
                return RedirectToAction(nameof(AdditionalDetails));
            }

            newRows.Add(new VendorAdditionalDetailRequest
            {
                District = district,
                WorkType = workTypeRow.WorkType,
                SubWorkType = workTypeRow.SubWorkType,
                Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks,
                VendorProfileId = profileId,
                ApplicationId = application.ApplicationId,
                RequestedByVendorId = UserId,
                SubmittedAtUtc = DateTime.UtcNow,
                Status = AdditionalDetailStatus.PendingReview
            });
        }

        if (application.Status == ApplicationStatus.Approved)
        {
            var approvedRows = await _db.VendorAdditionalDetailRequests
                .AsNoTracking()
                .Where(x => x.VendorProfileId == profileId &&
                            x.Status == AdditionalDetailStatus.Approved)
                .Select(x => new { x.District, x.WorkType, x.SubWorkType })
                .ToListAsync();

            var existingRows = approvedRows
                .Select(x => $"{x.District}\u001f{x.WorkType}\u001f{x.SubWorkType}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (newRows.Any(x => existingRows.Contains(
                    $"{x.District}\u001f{x.WorkType}\u001f{x.SubWorkType}")))
            {
                TempData["AdditionalDetailsError"] =
                    "One or more selected district and work type combinations are already approved.";
                return RedirectToAction(nameof(AdditionalDetails));
            }
        }

        var batchId = Guid.NewGuid();
        foreach (var row in newRows)
            row.RequestBatchId = batchId;

        foreach (var oldRow in correctionRows)
            oldRow.Status = AdditionalDetailStatus.Superseded;

        _db.VendorAdditionalDetailRequests.AddRange(newRows);
        await _db.SaveChangesAsync();

        TempData["AdditionalDetailsSuccess"] =
            "Additional details submitted to Admin for review.";
        return RedirectToAction(nameof(AdditionalDetails));
    }

    // ---------------- Vendor Profile ----------------

    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        // Load the latest application, including its review history and uploaded documents.
        var application = await _apps.GetForVendorAsync(UserId);

        var profile = await _db.VendorProfiles
            .AsNoTracking()
            .Include(p => p.User)
            .SingleOrDefaultAsync(p => p.UserId == UserId);

        if (profile == null)
        {
            return NotFound("Vendor profile not found.");
        }

        // Share the real application status with the Profile view.
        ViewBag.ApplicationStatus = application.Status;
        ViewBag.ApplicationSubmittedAt = application.SubmittedAt;
        ViewBag.ApprovedAdditionalDetails = await _db.VendorAdditionalDetailRequests
            .AsNoTracking()
            .Where(x => x.VendorProfileId == profile.VendorProfileId &&
                        x.Status == AdditionalDetailStatus.Approved)
            .OrderBy(x => x.District)
            .ThenBy(x => x.WorkType)
            .ThenBy(x => x.SubWorkType)
            .ToListAsync();

        // Show the latest Admin decision/review remarks, when available.
        ViewBag.LatestDecisionRemarks = application.DecisionHistory
            .OrderByDescending(h => h.DecidedAt)
            .Select(h => h.Remarks)
            .FirstOrDefault(remarks => !string.IsNullOrWhiteSpace(remarks));

        // Reuse the latest uploaded ProfilePhoto document version.
        var profilePhotoVersion = application.Documents
            .Where(d => d.DocumentType == DocumentType.ProfilePhoto)
            .SelectMany(d => d.Versions)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefault();

        // Documents/Get already serves vendor document versions securely.
        ViewBag.ProfilePhotoUrl = profilePhotoVersion == null
            ? null
            : Url.Action("Get", "Documents", new { id = profilePhotoVersion.DocumentVersionId });

        // Load the latest certificate issued for this vendor, if one exists.
        // Only the fields needed by the Profile page are selected.
        var latestCertificate = await _db.VendorCertificates
            .AsNoTracking()
            .Where(c => c.VendorProfileId == profile.VendorProfileId)
            .OrderByDescending(c => c.IssuedAtUtc)
            .ThenByDescending(c => c.VendorCertificateId)
            .Select(c => new
            {
                c.VendorCertificateId,

                c.VerificationToken,
                c.IssuedAtUtc,
                c.ValidUntilUtc,
                c.DownloadCount,
                c.MaxDownloads,
                c.IsRevoked
            })
            .FirstOrDefaultAsync();

        ViewBag.CertificateId = latestCertificate?.VendorCertificateId;
        
        ViewBag.CertificateIssuedAtUtc = latestCertificate?.IssuedAtUtc;
        ViewBag.CertificateValidUntilUtc = latestCertificate?.ValidUntilUtc;
        ViewBag.CertificateDownloadCount = latestCertificate?.DownloadCount ?? 0;
        ViewBag.CertificateMaxDownloads = latestCertificate?.MaxDownloads ?? 15;
        ViewBag.CertificateIsRevoked = latestCertificate?.IsRevoked ?? false;
        ViewBag.CertificateVerificationUrl = latestCertificate == null
            ? null
            : Url.Action(
                "Verify",
                "Certificates",
                new { token = latestCertificate.VerificationToken });

        return View(profile);
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadRevalidationDocument(
        int revalidationDocumentId,
        IFormFile? file)
    {
        var error = await _apps.UploadRevalidationDocumentAsync(
            UserId,
            revalidationDocumentId,
            file);

        TempData[error is null ? "Success" : "Error"] = error ?? "Document uploaded for admin review.";
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

        if (!ModelState.IsValid)
        {
            TempData["Error"] =
                "Please check the submitted application details.";

            return RedirectToAction(nameof(Dashboard));
        }

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

        // Submit only after processing files and profile details.
        var submissionError = await _apps.SubmitAsync(UserId);

        if (submissionError != null)
        {
            TempData["Error"] =
                $"Your details and selected uploads were saved. {submissionError}";

            return RedirectToAction(nameof(Dashboard));
        }

        TempData["Success"] =
            "Documents uploaded and application submitted successfully.";

        return RedirectToAction(nameof(Dashboard));
    }
}
