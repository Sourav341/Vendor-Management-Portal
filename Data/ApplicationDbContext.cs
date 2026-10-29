
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

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<VendorProfile>(e =>
        {
            e.HasIndex(x => x.UserId).IsUnique();
            e.HasIndex(x => x.PanNumber).IsUnique();
            e.HasIndex(x => x.GstNumber).IsUnique();

            e.HasOne(x => x.User)
                .WithOne(u => u.VendorProfile)
                .HasForeignKey<VendorProfile>(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // District is mandatory for certificate generation.
            e.Property(x => x.District)
                .HasMaxLength(100)
                .IsRequired();

            // Certificate type is optional until provided.
            e.Property(x => x.Cert_Type)
                .HasMaxLength(10)
                .IsRequired(false);

            e.Property(x => x.Remarks)
                .IsRequired(false);
        });

        b.Entity<VendorApplication>(e =>
        {
            e.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(30);

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
    }
}
