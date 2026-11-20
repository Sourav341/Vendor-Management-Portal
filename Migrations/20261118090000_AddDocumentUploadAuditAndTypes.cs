using Microsoft.EntityFrameworkCore.Migrations;
using VendorPortal.Data;

#nullable disable

namespace VendorPortal.Migrations
{
    public partial class AddDocumentUploadAuditAndTypes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UploadedByUserId",
                table: "DocumentVersions",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersions_UploadedByUserId",
                table: "DocumentVersions",
                column: "UploadedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentVersions_AspNetUsers_UploadedByUserId",
                table: "DocumentVersions",
                column: "UploadedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(@"
INSERT INTO dbo.VendorDocuments (ApplicationId, DocumentType, IsRequired, Status)
SELECT a.ApplicationId,
       newDocs.DocumentType,
       CASE WHEN a.Status IN (N'Draft', N'PendingReview', N'UnderReview', N'CorrectionsRequired')
            THEN 1 ELSE 0 END,
       N'NotSubmitted'
FROM dbo.VendorApplications AS a
CROSS JOIN (VALUES
    (N'AffidavitNoBloodRelation'),
    (N'ItemLicense')
) AS newDocs(DocumentType)
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.VendorDocuments AS existingDoc
    WHERE existingDoc.ApplicationId = a.ApplicationId
      AND existingDoc.DocumentType = newDocs.DocumentType
);");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DELETE FROM dbo.VendorDocuments
WHERE DocumentType IN (N'AffidavitNoBloodRelation', N'ItemLicense');");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentVersions_AspNetUsers_UploadedByUserId",
                table: "DocumentVersions");

            migrationBuilder.DropIndex(
                name: "IX_DocumentVersions_UploadedByUserId",
                table: "DocumentVersions");

            migrationBuilder.DropColumn(
                name: "UploadedByUserId",
                table: "DocumentVersions");
        }
    }
}