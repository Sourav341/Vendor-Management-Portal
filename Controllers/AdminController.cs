using System.Security.Claims;
using System.Security.Cryptography;
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
    private const int MaxCertificateDownloads = 5;

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

        // The view can use this flag to show Issue Certificate only when the
        // application is approved and no certificate has been issued yet.
        ViewBag.CertificateExists = await _db.VendorCertificates
            .AsNoTracking()
            .AnyAsync(c => c.ApplicationId == id);

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

            // Previously approved applications receive a new two-year validity
            // period beginning on the actual certificate issuance date.
            var issuedAtUtc = DateTime.UtcNow;
            var certificate = new VendorCertificate
            {
                VendorProfileId = profile.VendorProfileId,
                ApplicationId = application.ApplicationId,
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
                UcanNumber = profile.UcanNumber,
                RegistrationType = profile.Cert_Type,
                IssuedAtUtc = issuedAtUtc,
                ValidUntilUtc = issuedAtUtc.AddYears(CertificateValidityYears),
                MaxDownloads = MaxCertificateDownloads,
                DownloadCount = 0,
                GeneratedByAdminId = UserId,
                IsRevoked = false,
                CreatedAtUtc = issuedAtUtc
            };

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

    /// <summary>
    /// Rebuilds an issued certificate PDF with the current template while preserving
    /// its certificate number, verification token, validity dates, and download history.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegenerateCertificatePdf(
        int applicationId,
        CancellationToken cancellationToken)
    {
        var certificate = await _db.VendorCertificates
            .SingleOrDefaultAsync(c => c.ApplicationId == applicationId, cancellationToken);

        if (certificate is null)
        {
            TempData["Error"] = "No certificate has been issued for this application.";
            return RedirectToAction(nameof(Details), new { id = applicationId });
        }

        await _certificates.RegeneratePdfAsync(certificate, cancellationToken);
        
        return RedirectToAction(nameof(Details), new { id = applicationId });
    }
    private static string CreateCertificateNumber(DateTime issuedAtUtc)
    {
        var uniquePart = Guid.NewGuid().ToString("N").ToUpperInvariant();
        return $"JH34-{issuedAtUtc:yyyy}-{uniquePart}";
    }

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
