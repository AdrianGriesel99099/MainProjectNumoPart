using System;
using System.Linq;
using System.Threading.Tasks;
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

            if (!await roleManager.RoleExistsAsync("Admin"))
            {
                await roleManager.CreateAsync(new IdentityRole("Admin"));
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

            await userManager.AddToRoleAsync(admin, "Admin");
        }
    }
}
