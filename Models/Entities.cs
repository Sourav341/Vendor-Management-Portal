using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace VendorPortal.Models;

public class ApplicationUser : IdentityUser
{
    public VendorProfile? VendorProfile { get; set; }
}

public class VendorProfile
{
    [Key]
    public int VendorProfileId { get; set; }

    public string UserId { get; set; } = default!;
    public ApplicationUser User { get; set; } = default!;

    [Required, MaxLength(150)]
    public string FullName { get; set; } = default!;

    [Required, MaxLength(10)]
    public string PanNumber { get; set; } = default!;

    [Required, MaxLength(15)]
    public string MobileNumber { get; set; } = default!;

    [Required, MaxLength(15)]
    public string GstNumber { get; set; } = default!;

    [Required(ErrorMessage = "District is required for certificate generation.")]
    [MaxLength(100)]
    public string District { get; set; } = default!;

    // Generated only after the vendor's first successful Admin approval.
    // Format: JH34 + two-digit DistrictCode + unique four-digit random number.
    [MaxLength(10)]
    public string? UcanNumber { get; set; }

    // Stored separately so the four-digit random part can be unique globally.
    [MaxLength(4)]
    public string? UcanRandomNumber { get; set; }

    public DateTime? UcanGeneratedAt { get; set; }

    [MaxLength(10)]
    public string? Gender { get; set; }

    [MaxLength(20)]
    public string? DOB { get; set; }

    [MaxLength(150)]
    public string? Father_Husband_Name { get; set; }

    [MaxLength(450)]
    public string? Trade_Address { get; set; }

    [MaxLength(150)]
    public string? TradeName { get; set; }

    // Work type values come from the existing tbl_WorkType reference table.
    [MaxLength(10)]
    public string? Cert_Type { get; set; }

    [MaxLength(100)]
    public string? SubWorkType { get; set; }

    public string? Remarks { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<VendorApplication> Applications { get; set; } = new();
    public List<VendorCertificate> Certificates { get; set; } = new();
    public List<VendorAdditionalDetailRequest> AdditionalDetailRequests { get; set; } = new();
}

public class MasterDistrict
{
    public int Id { get; set; }
    public int DistrictCode { get; set; }
    public string DistrictName { get; set; } = string.Empty;
}

public class MasterWorkType
{
    public int Id { get; set; }
    public string WorkType { get; set; } = string.Empty;
    public string SubWorkType { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
}

public class VendorApplication
{
    [Key]
    public int ApplicationId { get; set; }
    public int VendorProfileId { get; set; }
    public VendorProfile VendorProfile { get; set; } = default!;
    public ApplicationStatus Status { get; set; } = ApplicationStatus.Draft;
    public DateTime? SubmittedAt { get; set; }
    public string? FinalDecisionById { get; set; }
    public ApplicationUser? FinalDecisionBy { get; set; }
    public DateTime? FinalDecisionAt { get; set; }
    public List<VendorDocument> Documents { get; set; } = new();
    public List<ApplicationDecisionHistory> DecisionHistory { get; set; } = new();
    public List<VendorCertificate> Certificates { get; set; } = new();
    public List<CertificateRevalidationCycle> RevalidationCycles { get; set; } = new();
    public List<VendorAdditionalDetailRequest> AdditionalDetailRequests { get; set; } = new();
}


/// <summary>
/// One row in a vendor's grouped request for approved district/work-type coverage.
/// Rows in one batch share RequestBatchId so one Admin decision issues at most one certificate.
/// </summary>
public class VendorAdditionalDetailRequest
{
    [Key]
    public int VendorAdditionalDetailRequestId { get; set; }

    public Guid RequestBatchId { get; set; }

    public int VendorProfileId { get; set; }
    public VendorProfile VendorProfile { get; set; } = default!;

    public int ApplicationId { get; set; }
    public VendorApplication Application { get; set; } = default!;

    [Required, MaxLength(100)]
    public string District { get; set; } = default!;

    [Required, MaxLength(100)]
    public string WorkType { get; set; } = default!;

    [Required, MaxLength(150)]
    public string SubWorkType { get; set; } = default!;

