using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VendorPortal.Data;

#nullable disable

namespace VendorPortal.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261119140000_AddVendorAdditionalDetails")]
public partial class AddVendorAdditionalDetails : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AdditionalDetailsSnapshotJson",
            table: "VendorCertificates",
            type: "nvarchar(max)",
            nullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "MaxDownloads",
            table: "VendorCertificates",
            type: "int",
            nullable: false,
            defaultValue: 15,
            oldClrType: typeof(int),
            oldType: "int",
            oldDefaultValue: 5);

        migrationBuilder.CreateTable(
            name: "VendorAdditionalDetailRequests",
            columns: table => new
            {
                VendorAdditionalDetailRequestId = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                RequestBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                VendorProfileId = table.Column<int>(type: "int", nullable: false),
                ApplicationId = table.Column<int>(type: "int", nullable: false),
                District = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                WorkType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                SubWorkType = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                Remarks = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                RequestedByVendorId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                ReviewedByAdminId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                ReviewRemarks = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_VendorAdditionalDetailRequests",
                    x => x.VendorAdditionalDetailRequestId);
                table.ForeignKey(
                    name: "FK_VendorAdditionalDetailRequests_AspNetUsers_RequestedByVendorId",
                    column: x => x.RequestedByVendorId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_VendorAdditionalDetailRequests_AspNetUsers_ReviewedByAdminId",
                    column: x => x.ReviewedByAdminId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_VendorAdditionalDetailRequests_VendorApplications_ApplicationId",
                    column: x => x.ApplicationId,
                    principalTable: "VendorApplications",
                    principalColumn: "ApplicationId",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_VendorAdditionalDetailRequests_VendorProfiles_VendorProfileId",
                    column: x => x.VendorProfileId,
                    principalTable: "VendorProfiles",
                    principalColumn: "VendorProfileId",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_VendorAdditionalDetailRequests_ApplicationId_Status",
            table: "VendorAdditionalDetailRequests",
            columns: new[] { "ApplicationId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_VendorAdditionalDetailRequests_RequestedByVendorId",
            table: "VendorAdditionalDetailRequests",
            column: "RequestedByVendorId");

        migrationBuilder.CreateIndex(
            name: "IX_VendorAdditionalDetailRequests_ReviewedByAdminId",
            table: "VendorAdditionalDetailRequests",
            column: "ReviewedByAdminId");

        migrationBuilder.CreateIndex(
            name: "IX_VendorAdditionalDetailRequests_VendorProfileId_RequestBatchId",
            table: "VendorAdditionalDetailRequests",
            columns: new[] { "VendorProfileId", "RequestBatchId" });

        migrationBuilder.Sql("""
            UPDATE profile
            SET UcanGeneratedAt = COALESCE(
                (SELECT MIN(certificate.IssuedAtUtc)
                 FROM dbo.VendorCertificates AS certificate
                 WHERE certificate.VendorProfileId = profile.VendorProfileId),
                profile.CreatedAt)
            FROM dbo.VendorProfiles AS profile
            WHERE profile.UcanNumber IS NOT NULL
              AND profile.UcanNumber <> ''
              AND profile.UcanGeneratedAt IS NULL;

            UPDATE dbo.VendorCertificates
            SET MaxDownloads = 15
            WHERE MaxDownloads <> 15;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "VendorAdditionalDetailRequests");

        migrationBuilder.DropColumn(
            name: "AdditionalDetailsSnapshotJson",
            table: "VendorCertificates");

        migrationBuilder.AlterColumn<int>(
            name: "MaxDownloads",
            table: "VendorCertificates",
            type: "int",
            nullable: false,
            defaultValue: 5,
            oldClrType: typeof(int),
            oldType: "int",
            oldDefaultValue: 15);
    }
}
