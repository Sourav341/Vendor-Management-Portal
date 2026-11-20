using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VendorPortal.Data;

#nullable disable

namespace VendorPortal.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261119100000_AddSubWorkType")]
public partial class AddSubWorkType : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "SubWorkType",
            table: "VendorProfiles",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SubWorkType",
            table: "VendorCertificates",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "SubWorkType", table: "VendorProfiles");
        migrationBuilder.DropColumn(name: "SubWorkType", table: "VendorCertificates");
    }
}
