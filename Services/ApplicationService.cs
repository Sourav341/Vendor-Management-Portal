using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using VendorPortal.Data;
using VendorPortal.Models;

namespace VendorPortal.Services;

public class ApplicationService
{
    private readonly ApplicationDbContext _db;
    private readonly FileStorageService _files;
    private readonly CertificateService _certificates;
    private readonly IWebHostEnvironment _environment;

    private const string UcanPrefix = "JH34";
    private const int UcanRandomNumberLength = 4;
    private const int UcanRandomNumberLimit = 10_000;
    private const int CertificateValidityYears = 2;
    private const int MaxCertificateDownloads = 5;

    private const string UcanGenerationLockSql = @"
DECLARE @LockResult INT;
EXEC @LockResult = sys.sp_getapplock
    @Resource = N'VendorPortal_UCAN_Number_Generation',
    @LockMode = 'Exclusive',
    @LockOwner = 'Transaction',
    @LockTimeout = 15000;

IF @LockResult < 0
    THROW 51000, 'Could not acquire the UCAN generation lock.', 1;";

    private static readonly HashSet<DocumentType> OptionalTypes =
        new() { DocumentType.MiningDocument, DocumentType.Itr3Years, DocumentType.StockRegister };

    public ApplicationService(
        ApplicationDbContext db,
        FileStorageService files,
        CertificateService certificates,
        IWebHostEnvironment environment)
    {
        _db = db;
        _files = files;
        _certificates = certificates;
        _environment = environment;
    }

    public async Task EnsureApplicationAsync(string userId)
    {
        var profile = await _db.VendorProfiles.FirstAsync(p => p.UserId == userId);
        if (await _db.VendorApplications.AnyAsync(a => a.VendorProfileId == profile.VendorProfileId)) return;

        var app = new VendorApplication { VendorProfileId = profile.VendorProfileId };
        foreach (var t in Enum.GetValues<DocumentType>())
            app.Documents.Add(new VendorDocument { DocumentType = t, IsRequired = !OptionalTypes.Contains(t) });
        _db.VendorApplications.Add(app);
        await _db.SaveChangesAsync();
    }

    public async Task<VendorApplication> GetForVendorAsync(string userId)
    {
        await EnsureApplicationAsync(userId);
        return await _db.VendorApplications
            .Include(a => a.VendorProfile)
            .Include(a => a.DecisionHistory)
            .Include(a => a.Documents).ThenInclude(d => d.Versions).ThenInclude(v => v.Reviews)
            .Where(a => a.VendorProfile.UserId == userId)
            .OrderByDescending(a => a.ApplicationId)
            .AsSplitQuery()
            .FirstAsync();
    }

    public Task<VendorApplication?> GetForAdminAsync(int id) =>
        _db.VendorApplications
            .Include(a => a.VendorProfile).ThenInclude(p => p.User)
            .Include(a => a.DecisionHistory).ThenInclude(h => h.Admin)
            .Include(a => a.Documents).ThenInclude(d => d.Versions).ThenInclude(v => v.Reviews).ThenInclude(r => r.Reviewer)
            .AsSplitQuery()
            .FirstOrDefaultAsync(a => a.ApplicationId == id);

    public async Task<string?> UploadAsync(string userId, DocumentType type, IFormFile? file)
    {
        await EnsureApplicationAsync(userId);
        var app = await _db.VendorApplications
            .Where(a => a.VendorProfile.UserId == userId)
            .OrderByDescending(a => a.ApplicationId).FirstAsync();

        var doc = await _db.VendorDocuments.Include(d => d.Versions)
            .FirstAsync(d => d.ApplicationId == app.ApplicationId && d.DocumentType == type);

        var canUploadAsDraft = app.Status == ApplicationStatus.Draft;
        var canUploadDuringCorrections =
            app.Status == ApplicationStatus.CorrectionsRequired &&
            (doc.Status is DocumentStatus.NotSubmitted or DocumentStatus.Rejected);
        var canUploadDuringReview =
            (app.Status is ApplicationStatus.PendingReview or ApplicationStatus.UnderReview) &&
            (doc.Status is DocumentStatus.NotSubmitted or DocumentStatus.Rejected);

        if (!canUploadAsDraft && !canUploadDuringCorrections && !canUploadDuringReview)
            return "Only rejected or not-submitted documents can be uploaded while an application is under review.";

        var error = FileStorageService.Validate(file, type);
        if (error != null) return error;

        var (key, contentType) = await _files.SaveAsync(file!);
        doc.Versions.Add(new DocumentVersion
        {
            VersionNumber = doc.Versions.Count == 0 ? 1 : doc.Versions.Max(v => v.VersionNumber) + 1,
            StorageKey = key,
            OriginalFileName = Path.GetFileName(file!.FileName),
            ContentType = contentType,
            SizeBytes = file.Length
        });
        doc.Status = DocumentStatus.Pending;
        await _db.SaveChangesAsync();
        return null;
    }

