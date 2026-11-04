using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendorPortal.Migrations
{
    public partial class AddVendorUcanNumber : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UcanNumber",
                table: "VendorProfiles",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VendorProfiles_UcanNumber",
                table: "VendorProfiles",
                column: "UcanNumber",
                unique: true,
                filter: "[UcanNumber] IS NOT NULL AND [UcanNumber] <> ''");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VendorProfiles_UcanNumber",
                table: "VendorProfiles");

            migrationBuilder.DropColumn(
                name: "UcanNumber",
                table: "VendorProfiles");
        }
    }
}
