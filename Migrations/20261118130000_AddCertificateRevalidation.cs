using Microsoft.EntityFrameworkCore.Migrations;
using VendorPortal.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace VendorPortal.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261118130000_AddCertificateRevalidation")]
public partial class AddCertificateRevalidation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_VendorCertificates_ApplicationId",
            table: "VendorCertificates");

        migrationBuilder.CreateIndex(
            name: "IX_VendorCertificates_ApplicationId",
            table: "VendorCertificates",
            column: "ApplicationId");

        migrationBuilder.CreateTable(
            name: "CertificateRevalidationCycles",
            columns: table => new
            {
                CertificateRevalidationCycleId = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ApplicationId = table.Column<int>(type: "int", nullable: false),
                Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                RequestedByAdminId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                ApprovedByAdminId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CertificateRevalidationCycles", x => x.CertificateRevalidationCycleId);
                table.ForeignKey(
                    name: "FK_CertificateRevalidationCycles_AspNetUsers_ApprovedByAdminId",
                    column: x => x.ApprovedByAdminId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CertificateRevalidationCycles_AspNetUsers_RequestedByAdminId",
                    column: x => x.RequestedByAdminId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CertificateRevalidationCycles_VendorApplications_ApplicationId",
                    column: x => x.ApplicationId,
                    principalTable: "VendorApplications",
                    principalColumn: "ApplicationId",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "CertificateRevalidationDocuments",
            columns: table => new
            {
                CertificateRevalidationDocumentId = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                CertificateRevalidationCycleId = table.Column<int>(type: "int", nullable: false),
                VendorDocumentId = table.Column<int>(type: "int", nullable: false),
                SubmittedVersionId = table.Column<int>(type: "int", nullable: true),
                Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                RequestedByAdminId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                ReviewedByAdminId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                ReviewRemarks = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CertificateRevalidationDocuments", x => x.CertificateRevalidationDocumentId);
                table.ForeignKey(
                    name: "FK_CertificateRevalidationDocuments_AspNetUsers_RequestedByAdminId",
                    column: x => x.RequestedByAdminId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CertificateRevalidationDocuments_AspNetUsers_ReviewedByAdminId",
                    column: x => x.ReviewedByAdminId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CertificateRevalidationDocuments_CertificateRevalidationCycles_CertificateRevalidationCycleId",
                    column: x => x.CertificateRevalidationCycleId,
                    principalTable: "CertificateRevalidationCycles",
                    principalColumn: "CertificateRevalidationCycleId",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CertificateRevalidationDocuments_DocumentVersions_SubmittedVersionId",
                    column: x => x.SubmittedVersionId,
                    principalTable: "DocumentVersions",
                    principalColumn: "DocumentVersionId",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CertificateRevalidationDocuments_VendorDocuments_VendorDocumentId",
                    column: x => x.VendorDocumentId,
                    principalTable: "VendorDocuments",
                    principalColumn: "VendorDocumentId",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_CertificateRevalidationCycles_ApplicationId",
            table: "CertificateRevalidationCycles",
            column: "ApplicationId",
            unique: true,
            filter: "[Status] = 'Open'");

        migrationBuilder.CreateIndex(
            name: "IX_CertificateRevalidationCycles_ApprovedByAdminId",
            table: "CertificateRevalidationCycles",
            column: "ApprovedByAdminId");

        migrationBuilder.CreateIndex(
            name: "IX_CertificateRevalidationCycles_RequestedByAdminId",
            table: "CertificateRevalidationCycles",
            column: "RequestedByAdminId");

        migrationBuilder.CreateIndex(
            name: "IX_CertificateRevalidationDocuments_CertificateRevalidationCycleId_VendorDocumentId",
            table: "CertificateRevalidationDocuments",
            columns: new[] { "CertificateRevalidationCycleId", "VendorDocumentId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_CertificateRevalidationDocuments_RequestedByAdminId",
            table: "CertificateRevalidationDocuments",
            column: "RequestedByAdminId");

        migrationBuilder.CreateIndex(
            name: "IX_CertificateRevalidationDocuments_ReviewedByAdminId",
            table: "CertificateRevalidationDocuments",
            column: "ReviewedByAdminId");

        migrationBuilder.CreateIndex(
            name: "IX_CertificateRevalidationDocuments_SubmittedVersionId",
            table: "CertificateRevalidationDocuments",
            column: "SubmittedVersionId");

        migrationBuilder.CreateIndex(
            name: "IX_CertificateRevalidationDocuments_VendorDocumentId",
            table: "CertificateRevalidationDocuments",
            column: "VendorDocumentId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "CertificateRevalidationDocuments");
        migrationBuilder.DropTable(name: "CertificateRevalidationCycles");

        migrationBuilder.DropIndex(
            name: "IX_VendorCertificates_ApplicationId",
            table: "VendorCertificates");

        migrationBuilder.CreateIndex(
            name: "IX_VendorCertificates_ApplicationId",
            table: "VendorCertificates",
            column: "ApplicationId",
            unique: true);
    }
}