    public async Task<string?> SubmitAsync(string userId)
    {
        var app = await GetForVendorAsync(userId);
        if (app.Status is not (ApplicationStatus.Draft or ApplicationStatus.CorrectionsRequired))
            return "This application has already been submitted.";

        var missing = app.Documents.Where(d => d.IsRequired && d.Status == DocumentStatus.NotSubmitted)
                                   .Select(d => d.DocumentType.DisplayName()).ToList();
        if (missing.Count > 0) return "Upload all required documents first: " + string.Join(", ", missing) + ".";
        if (app.Documents.Any(d => d.Status == DocumentStatus.Rejected))
            return "Replace all rejected documents before resubmitting.";

        app.Status = ApplicationStatus.PendingReview;
        app.SubmittedAt ??= DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return null;
    }

    public async Task<string?> ReviewDocumentAsync(string adminId, int vendorDocumentId, ReviewDecision decision, string? remarks)
    {
        var doc = await _db.VendorDocuments.Include(d => d.Application).Include(d => d.Versions)
            .FirstOrDefaultAsync(d => d.VendorDocumentId == vendorDocumentId);
        if (doc is null) return "Document not found.";
        if (doc.Application.Status is not (ApplicationStatus.PendingReview or ApplicationStatus.UnderReview))
            return "This application is not open for review.";
        if (doc.Status != DocumentStatus.Pending) return "There is no pending upload to review.";
        if (decision == ReviewDecision.Rejected && string.IsNullOrWhiteSpace(remarks))
            return "Remarks are required when rejecting a document.";

        var version = doc.Versions.OrderByDescending(v => v.VersionNumber).First();
        _db.DocumentReviewHistories.Add(new DocumentReviewHistory
        {
            DocumentVersionId = version.DocumentVersionId,
            ReviewerUserId = adminId,
            Decision = decision,
            Remarks = remarks?.Trim()
        });
        doc.Status = decision == ReviewDecision.Approved ? DocumentStatus.Approved : DocumentStatus.Rejected;
        doc.Application.Status = ApplicationStatus.UnderReview;
        await _db.SaveChangesAsync();
        return null;
    }

    public async Task<string?> FinalDecisionAsync(string adminId, int applicationId, string action, string? remarks)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        string? generatedPdfStorageKey = null;

