
using Microsoft.AspNetCore.Identity;

namespace VendorPortal.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var roles =
            services.GetRequiredService<RoleManager<IdentityRole>>();

        string[] requiredRoles = { "Admin", "Vendor" };

        foreach (var roleName in requiredRoles)
        {
            if (await roles.RoleExistsAsync(roleName))
                continue;

            var result = await roles.CreateAsync(
                new IdentityRole(roleName));

            if (!result.Succeeded)
            {
                var errors = string.Join(
                    "; ",
                    result.Errors.Select(e => e.Description));

                throw new InvalidOperationException(
                    $"Failed to create role '{roleName}': {errors}");
            }
        }
    }
}
