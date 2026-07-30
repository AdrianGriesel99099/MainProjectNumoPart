using System;
using System.Linq;
using System.Threading.Tasks;
using MainProjectNumoPart.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MainProjectNumoPart.Services
{
    public static class AdminSeeder
    {
        public static async Task SeedInitialAdminAsync(IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            // Role creation MUST stay above the "already bootstrapped" early-return below. This
            // is the entire upgrade path for an existing deployment: production already has
            // users, so a seeder that returned first would never create Viewer or Staff, and
            // they'd be unassignable forever. Pinned by AdminSeederTests.
            foreach (var role in Roles.All)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                }
            }

            if (userManager.Users.Any())
            {
                return; // Already bootstrapped — never re-seed.
            }

            var email = config["InitialAdmin:Email"];
            var password = config["InitialAdmin:Password"];

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                throw new InvalidOperationException(
                    "No user accounts exist and InitialAdmin:Email/InitialAdmin:Password are not configured. " +
                    "Set them in appsettings.Development.json locally, or as environment configuration in production.");
            }

            var admin = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            var result = await userManager.CreateAsync(admin, password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    "Failed to seed initial admin: " + string.Join("; ", result.Errors.Select(e => e.Description)));
            }

            await userManager.AddToRoleAsync(admin, Roles.Admin);
        }
    }
}
