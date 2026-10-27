using Microsoft.EntityFrameworkCore;
using VendorPortal.Data;
using VendorPortal.Models;

namespace VendorPortal.Services;

public class ApplicationService
{
    private readonly ApplicationDbContext _db;
    private readonly FileStorageService _files;

    private static readonly HashSet<DocumentType> OptionalTypes =
        new() { DocumentType.MiningDocument, DocumentType.Itr3Years, DocumentType.StockRegister };

    public ApplicationService(ApplicationDbContext db, FileStorageService files)
    {
        _db = db;
        _files = files;
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

        if (app.Status is not (ApplicationStatus.Draft or ApplicationStatus.CorrectionsRequired))
            return "The application is locked while it is being reviewed.";

        var doc = await _db.VendorDocuments.Include(d => d.Versions)
            .FirstAsync(d => d.ApplicationId == app.ApplicationId && d.DocumentType == type);

        if (app.Status == ApplicationStatus.CorrectionsRequired &&
            doc.Status is DocumentStatus.Pending or DocumentStatus.Approved)
            return "Only rejected or missing documents can be uploaded now.";

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
        var app = await _db.VendorApplications.Include(a => a.Documents)
            .FirstOrDefaultAsync(a => a.ApplicationId == applicationId);
        if (app is null) return "Application not found.";
        if (app.Status is not (ApplicationStatus.PendingReview or ApplicationStatus.UnderReview))
            return "This application is not open for a decision.";

        var previous = app.Status;
        ApplicationStatus next;
        switch (action)
        {
            case "Approve":
                if (app.Documents.Any(d => d.IsRequired && d.Status != DocumentStatus.Approved))
                    return "Every required document must be approved first.";
                next = ApplicationStatus.Approved;
                break;
            case "Reject":
                if (string.IsNullOrWhiteSpace(remarks)) return "Remarks are required to reject an application.";
                next = ApplicationStatus.Rejected;
                break;
            case "RequestCorrections":
                if (string.IsNullOrWhiteSpace(remarks)) return "Remarks are required when requesting corrections.";
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
            app.FinalDecisionAt = DateTime.UtcNow;
        }
        _db.ApplicationDecisionHistories.Add(new ApplicationDecisionHistory
        {
            ApplicationId = app.ApplicationId,
            AdminUserId = adminId,
            PreviousStatus = previous,
            NewStatus = next,
            Remarks = remarks?.Trim()
        });
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return null;
    }
}