    [MaxLength(4000)]
    public string? Remarks { get; set; }

    public AdditionalDetailStatus Status { get; set; } = AdditionalDetailStatus.PendingReview;

    [Required]
    public string RequestedByVendorId { get; set; } = default!;
    public ApplicationUser RequestedByVendor { get; set; } = default!;

    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;

    public string? ReviewedByAdminId { get; set; }
    public ApplicationUser? ReviewedByAdmin { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }

    [MaxLength(1000)]
    public string? ReviewRemarks { get; set; }
}

/// <summary>
/// Immutable display-only work coverage captured on a certificate at issue time.
/// Remarks deliberately do not appear in this snapshot.
/// </summary>
public class CertificateAdditionalDetailSnapshot
{
    public string District { get; set; } = string.Empty;
    public string WorkType { get; set; } = string.Empty;
    public string SubWorkType { get; set; } = string.Empty;
}

/// <summary>
/// An immutable certificate issued after an Admin's successful final approval.
/// A new record is created for each issue/update. Versions share the UCAN validity
/// period and each begins with its own download allowance.
/// </summary>
public class VendorCertificate
{
    [Key]
    public int VendorCertificateId { get; set; }

    public int VendorProfileId { get; set; }
    public VendorProfile VendorProfile { get; set; } = default!;

    public int ApplicationId { get; set; }
    public VendorApplication Application { get; set; } = default!;

    [Required, MaxLength(50)]
    public string CertificateNumber { get; set; } = default!;

    // Random token used by the QR code's public verification URL.
    // The QR code contains the URL/token, not the vendor's personal information.
    [Required, MaxLength(64)]
    public string VerificationToken { get; set; } = default!;

    // Private storage key for the generated PDF. Never expose this directly to the browser.
    [Required, MaxLength(500)]
    public string PdfStorageKey { get; set; } = default!;

    // Snapshot of the approved vendor details; later profile edits must not
    // silently change an already-issued certificate.
    [Required, MaxLength(150)]
    public string VendorName { get; set; } = default!;

    [MaxLength(150)]
    public string? CompanyFirmName { get; set; }

    [MaxLength(150)]
    public string? FatherOrHusbandName { get; set; }

    [MaxLength(20)]
    public string? DateOfBirth { get; set; }

    [MaxLength(10)]
    public string? Gender { get; set; }

    [MaxLength(450)]
    public string? RegisteredAddress { get; set; }

    [Required, MaxLength(100)]
    public string District { get; set; } = default!;

    [Required, MaxLength(10)]
    public string PanNumber { get; set; } = default!;

    [Required, MaxLength(15)]
    public string GstNumber { get; set; } = default!;

    [Required, MaxLength(10)]
    public string UcanNumber { get; set; } = default!;

    [MaxLength(10)]
    public string? RegistrationType { get; set; }

    // Snapshot of the selected sub-work type at issuance.
    [MaxLength(100)]
    public string? SubWorkType { get; set; }

    // Immutable JSON snapshot of approved District / Work Type / Sub Work Type rows.
    public string? AdditionalDetailsSnapshotJson { get; set; }

    public DateTime IssuedAtUtc { get; set; }
    public DateTime ValidUntilUtc { get; set; }

    // These are set by server-side certificate generation, never by the vendor.
    public int MaxDownloads { get; set; } = 15;
    public int DownloadCount { get; set; }
    public DateTime? LastDownloadedAtUtc { get; set; }

    public string? GeneratedByAdminId { get; set; }
    public ApplicationUser? GeneratedByAdmin { get; set; }

    public bool IsRevoked { get; set; }
    public DateTime? RevokedAtUtc { get; set; }

    [MaxLength(1000)]
    public string? RevocationReason { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<VendorCertificateDownload> Downloads { get; set; } = new();
}

/// <summary>
/// Audit record for each successful certificate download.
/// </summary>
public class VendorCertificateDownload
{
    [Key]
    public int VendorCertificateDownloadId { get; set; }

    public int VendorCertificateId { get; set; }
    public VendorCertificate VendorCertificate { get; set; } = default!;