        try
        {
            var app = await _db.VendorApplications
                .Include(a => a.Documents)
                .Include(a => a.VendorProfile)
                .FirstOrDefaultAsync(a => a.ApplicationId == applicationId);

            if (app is null) return "Application not found.";
            if (app.Status is not (ApplicationStatus.PendingReview or ApplicationStatus.UnderReview))
                return "This application is not open for a decision.";

            var previous = app.Status;
            var decisionAtUtc = DateTime.UtcNow;
            ApplicationStatus next;

            switch (action)
            {
                case "Approve":
                    if (app.Documents.Any(d => d.IsRequired && d.Status != DocumentStatus.Approved))
                        return "Every required document must be approved first.";

                    if (app.VendorProfile is null)
                        return "Vendor profile not found. UCAN cannot be generated.";

                    // Prevent a second certificate from being issued for the same application.
                    if (await _db.VendorCertificates.AnyAsync(c => c.ApplicationId == app.ApplicationId))
                        return "A certificate has already been issued for this application.";

                    // The vendor keeps the same UCAN. It is generated only on the first approval.
                    var ucanError = await AssignUcanIfMissingAsync(app.VendorProfile);
                    if (ucanError != null)
                        return ucanError;

                    next = ApplicationStatus.Approved;
                    break;

                case "Reject":
                    if (string.IsNullOrWhiteSpace(remarks))
                        return "Remarks are required to reject an application.";
                    next = ApplicationStatus.Rejected;
                    break;

                case "RequestCorrections":
                    if (string.IsNullOrWhiteSpace(remarks))
                        return "Remarks are required when requesting corrections.";
                    if (!app.Documents.Any(d => d.Status == DocumentStatus.Rejected))
                        return "Reject at least one document before requesting corrections.";
                    next = ApplicationStatus.CorrectionsRequired;
                    break;

                default:
                    return "Unknown action.";
            }

            app.Status = next;
            if (next is ApplicationStatus.Approved or ApplicationStatus.Rejected)
            {
                app.FinalDecisionById = adminId;
                app.FinalDecisionAt = decisionAtUtc;
            }

            _db.ApplicationDecisionHistories.Add(new ApplicationDecisionHistory
            {
                ApplicationId = app.ApplicationId,
                AdminUserId = adminId,
                PreviousStatus = previous,
                NewStatus = next,
                Remarks = remarks?.Trim()
            });

            // Certificate generation happens only as part of the Admin's final approval.
            // Vendors have no call to this method and cannot generate their own certificates.
            if (next == ApplicationStatus.Approved)
            {
                var profile = app.VendorProfile!;
                var certificate = new VendorCertificate
                {
                    VendorProfileId = profile.VendorProfileId,
                    ApplicationId = app.ApplicationId,
                    CertificateNumber = CreateCertificateNumber(decisionAtUtc),
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
                    IssuedAtUtc = decisionAtUtc,
                    ValidUntilUtc = decisionAtUtc.AddYears(CertificateValidityYears),
                    MaxDownloads = MaxCertificateDownloads,
                    DownloadCount = 0,
                    GeneratedByAdminId = adminId,
                    IsRevoked = false,
                    CreatedAtUtc = decisionAtUtc
                };

                generatedPdfStorageKey = await _certificates.GeneratePdfAsync(certificate);
                certificate.PdfStorageKey = generatedPdfStorageKey;
                _db.VendorCertificates.Add(certificate);
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return null;
        }
        catch
        {
            await tx.RollbackAsync();
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

    private void TryDeleteGeneratedCertificateFile(string? storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
            return;

        try
        {
            var certificatesRoot = Path.GetFullPath(Path.Combine(
                _environment.ContentRootPath, "App_Data", "Certificates"));
            var certificatePath = Path.GetFullPath(Path.Combine(
                _environment.ContentRootPath,
                "App_Data",
                storageKey.Replace('/', Path.DirectorySeparatorChar)));
            var rootPrefix = certificatesRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (!certificatePath.StartsWith(rootPrefix, comparison))
                return;

            if (File.Exists(certificatePath))
                File.Delete(certificatePath);
        }
        catch
        {
            // Do not replace the original database or certificate-generation exception.
        }
    }

    private async Task<string?> AssignUcanIfMissingAsync(VendorProfile profile)
    {
        // A vendor receives one permanent UCAN. Never replace an existing value.
        if (!string.IsNullOrWhiteSpace(profile.UcanNumber))
            return null;

        if (string.IsNullOrWhiteSpace(profile.District))
            return "Vendor district is missing. UCAN cannot be generated.";

        // DistrictCode remains in the government master table only.
        // District_code can repeat across block/panchayat/village rows, so retrieve
        // the distinct code(s) for the saved district name.
        var districtCodes = await _db.MasterDistricts
            .AsNoTracking()
            .Where(d => d.DistrictName == profile.District)
            .Select(d => d.DistrictCode)
            .Distinct()
            .ToListAsync();

        if (districtCodes.Count == 0)
            return $"District '{profile.District}' was not found in the district master table.";

        if (districtCodes.Count > 1)
            return $"District '{profile.District}' maps to multiple district codes. Please correct the master data.";

        var districtCode = districtCodes[0];
        if (districtCode < 1 || districtCode > 99)
            return "The district code must be between 1 and 99 to generate a two-digit UCAN district segment.";

        // Serialize UCAN generation so two simultaneous approvals cannot select
        // the same four-digit suffix. The lock is held until this approval transaction commits.
        await _db.Database.ExecuteSqlRawAsync(UcanGenerationLockSql);

        // Another approval may have completed while this request was waiting for the generation lock.
        var currentUcan = await _db.VendorProfiles
            .AsNoTracking()
            .Where(p => p.VendorProfileId == profile.VendorProfileId)
            .Select(p => p.UcanNumber)
            .SingleAsync();

        if (!string.IsNullOrWhiteSpace(currentUcan))
        {
            profile.UcanNumber = currentUcan;
            return null;
        }

        var existingUcans = await _db.VendorProfiles
            .AsNoTracking()
            .Where(p => p.UcanNumber != null && p.UcanNumber != "")
            .Select(p => p.UcanNumber!)
            .ToListAsync();

        // The four-digit random suffix is globally unique across all districts.
        var usedRandomNumbers = existingUcans
            .Where(value => value.Length >= UcanRandomNumberLength)
            .Select(value => value.Substring(value.Length - UcanRandomNumberLength))
            .Where(value => value.Length == UcanRandomNumberLength && value.All(char.IsDigit))
            .ToHashSet(StringComparer.Ordinal);

        var availableRandomNumbers = Enumerable.Range(0, UcanRandomNumberLimit)
            .Select(number => number.ToString("D4"))
            .Where(number => !usedRandomNumbers.Contains(number))
            .ToList();

        if (availableRandomNumbers.Count == 0)
            return "All four-digit UCAN random numbers have been allocated. No new UCAN can be issued.";

        var randomNumber = availableRandomNumbers[
            RandomNumberGenerator.GetInt32(availableRandomNumbers.Count)];

        profile.UcanNumber = $"{UcanPrefix}{districtCode:D2}{randomNumber}";
        return null;
    }
}
