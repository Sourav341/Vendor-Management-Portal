using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using VendorPortal.Models;

namespace VendorPortal.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<VendorProfile> VendorProfiles => Set<VendorProfile>();
    public DbSet<VendorApplication> VendorApplications => Set<VendorApplication>();
    public DbSet<VendorDocument> VendorDocuments => Set<VendorDocument>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();
    public DbSet<DocumentReviewHistory> DocumentReviewHistories => Set<DocumentReviewHistory>();
    public DbSet<ApplicationDecisionHistory> ApplicationDecisionHistories => Set<ApplicationDecisionHistory>();
    public DbSet<CertificateRevalidationCycle> CertificateRevalidationCycles => Set<CertificateRevalidationCycle>();
    public DbSet<CertificateRevalidationDocument> CertificateRevalidationDocuments => Set<CertificateRevalidationDocument>();
    public DbSet<MasterDistrict> MasterDistricts => Set<MasterDistrict>();
    public DbSet<MasterWorkType> MasterWorkTypes => Set<MasterWorkType>();
    public DbSet<VendorAdditionalDetailRequest> VendorAdditionalDetailRequests => Set<VendorAdditionalDetailRequest>();

    // Certificate records and their download audit history.
    public DbSet<VendorCertificate> VendorCertificates => Set<VendorCertificate>();
    public DbSet<VendorCertificateDownload> VendorCertificateDownloads => Set<VendorCertificateDownload>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // Read the existing government master table. Exclude it from EF migrations
        // because it is pre-existing reference data and must not be recreated by VendorPortal.
        b.Entity<MasterDistrict>(e =>
        {
            e.ToTable("tbl_Master_district", table => table.ExcludeFromMigrations());

            e.HasKey(x => x.Id);

            e.Property(x => x.Id)
                .HasColumnName("Id");

            e.Property(x => x.DistrictCode)
                .HasColumnName("District_code");

            e.Property(x => x.DistrictName)
                .HasColumnName("District_name")
                .HasMaxLength(40)
                .IsRequired();
        });

        // Read the existing work-type lookup table; it is managed outside this application's migrations.
        b.Entity<MasterWorkType>(e =>
        {
            e.ToTable("tbl_WorkType", table => table.ExcludeFromMigrations());
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("Id");
            e.Property(x => x.WorkType).HasColumnName("WorkType").IsRequired();
            e.Property(x => x.SubWorkType).HasColumnName("SubWorkType").IsRequired();
            e.Property(x => x.UploadedAt).HasColumnName("UploadedAt");
        });

        b.Entity<VendorProfile>(e =>
        {
            e.HasIndex(x => x.UserId).IsUnique();
            e.HasIndex(x => x.PanNumber).IsUnique();
            e.HasIndex(x => x.GstNumber).IsUnique();

            e.HasOne(x => x.User)
                .WithOne(u => u.VendorProfile)
                .HasForeignKey<VendorProfile>(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // District name is required for registration and certificate generation.
            e.Property(x => x.District)
                .HasMaxLength(100)
                .IsRequired();

            // UCAN is generated only after the first successful Admin approval.
            // NULL is allowed for vendors whose applications are not yet approved.
            e.Property(x => x.UcanNumber)
                .HasMaxLength(10)
                .IsRequired(false);

            // Prevent duplicate issued UCANs while allowing NULL/empty values
            // for vendors who do not have a UCAN yet.
            e.HasIndex(x => x.UcanNumber)
                .IsUnique()
                .HasFilter("[UcanNumber] IS NOT NULL AND [UcanNumber] <> ''");

            // Certificate type is optional until provided by the vendor.
            e.Property(x => x.Cert_Type)
                .HasMaxLength(10)
                .IsRequired(false);

            e.Property(x => x.SubWorkType)
                .HasMaxLength(100)
                .IsRequired(false);

            e.Property(x => x.Remarks)
                .IsRequired(false);
        });

        b.Entity<VendorApplication>(e =>
        {
            e.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(30);

            // Final decision is optional until an application is decided.
            e.Property(x => x.FinalDecisionById)
                .IsRequired(false);

            e.HasOne(x => x.FinalDecisionBy)
                .WithMany()
                .HasForeignKey(x => x.FinalDecisionById)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<VendorDocument>(e =>
        {
            e.Property(x => x.DocumentType)
                .HasConversion<string>()
                .HasMaxLength(40);

            e.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

            e.HasIndex(x => new { x.ApplicationId, x.DocumentType })
                .IsUnique();
        });

        b.Entity<DocumentVersion>(e =>
        {
            e.HasIndex(x => new { x.VendorDocumentId, x.VersionNumber })
                .IsUnique();

            e.HasIndex(x => x.UploadedByUserId);

            e.HasOne(x => x.UploadedBy)
                .WithMany()
                .HasForeignKey(x => x.UploadedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<DocumentReviewHistory>(e =>
        {
            e.Property(x => x.Decision)
                .HasConversion<string>()
                .HasMaxLength(20);

            e.Property(x => x.Remarks)
                .IsRequired(false);

            e.HasOne(x => x.Reviewer)
                .WithMany()
                .HasForeignKey(x => x.ReviewerUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ApplicationDecisionHistory>(e =>
        {
            e.Property(x => x.PreviousStatus)
                .HasConversion<string>()
                .HasMaxLength(30);

            e.Property(x => x.NewStatus)
                .HasConversion<string>()
                .HasMaxLength(30);

            e.Property(x => x.Remarks)
                .IsRequired(false);

            e.HasOne(x => x.Admin)
                .WithMany()
                .HasForeignKey(x => x.AdminUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<CertificateRevalidationCycle>(e =>
        {
            e.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

            // Only one open document revalidation round may exist per application.
            e.HasIndex(x => x.ApplicationId)
                .IsUnique()
                .HasFilter("[Status] = 'Open'");

            e.HasOne(x => x.Application)
                .WithMany(x => x.RevalidationCycles)
                .HasForeignKey(x => x.ApplicationId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.RequestedByAdmin)
                .WithMany()
                .HasForeignKey(x => x.RequestedByAdminId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.ApprovedByAdmin)
                .WithMany()
                .HasForeignKey(x => x.ApprovedByAdminId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<CertificateRevalidationDocument>(e =>
        {
            e.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

            e.Property(x => x.ReviewRemarks)
                .HasMaxLength(1000)
                .IsRequired(false);

            e.HasIndex(x => new { x.CertificateRevalidationCycleId, x.VendorDocumentId })
                .IsUnique();

            e.HasOne(x => x.Cycle)
                .WithMany(x => x.Documents)
                .HasForeignKey(x => x.CertificateRevalidationCycleId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.VendorDocument)
                .WithMany()
                .HasForeignKey(x => x.VendorDocumentId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.SubmittedVersion)
                .WithMany()
                .HasForeignKey(x => x.SubmittedVersionId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.RequestedByAdmin)
                .WithMany()
                .HasForeignKey(x => x.RequestedByAdminId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.ReviewedByAdmin)
                .WithMany()
                .HasForeignKey(x => x.ReviewedByAdminId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<VendorAdditionalDetailRequest>(e =>
        {
            e.Property(x => x.District).HasMaxLength(100).IsRequired();
            e.Property(x => x.WorkType).HasMaxLength(100).IsRequired();
            e.Property(x => x.SubWorkType).HasMaxLength(150).IsRequired();
            e.Property(x => x.Remarks).HasMaxLength(4000).IsRequired(false);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
            e.Property(x => x.RequestedByVendorId).IsRequired();
            e.Property(x => x.ReviewRemarks).HasMaxLength(1000).IsRequired(false);

            e.HasIndex(x => new { x.VendorProfileId, x.RequestBatchId });
            e.HasIndex(x => new { x.ApplicationId, x.Status });

            e.HasOne(x => x.VendorProfile)
                .WithMany(x => x.AdditionalDetailRequests)
                .HasForeignKey(x => x.VendorProfileId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Application)
                .WithMany(x => x.AdditionalDetailRequests)
                .HasForeignKey(x => x.ApplicationId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.RequestedByVendor)
                .WithMany()
                .HasForeignKey(x => x.RequestedByVendorId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.ReviewedByAdmin)
                .WithMany()
                .HasForeignKey(x => x.ReviewedByAdminId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Certificate history may contain several issues for one application.
        b.Entity<VendorCertificate>(e =>
        {
            e.HasIndex(x => x.CertificateNumber)
                .IsUnique();

            e.HasIndex(x => x.VerificationToken)
                .IsUnique();

            // A revalidation creates a new certificate record for the same application.
            e.HasIndex(x => x.ApplicationId);

            e.HasIndex(x => new { x.VendorProfileId, x.IssuedAtUtc });

            e.Property(x => x.CertificateNumber)
                .HasMaxLength(50)
                .IsRequired();

            e.Property(x => x.VerificationToken)
                .HasMaxLength(64)
                .IsRequired();

            e.Property(x => x.PdfStorageKey)
                .HasMaxLength(500)
                .IsRequired();

            e.Property(x => x.VendorName)
                .HasMaxLength(150)
                .IsRequired();

            e.Property(x => x.CompanyFirmName).HasMaxLength(150);
            e.Property(x => x.FatherOrHusbandName).HasMaxLength(150);
            e.Property(x => x.DateOfBirth).HasMaxLength(20);
            e.Property(x => x.Gender).HasMaxLength(10);
            e.Property(x => x.RegisteredAddress).HasMaxLength(450);

            e.Property(x => x.District)
                .HasMaxLength(100)
                .IsRequired();

            e.Property(x => x.PanNumber)
                .HasMaxLength(10)
                .IsRequired();

            e.Property(x => x.GstNumber)
                .HasMaxLength(15)
                .IsRequired();

            e.Property(x => x.UcanNumber)
                .HasMaxLength(10)
                .IsRequired();

            e.Property(x => x.RegistrationType).HasMaxLength(10);
            e.Property(x => x.SubWorkType).HasMaxLength(100);
            e.Property(x => x.GeneratedByAdminId).IsRequired(false);
            e.Property(x => x.RevocationReason).HasMaxLength(1000);

            e.Property(x => x.MaxDownloads).HasDefaultValue(15);
            e.Property(x => x.DownloadCount).HasDefaultValue(0);
            e.Property(x => x.IsRevoked).HasDefaultValue(false);

            e.HasOne(x => x.VendorProfile)
                .WithMany(x => x.Certificates)
                .HasForeignKey(x => x.VendorProfileId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Application)
                .WithMany(x => x.Certificates)
                .HasForeignKey(x => x.ApplicationId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.GeneratedByAdmin)
                .WithMany()
                .HasForeignKey(x => x.GeneratedByAdminId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Each successful download is retained as an audit record.
        b.Entity<VendorCertificateDownload>(e =>
        {
            e.HasIndex(x => new { x.VendorCertificateId, x.DownloadedAtUtc });

            e.Property(x => x.DownloadedByUserId)
                .IsRequired();

            e.HasOne(x => x.VendorCertificate)
                .WithMany(x => x.Downloads)
                .HasForeignKey(x => x.VendorCertificateId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.DownloadedBy)
                .WithMany()
                .HasForeignKey(x => x.DownloadedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