    [Required]
    public string DownloadedByUserId { get; set; } = default!;
    public ApplicationUser DownloadedBy { get; set; } = default!;

    public DateTime DownloadedAtUtc { get; set; } = DateTime.UtcNow;
}

public class VendorDocument
{
    [Key]
    public int VendorDocumentId { get; set; }
    public int ApplicationId { get; set; }
    public VendorApplication Application { get; set; } = default!;
    public DocumentType DocumentType { get; set; }
    public bool IsRequired { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.NotSubmitted;
    public List<DocumentVersion> Versions { get; set; } = new();
}

public class DocumentVersion
{
    [Key]
    public int DocumentVersionId { get; set; }
    public int VendorDocumentId { get; set; }
    public VendorDocument VendorDocument { get; set; } = default!;
    public int VersionNumber { get; set; }
    [Required, MaxLength(300)] public string StorageKey { get; set; } = default!;
    [MaxLength(255)] public string OriginalFileName { get; set; } = default!;
    [MaxLength(100)] public string ContentType { get; set; } = default!;
    public long SizeBytes { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public string? UploadedByUserId { get; set; }
    public ApplicationUser? UploadedBy { get; set; }
    public List<DocumentReviewHistory> Reviews { get; set; } = new();
}

public class DocumentReviewHistory
{
    [Key]
    public int ReviewId { get; set; }
    public int DocumentVersionId { get; set; }
    public DocumentVersion DocumentVersion { get; set; } = default!;
    public string ReviewerUserId { get; set; } = default!;
    public ApplicationUser Reviewer { get; set; } = default!;
    public ReviewDecision Decision { get; set; }
    [MaxLength(1000)] public string? Remarks { get; set; }
    public DateTime ReviewedAt { get; set; } = DateTime.UtcNow;
}

public class ApplicationDecisionHistory
{
    [Key]
    public int DecisionHistoryId { get; set; }
    public int ApplicationId { get; set; }
    public VendorApplication Application { get; set; } = default!;
    public string AdminUserId { get; set; } = default!;
    public ApplicationUser Admin { get; set; } = default!;
    public ApplicationStatus PreviousStatus { get; set; }
    public ApplicationStatus NewStatus { get; set; }
    [MaxLength(1000)] public string? Remarks { get; set; }
    public DateTime DecidedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A document revalidation round for an already approved vendor. It remains
/// separate from the original application so previous approval and certificate
/// history stay intact.
/// </summary>
public class CertificateRevalidationCycle
{
    [Key]
    public int CertificateRevalidationCycleId { get; set; }

    public int ApplicationId { get; set; }
    public VendorApplication Application { get; set; } = default!;

    public CertificateRevalidationStatus Status { get; set; } = CertificateRevalidationStatus.Open;

    [Required]
    public string RequestedByAdminId { get; set; } = default!;
    public ApplicationUser RequestedByAdmin { get; set; } = default!;
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;

    public string? ApprovedByAdminId { get; set; }
    public ApplicationUser? ApprovedByAdmin { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }

    public List<CertificateRevalidationDocument> Documents { get; set; } = new();
}

/// <summary>
/// One document requested during a revalidation cycle. SubmittedVersionId
/// pins approval to the exact file version submitted for this request.
/// </summary>
public class CertificateRevalidationDocument
{
    [Key]
    public int CertificateRevalidationDocumentId { get; set; }

    public int CertificateRevalidationCycleId { get; set; }
    public CertificateRevalidationCycle Cycle { get; set; } = default!;

    public int VendorDocumentId { get; set; }
    public VendorDocument VendorDocument { get; set; } = default!;

    public int? SubmittedVersionId { get; set; }
    public DocumentVersion? SubmittedVersion { get; set; }

    public DocumentStatus Status { get; set; } = DocumentStatus.NotSubmitted;

    [Required]
    public string RequestedByAdminId { get; set; } = default!;
    public ApplicationUser RequestedByAdmin { get; set; } = default!;
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;

    public string? ReviewedByAdminId { get; set; }
    public ApplicationUser? ReviewedByAdmin { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }

    [MaxLength(1000)]
    public string? ReviewRemarks { get; set; }
}
