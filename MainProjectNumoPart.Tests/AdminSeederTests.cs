using System;
using System.Threading.Tasks;
using MainProjectNumoPart.Authorization;
using MainProjectNumoPart.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class AdminSeederTests
    {
        private static ServiceProvider BuildServices(string? adminEmail, string? adminPassword)
            => TestIdentityFactory.Build(adminEmail, adminPassword);

        [Fact]
        public async Task SeedsAdminWhenNoUsersExist()
        {
            var services = BuildServices("admin@workshop.local", "Str0ng!Passw0rd");

            await AdminSeeder.SeedInitialAdminAsync(services);

            using var scope = services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var admin = await userManager.FindByEmailAsync("admin@workshop.local");

            Assert.NotNull(admin);
            Assert.True(await userManager.IsInRoleAsync(admin!, Roles.Admin));
        }

        [Fact]
        public async Task SeedsAllThreeRoles()
        {
            var services = BuildServices("admin@workshop.local", "Str0ng!Passw0rd");

            await AdminSeeder.SeedInitialAdminAsync(services);

            using var scope = services.CreateScope();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

            foreach (var role in Roles.All)
            {
                Assert.True(await roleManager.RoleExistsAsync(role), $"Role '{role}' was not created.");
            }
        }

        // The production upgrade path, and the reason role creation sits ABOVE the
        // "already bootstrapped" early-return in AdminSeeder. Production already has users, so a
        // seeder that returned before creating roles would leave Viewer and Staff non-existent
        // and therefore permanently unassignable. If someone reorders that method, this fails.
        [Fact]
        public async Task SeedsMissingRolesWhenUsersAlreadyExist()
        {
            var services = BuildServices("admin@workshop.local", "Str0ng!Passw0rd");

            using (var setup = services.CreateScope())
            {
                var roleManager = setup.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                var userManager = setup.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

                // Recreate the pre-upgrade shape: a user exists, and only the Admin role exists.
                await roleManager.CreateAsync(new IdentityRole(Roles.Admin));
                var existing = new IdentityUser { UserName = "existing@workshop.local", Email = "existing@workshop.local" };
                await userManager.CreateAsync(existing, "Str0ng!Passw0rd");
            }

            await AdminSeeder.SeedInitialAdminAsync(services);

            using var scope = services.CreateScope();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

            Assert.True(await roles.RoleExistsAsync(Roles.Viewer));
            Assert.True(await roles.RoleExistsAsync(Roles.Staff));
            Assert.Single(users.Users); // the seeder still must not create a second admin
        }

        [Fact]
        public async Task DoesNotReseedWhenUsersAlreadyExist()
        {
            var services = BuildServices("admin@workshop.local", "Str0ng!Passw0rd");
            await AdminSeeder.SeedInitialAdminAsync(services);

            // Second call must not throw or duplicate — it should see an existing user and no-op.
            await AdminSeeder.SeedInitialAdminAsync(services);

            using var scope = services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            Assert.Single(userManager.Users);
        }

        [Fact]
        public async Task ThrowsWhenNoUsersAndNoConfig()
        {
            var services = BuildServices(null, null);

            await Assert.ThrowsAsync<InvalidOperationException>(() => AdminSeeder.SeedInitialAdminAsync(services));
        }
    }
}
