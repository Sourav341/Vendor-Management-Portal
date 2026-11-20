using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VendorPortal.Data;
using VendorPortal.Models;
using VendorPortal.Services;

namespace VendorPortal.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private const int CertificateValidityYears = 2;
    private const int MaxCertificateDownloads = 15;

    private readonly ApplicationDbContext _db;
    private readonly ApplicationService _apps;
    private readonly CertificateService _certificates;
    private readonly IWebHostEnvironment _environment;

    public AdminController(
        ApplicationDbContext db,
        ApplicationService apps,
        CertificateService certificates,
        IWebHostEnvironment environment)
    {
        _db = db;
        _apps = apps;
        _certificates = certificates;
        _environment = environment;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public async Task<IActionResult> Index(string? search, ApplicationStatus? status)
    {
        var q = _db.VendorApplications
            .Include(a => a.VendorProfile).ThenInclude(p => p.User)
            .AsQueryable();

        q = status.HasValue
            ? q.Where(a => a.Status == status)
            : q.Where(a => a.Status != ApplicationStatus.Draft);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(a => a.VendorProfile.FullName.Contains(s)
                          || a.VendorProfile.PanNumber.Contains(s)
                          || a.VendorProfile.GstNumber.Contains(s)
                          || a.VendorProfile.User.Email!.Contains(s));
        }

        ViewData["Search"] = search;
        ViewData["Status"] = status;
        return View(await q.OrderByDescending(a => a.SubmittedAt).ToListAsync());
    }

    public async Task<IActionResult> Details(int id)
    {
        var app = await _apps.GetForAdminAsync(id);
        if (app == null)
            return NotFound();

        ViewBag.CertificateExists = await _db.VendorCertificates
            .AsNoTracking()
            .AnyAsync(c => c.ApplicationId == id && !c.IsRevoked);
        ViewBag.CertificateHistoryExists = await _db.VendorCertificates
            .AsNoTracking()
            .AnyAsync(c => c.ApplicationId == id);

        var revalidationCycle = await _db.CertificateRevalidationCycles
            .AsNoTracking()
            .Where(c => c.ApplicationId == id)
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

        ViewBag.RevalidationCycle = revalidationCycle;
        ViewBag.AdditionalDetailRequests = await _db.VendorAdditionalDetailRequests
            .AsNoTracking()
            .Where(x => x.ApplicationId == id)
            .Include(x => x.RequestedByVendor)
            .Include(x => x.ReviewedByAdmin)
            .OrderByDescending(x => x.SubmittedAtUtc)
            .ThenBy(x => x.RequestBatchId)
            .ThenBy(x => x.District)
            .ToListAsync();

        var requestedTypesInOpenCycle = revalidationCycle?.Status == CertificateRevalidationStatus.Open
            ? revalidationCycle.Documents.Select(d => d.VendorDocument.DocumentType).ToHashSet()
            : new HashSet<DocumentType>();
        ViewBag.AvailableRevalidationDocumentTypes = Enum.GetValues<DocumentType>()
            .Where(type => !requestedTypesInOpenCycle.Contains(type))
            .OrderBy(type => type.DisplayOrder())
            .ToList();

        return View(app);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReviewDocument(
        int applicationId,
        int vendorDocumentId,
        ReviewDecision decision,
        string? remarks)
    {
        var error = await _apps.ReviewDocumentAsync(UserId, vendorDocumentId, decision, remarks);
        TempData[error == null ? "Success" : "Error"] = error ?? $"Document marked {decision}.";
        return RedirectToAction(nameof(Details), new { id = applicationId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestRevalidationDocuments(
        int applicationId,
        DocumentType[]? documentTypes)
    {
        var error = await _apps.RequestRevalidationDocumentsAsync(
            UserId,
            applicationId,
            documentTypes);

        TempData[error is null ? "Success" : "Error"] = error
            ?? "The selected documents were added to the vendor's revalidation review.";
        return RedirectToAction(nameof(Details), new { id = applicationId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReviewRevalidationDocument(
        int applicationId,
        int revalidationDocumentId,
        ReviewDecision decision,
        string? remarks)
    {
        var error = await _apps.ReviewRevalidationDocumentAsync(
            UserId,
            revalidationDocumentId,
            decision,
            remarks);

        TempData[error is null ? "Success" : "Error"] = error
            ?? $"Requested document marked {decision}.";
        return RedirectToAction(nameof(Details), new { id = applicationId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReviewAdditionalDetails(
        int applicationId,
        Guid requestBatchId,
        string action,
        string? remarks)
    {
        var error = await _apps.ReviewAdditionalDetailsAsync(
            UserId,
            applicationId,
            requestBatchId,
            action,
            remarks);

        TempData[error is null ? "Success" : "Error"] = error ?? action switch
        {
            "Approve" => "Additional Details approved.",
            "Reject" => "Additional Details rejected.",
            _ => "Additional Details returned to the vendor for correction."
        };
        return RedirectToAction(nameof(Details), new { id = applicationId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveApprovedAdditionalDetail(
        int applicationId,
        int requestId)
    {
        var error = await _apps.RemoveApprovedAdditionalDetailAsync(
            UserId,
            applicationId,
            requestId);

        TempData[error is null ? "Success" : "Error"] = error
            ?? "Approved coverage removed. The certificate was refreshed; UCAN and its validity remain unchanged, and the download count has reset.";
        return RedirectToAction(nameof(Details), new { id = applicationId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FinalDecision(
        int applicationId,
        string action,
        string? remarks)
    {
        var error = await _apps.FinalDecisionAsync(UserId, applicationId, action, remarks);
        TempData[error == null ? "Success" : "Error"] = error ?? "Decision recorded.";
        return RedirectToAction(nameof(Details), new { id = applicationId });
    }

    /// <summary>
    /// Issues a certificate for an application that was approved before
    /// automatic certificate generation was added. This action is Admin-only.
    /// It does not re-approve or change the application's status.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IssueCertificate(
        int applicationId,
        CancellationToken cancellationToken)
    {
        string? generatedPdfStorageKey = null;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var application = await _db.VendorApplications
                .Include(a => a.VendorProfile)
                .Include(a => a.Documents)
                .SingleOrDefaultAsync(
                    a => a.ApplicationId == applicationId,
                    cancellationToken);

            if (application == null)
            {
                TempData["Error"] = "Vendor application not found.";
                return RedirectToAction(nameof(Index));
            }

            if (application.Status != ApplicationStatus.Approved)
            {
                TempData["Error"] = "A certificate can only be issued for an approved application.";
                return RedirectToAction(nameof(Details), new { id = applicationId });
            }

            if (application.Documents.Any(d => d.IsRequired && d.Status != DocumentStatus.Approved))
            {
                TempData["Error"] = "Every required document must be approved before issuing the certificate.";
                return RedirectToAction(nameof(Details), new { id = applicationId });
            }

            var profile = application.VendorProfile;
            if (profile == null)
            {
                TempData["Error"] = "Vendor profile not found.";
                return RedirectToAction(nameof(Details), new { id = applicationId });
            }

            if (string.IsNullOrWhiteSpace(profile.UcanNumber))
            {
                TempData["Error"] = "This approved vendor does not have a UCAN. Assign the UCAN before issuing the certificate.";
                return RedirectToAction(nameof(Details), new { id = applicationId });
            }

            var certificateAlreadyExists = await _db.VendorCertificates
                .AnyAsync(c => c.ApplicationId == applicationId, cancellationToken);

            if (certificateAlreadyExists)
            {
                TempData["Error"] = "A certificate has already been issued for this application.";
                return RedirectToAction(nameof(Details), new { id = applicationId });
            }

            // Legacy certificate issuance keeps validity anchored to the original UCAN generation date.
            var issuedAtUtc = DateTime.UtcNow;
            await EnsureUcanGeneratedAtAsync(profile, cancellationToken);
            var certificate = CreateCertificate(profile, application.ApplicationId, issuedAtUtc);
            certificate.AdditionalDetailsSnapshotJson =
                await SerializeApprovedAdditionalDetailsAsync(profile, cancellationToken);

            generatedPdfStorageKey = await _certificates.GeneratePdfAsync(
                certificate,
                cancellationToken);

            certificate.PdfStorageKey = generatedPdfStorageKey;
            _db.VendorCertificates.Add(certificate);
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

                        TempData["Success"] = $"Certificate issued successfully for UCAN {certificate.UcanNumber}.";

            return RedirectToAction(nameof(Details), new { id = applicationId });
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            TryDeleteGeneratedCertificateFile(generatedPdfStorageKey);
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveRevalidation(
        int applicationId,
        int revalidationCycleId,
        CancellationToken cancellationToken)
    {
        string? generatedPdfStorageKey = null;
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var cycle = await _db.CertificateRevalidationCycles
                .Include(c => c.Application).ThenInclude(a => a.VendorProfile)
                .Include(c => c.Documents)
                .SingleOrDefaultAsync(
                    c => c.CertificateRevalidationCycleId == revalidationCycleId &&
                         c.ApplicationId == applicationId,
                    cancellationToken);

            if (cycle is null)
            {
                TempData["Error"] = "Document revalidation was not found.";
                return RedirectToAction(nameof(Details), new { id = applicationId });
            }

            if (cycle.Status != CertificateRevalidationStatus.Open ||
                cycle.Application.Status != ApplicationStatus.Approved)
            {
                TempData["Error"] = "This document revalidation is not open for approval.";
                return RedirectToAction(nameof(Details), new { id = applicationId });
            }

            if (cycle.Documents.Count == 0 || cycle.Documents.Any(d =>
                    d.Status != DocumentStatus.Approved || d.SubmittedVersionId is null))
            {
                TempData["Error"] = "Every document requested in this revalidation must be uploaded and approved first.";
                return RedirectToAction(nameof(Details), new { id = applicationId });
            }

            var profile = cycle.Application.VendorProfile;
            if (profile is null || string.IsNullOrWhiteSpace(profile.UcanNumber))
            {
                TempData["Error"] = "The approved vendor profile must have a UCAN before a fresh certificate can be issued.";
                return RedirectToAction(nameof(Details), new { id = applicationId });
            }

            var previousCertificates = await _db.VendorCertificates
                .Where(c => c.ApplicationId == applicationId)
                .ToListAsync(cancellationToken);

            if (previousCertificates.Count == 0)
            {
                TempData["Error"] = "A previous certificate is required for document revalidation.";
                return RedirectToAction(nameof(Details), new { id = applicationId });
            }

            var issuedAtUtc = DateTime.UtcNow;
            foreach (var previousCertificate in previousCertificates.Where(c => !c.IsRevoked))
            {
                previousCertificate.IsRevoked = true;
                previousCertificate.RevokedAtUtc = issuedAtUtc;
                previousCertificate.RevocationReason = "Superseded after the requested documents were approved.";
            }

            await EnsureUcanGeneratedAtAsync(profile, cancellationToken);
            var certificate = CreateCertificate(profile, applicationId, issuedAtUtc);
            certificate.AdditionalDetailsSnapshotJson =
                await SerializeApprovedAdditionalDetailsAsync(profile, cancellationToken);
            generatedPdfStorageKey = await _certificates.GeneratePdfAsync(
                certificate,
                cancellationToken);
            certificate.PdfStorageKey = generatedPdfStorageKey;

            cycle.Status = CertificateRevalidationStatus.Approved;
            cycle.ApprovedByAdminId = UserId;
            cycle.ApprovedAtUtc = issuedAtUtc;
            _db.VendorCertificates.Add(certificate);

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            TempData["Success"] = "All requested documents were approved and a fresh certificate was issued. Its download count starts at zero.";
            return RedirectToAction(nameof(Details), new { id = applicationId });
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            TryDeleteGeneratedCertificateFile(generatedPdfStorageKey);
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    /// <summary>
    /// Issues a refreshed certificate version with a new verification token and a reset
    /// download count, while preserving the original UCAN validity date and coverage snapshot.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegenerateCertificatePdf(
        int applicationId,
        CancellationToken cancellationToken)
    {
        string? generatedPdfStorageKey = null;
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var previousCertificate = await _db.VendorCertificates
                .Where(c => c.ApplicationId == applicationId && !c.IsRevoked)
                .OrderByDescending(c => c.IssuedAtUtc)
                .ThenByDescending(c => c.VendorCertificateId)
                .FirstOrDefaultAsync(cancellationToken);

            if (previousCertificate is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                TempData["Error"] = "No active certificate has been issued for this application.";
                return RedirectToAction(nameof(Details), new { id = applicationId });
            }

            var issuedAtUtc = DateTime.UtcNow;
            previousCertificate.IsRevoked = true;
            previousCertificate.RevokedAtUtc = issuedAtUtc;
            previousCertificate.RevocationReason = "Superseded when Admin issued a refreshed certificate version.";

            var refreshedCertificate = new VendorCertificate
            {
                VendorProfileId = previousCertificate.VendorProfileId,
                ApplicationId = previousCertificate.ApplicationId,
                CertificateNumber = CreateCertificateNumber(issuedAtUtc),
                VerificationToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                PdfStorageKey = string.Empty,
                VendorName = previousCertificate.VendorName,
                CompanyFirmName = previousCertificate.CompanyFirmName,
                FatherOrHusbandName = previousCertificate.FatherOrHusbandName,
                DateOfBirth = previousCertificate.DateOfBirth,
                Gender = previousCertificate.Gender,
                RegisteredAddress = previousCertificate.RegisteredAddress,
                District = previousCertificate.District,
                PanNumber = previousCertificate.PanNumber,
                GstNumber = previousCertificate.GstNumber,
                UcanNumber = previousCertificate.UcanNumber,
                RegistrationType = previousCertificate.RegistrationType,
                SubWorkType = previousCertificate.SubWorkType,
                AdditionalDetailsSnapshotJson = previousCertificate.AdditionalDetailsSnapshotJson,
                IssuedAtUtc = issuedAtUtc,
                ValidUntilUtc = previousCertificate.ValidUntilUtc,
                MaxDownloads = MaxCertificateDownloads,
                DownloadCount = 0,
                LastDownloadedAtUtc = null,
                GeneratedByAdminId = UserId,
                IsRevoked = false,
                CreatedAtUtc = issuedAtUtc
            };

            generatedPdfStorageKey = await _certificates.GeneratePdfAsync(
                refreshedCertificate,
                cancellationToken);
            refreshedCertificate.PdfStorageKey = generatedPdfStorageKey;
            _db.VendorCertificates.Add(refreshedCertificate);

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            TempData["Success"] =
                "A refreshed certificate version was issued. UCAN validity was preserved and its download count starts at zero.";
            return RedirectToAction(nameof(Details), new { id = applicationId });
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            TryDeleteGeneratedCertificateFile(generatedPdfStorageKey);
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    private static string CreateCertificateNumber(DateTime issuedAtUtc)
    {
        var uniquePart = Guid.NewGuid().ToString("N").ToUpperInvariant();
        return $"JH34-{issuedAtUtc:yyyy}-{uniquePart}";
    }

    private async Task EnsureUcanGeneratedAtAsync(
        VendorProfile profile,
        CancellationToken cancellationToken)
    {
        if (profile.UcanGeneratedAt.HasValue)
            return;

        var firstCertificateIssue = await _db.VendorCertificates
            .Where(x => x.VendorProfileId == profile.VendorProfileId)
            .Select(x => (DateTime?)x.IssuedAtUtc)
            .MinAsync(cancellationToken);

        profile.UcanGeneratedAt = firstCertificateIssue ?? profile.CreatedAt;
    }

    private async Task<string> SerializeApprovedAdditionalDetailsAsync(
        VendorProfile profile,
        CancellationToken cancellationToken)
    {
        var rows = await _db.VendorAdditionalDetailRequests
            .AsNoTracking()
            .Where(x => x.VendorProfileId == profile.VendorProfileId &&
                        x.Status == AdditionalDetailStatus.Approved)
            .OrderBy(x => x.District)
            .ThenBy(x => x.WorkType)
            .ThenBy(x => x.SubWorkType)
            .Select(x => new CertificateAdditionalDetailSnapshot
            {
                District = x.District,
                WorkType = x.WorkType,
                SubWorkType = x.SubWorkType
            })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0 &&
            !string.IsNullOrWhiteSpace(profile.Cert_Type) &&
            !string.IsNullOrWhiteSpace(profile.SubWorkType))
        {
            rows.Add(new CertificateAdditionalDetailSnapshot
            {
                District = profile.District,
                WorkType = profile.Cert_Type,
                SubWorkType = profile.SubWorkType
            });
        }

        return JsonSerializer.Serialize(rows);
    }

    private VendorCertificate CreateCertificate(
        VendorProfile profile,
        int applicationId,
        DateTime issuedAtUtc) => new()
    {
        VendorProfileId = profile.VendorProfileId,
        ApplicationId = applicationId,
        CertificateNumber = CreateCertificateNumber(issuedAtUtc),
        VerificationToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
        PdfStorageKey = string.Empty,
        VendorName = profile.FullName,
        CompanyFirmName = profile.TradeName,
        FatherOrHusbandName = profile.Father_Husband_Name,
        DateOfBirth = profile.DOB,
        Gender = profile.Gender,
        RegisteredAddress = profile.Trade_Address,
        District = profile.District,
        PanNumber = profile.PanNumber,
        GstNumber = profile.GstNumber,
        UcanNumber = profile.UcanNumber!,
        RegistrationType = profile.Cert_Type,
        SubWorkType = profile.SubWorkType,
        IssuedAtUtc = issuedAtUtc,
        ValidUntilUtc = (profile.UcanGeneratedAt ?? profile.CreatedAt).AddYears(CertificateValidityYears),
        MaxDownloads = MaxCertificateDownloads,
        DownloadCount = 0,
        GeneratedByAdminId = UserId,
        IsRevoked = false,
        CreatedAtUtc = issuedAtUtc
    };

    private void TryDeleteGeneratedCertificateFile(string? storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
            return;

        try
        {
            var appDataRoot = Path.GetFullPath(Path.Combine(
                _environment.ContentRootPath,
                "App_Data"));

            var normalizedKey = storageKey
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);

            var certificatePath = Path.GetFullPath(Path.Combine(appDataRoot, normalizedKey));
            var rootPrefix = appDataRoot.TrimEnd(Path.DirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;

            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (!certificatePath.StartsWith(rootPrefix, comparison))
                return;

            // Fully qualify System.IO.File because Controller also exposes a File() method.
            if (System.IO.File.Exists(certificatePath))
                System.IO.File.Delete(certificatePath);
        }
        catch
        {
            // Preserve the original certificate/database exception.
        }
    }
}
