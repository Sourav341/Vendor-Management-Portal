using Microsoft.AspNetCore.Identity;
using VendorPortal.Models;

namespace VendorPortal.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration config)
    {
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var r in new[] { "Admin", "Vendor" })
            if (!await roles.RoleExistsAsync(r)) await roles.CreateAsync(new IdentityRole(r));

        var email = config["SeedAdmin:Email"];
        var password = config["SeedAdmin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return;

        if (await users.FindByEmailAsync(email) is null)
        {
            var admin = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
            var res = await users.CreateAsync(admin, password);
            if (res.Succeeded) await users.AddToRoleAsync(admin, "Admin");
        }
    }
}
