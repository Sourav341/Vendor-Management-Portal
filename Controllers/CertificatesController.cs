using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VendorPortal.Data;
using VendorPortal.Models;
using VendorPortal.ViewModels;

namespace VendorPortal.Controllers;

[Route("[controller]")]
public sealed class CertificatesController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _environment;

    public CertificatesController(
        ApplicationDbContext db,
        IWebHostEnvironment environment)
    {
        _db = db;
        _environment = environment;
    }

    /// <summary>
    /// Allows an authenticated vendor to download only their own certificate.
    /// Each successful download is counted and written to the audit table.
    /// There is deliberately no certificate-generation action in this controller.
    /// </summary>
    [Authorize(Roles = "Vendor")]
    [HttpGet("Download/{id:int}")]
    public async Task<IActionResult> Download(
        int id,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        var certificate = await _db.VendorCertificates
            .AsNoTracking()
            .Where(c => c.VendorCertificateId == id && c.VendorProfile.UserId == userId)
            .Select(c => new
            {
                c.VendorCertificateId,
                c.CertificateNumber,
                c.PdfStorageKey,
                c.UcanNumber,
                c.IssuedAtUtc,
                c.ValidUntilUtc,
                c.IsRevoked,
                c.MaxDownloads,
                c.DownloadCount
            })
            .SingleOrDefaultAsync(cancellationToken);

        // Do not reveal whether another vendor's certificate exists.
        if (certificate is null)
            return NotFound("Certificate not found.");

        var now = DateTime.UtcNow;
        var unavailable = GetDownloadUnavailableResult(
            certificate.IsRevoked,
            certificate.ValidUntilUtc,
            certificate.DownloadCount,
            certificate.MaxDownloads,
            now);

        if (unavailable is not null)
            return unavailable;

        var pdfPath = ResolveCertificatePath(certificate.PdfStorageKey);
        if (pdfPath is null || !System.IO.File.Exists(pdfPath))
        {
            return NotFound(
                "The certificate PDF could not be found. Please contact the portal administrator.");
        }

        // The UCAN is required for the prescribed download filename.
        var safeUcan = new string(
            (certificate.UcanNumber ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .ToArray());

        if (string.IsNullOrWhiteSpace(safeUcan))
            return Problem("The UCAN is missing from this certificate. Please contact the portal administrator.");

        var downloadFileName = $"JH34-{certificate.IssuedAtUtc:yyyy}-{safeUcan}.pdf";

        // Read the file before consuming a download so a missing/unreadable file
        // does not use one of the vendor's five allowed downloads.
        var pdfBytes = await System.IO.File.ReadAllBytesAsync(pdfPath, cancellationToken);

        // Atomically increment the count. The database predicate enforces the
        // five-download limit even when the vendor opens multiple download requests
        // at nearly the same time or uses more than one browser session.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var updatedRows = await _db.VendorCertificates
            .Where(c => c.VendorCertificateId == id
                        && c.VendorProfile.UserId == userId
                        && !c.IsRevoked
                        && c.ValidUntilUtc > now
                        && c.DownloadCount < c.MaxDownloads)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(c => c.DownloadCount, c => c.DownloadCount + 1)
                .SetProperty(c => c.LastDownloadedAtUtc, now), cancellationToken);

        if (updatedRows != 1)
        {
            await transaction.RollbackAsync(cancellationToken);

            var latest = await _db.VendorCertificates
                .AsNoTracking()
                .Where(c => c.VendorCertificateId == id && c.VendorProfile.UserId == userId)
                .Select(c => new
                {
                    c.IsRevoked,
                    c.ValidUntilUtc,
                    c.MaxDownloads,
                    c.DownloadCount
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (latest is null)
                return NotFound("Certificate not found.");

            return GetDownloadUnavailableResult(
                       latest.IsRevoked,
                       latest.ValidUntilUtc,
                       latest.DownloadCount,
                       latest.MaxDownloads,
                       DateTime.UtcNow)
                   ?? Conflict("The certificate could not be downloaded. Please try again.");
        }

        _db.VendorCertificateDownloads.Add(new VendorCertificateDownload
        {
            VendorCertificateId = certificate.VendorCertificateId,
            DownloadedByUserId = userId,
            DownloadedAtUtc = now
        });

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return File(
            pdfBytes,
            "application/pdf",
            downloadFileName);
    }

    /// <summary>
    /// Public QR verification endpoint. It exposes certificate-verification
    /// details only, not PAN, GST, email, mobile number or other sensitive data.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("Verify/{token}")]
    public async Task<IActionResult> Verify(
        string token,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
            return NotFound("Invalid certificate verification link.");

        var certificate = await _db.VendorCertificates
            .AsNoTracking()
            .Where(c => c.VerificationToken == token)
            .Select(c => new CertificateVerificationViewModel
            {
                CertificateNumber = c.CertificateNumber,
                VendorName = c.VendorName,
                CompanyFirmName = c.CompanyFirmName,
                District = c.District,
                UcanNumber = c.UcanNumber,
                RegistrationType = c.RegistrationType,
                IssuedAtUtc = c.IssuedAtUtc,
                ValidUntilUtc = c.ValidUntilUtc,
                IsRevoked = c.IsRevoked
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (certificate is null)
            return NotFound("This certificate verification link is invalid.");

        var now = DateTime.UtcNow;
        certificate.Status = certificate.IsRevoked
            ? "REVOKED"
            : certificate.ValidUntilUtc <= now
                ? "EXPIRED"
                : "VALID";

        certificate.IsValid = certificate.Status == "VALID";

        return View("Verify", certificate);
    }

    private string? ResolveCertificatePath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
            return null;

        var certificatesRoot = Path.GetFullPath(Path.Combine(
            _environment.ContentRootPath,
            "App_Data",
            "Certificates"));

        var normalizedKey = storageKey
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        var fullPath = Path.GetFullPath(Path.Combine(
            _environment.ContentRootPath,
            "App_Data",
            normalizedKey));

        var rootPrefix = certificatesRoot.TrimEnd(Path.DirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!fullPath.StartsWith(rootPrefix, comparison))
            return null;

        if (!string.Equals(Path.GetExtension(fullPath), ".pdf", StringComparison.OrdinalIgnoreCase))
            return null;

        return fullPath;
    }

    private static IActionResult? GetDownloadUnavailableResult(
        bool isRevoked,
        DateTime validUntilUtc,
        int downloadCount,
        int maxDownloads,
        DateTime nowUtc)
    {
        if (isRevoked)
            return new StatusCodeResult(StatusCodes.Status410Gone);

        if (validUntilUtc <= nowUtc)
            return new ObjectResult("This certificate has expired and can no longer be downloaded.")
            {
                StatusCode = StatusCodes.Status410Gone
            };

        if (downloadCount >= maxDownloads)
            return new ObjectResult($"The certificate download limit of {maxDownloads} has been reached.")
            {
                StatusCode = StatusCodes.Status429TooManyRequests
            };

        return null;
    }
}
