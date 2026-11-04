using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendorPortal.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorCertificates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UcanGeneratedAt",
                table: "VendorProfiles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UcanRandomNumber",
                table: "VendorProfiles",
                type: "nvarchar(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VendorCertificates",
                columns: table => new
                {
                    VendorCertificateId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VendorProfileId = table.Column<int>(type: "int", nullable: false),
                    ApplicationId = table.Column<int>(type: "int", nullable: false),
                    CertificateNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    VerificationToken = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PdfStorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    VendorName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    CompanyFirmName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    FatherOrHusbandName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    DateOfBirth = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Gender = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    RegisteredAddress = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    District = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PanNumber = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    GstNumber = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    UcanNumber = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    RegistrationType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    IssuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ValidUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MaxDownloads = table.Column<int>(type: "int", nullable: false, defaultValue: 5),
                    DownloadCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    LastDownloadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GeneratedByAdminId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    IsRevoked = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevocationReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorCertificates", x => x.VendorCertificateId);
                    table.ForeignKey(
                        name: "FK_VendorCertificates_AspNetUsers_GeneratedByAdminId",
                        column: x => x.GeneratedByAdminId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VendorCertificates_VendorApplications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "VendorApplications",
                        principalColumn: "ApplicationId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VendorCertificates_VendorProfiles_VendorProfileId",
                        column: x => x.VendorProfileId,
                        principalTable: "VendorProfiles",
                        principalColumn: "VendorProfileId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VendorCertificateDownloads",
                columns: table => new
                {
                    VendorCertificateDownloadId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VendorCertificateId = table.Column<int>(type: "int", nullable: false),
                    DownloadedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DownloadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorCertificateDownloads", x => x.VendorCertificateDownloadId);
                    table.ForeignKey(
                        name: "FK_VendorCertificateDownloads_AspNetUsers_DownloadedByUserId",
                        column: x => x.DownloadedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VendorCertificateDownloads_VendorCertificates_VendorCertificateId",
                        column: x => x.VendorCertificateId,
                        principalTable: "VendorCertificates",
                        principalColumn: "VendorCertificateId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VendorCertificateDownloads_DownloadedByUserId",
                table: "VendorCertificateDownloads",
                column: "DownloadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorCertificateDownloads_VendorCertificateId_DownloadedAtUtc",
                table: "VendorCertificateDownloads",
                columns: new[] { "VendorCertificateId", "DownloadedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_VendorCertificates_ApplicationId",
                table: "VendorCertificates",
                column: "ApplicationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VendorCertificates_CertificateNumber",
                table: "VendorCertificates",
                column: "CertificateNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VendorCertificates_GeneratedByAdminId",
                table: "VendorCertificates",
                column: "GeneratedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorCertificates_VendorProfileId_IssuedAtUtc",
                table: "VendorCertificates",
                columns: new[] { "VendorProfileId", "IssuedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_VendorCertificates_VerificationToken",
                table: "VendorCertificates",
                column: "VerificationToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VendorCertificateDownloads");

            migrationBuilder.DropTable(
                name: "VendorCertificates");

            migrationBuilder.DropColumn(
                name: "UcanGeneratedAt",
                table: "VendorProfiles");

            migrationBuilder.DropColumn(
                name: "UcanRandomNumber",
                table: "VendorProfiles");
        }
    }
}
