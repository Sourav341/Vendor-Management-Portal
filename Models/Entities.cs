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

    [Required, MaxLength(150)] public string FullName { get; set; } = default!;
    [Required, MaxLength(10)] public string PanNumber { get; set; } = default!;
    [Required, MaxLength(15)] public string MobileNumber { get; set; } = default!;
    [Required, MaxLength(15)] public string GstNumber { get; set; } = default!;
    [MaxLength(150)] public string? TradeName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<VendorApplication> Applications { get; set; } = new();
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
