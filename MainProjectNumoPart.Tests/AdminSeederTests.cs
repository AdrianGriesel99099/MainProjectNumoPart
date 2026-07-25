using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class AdminSeederTests
    {
        private static ServiceProvider BuildServices(string? adminEmail, string? adminPassword)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();

            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));
            services.AddLogging();
            services.AddIdentity<IdentityUser, IdentityRole>()
                .AddEntityFrameworkStores<AppDbContext>()
                .AddDefaultTokenProviders();

            var configData = new System.Collections.Generic.Dictionary<string, string?>();
            if (adminEmail is not null) configData["InitialAdmin:Email"] = adminEmail;
            if (adminPassword is not null) configData["InitialAdmin:Password"] = adminPassword;
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(configData).Build());

            var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();

            return provider;
        }

        [Fact]
        public async Task SeedsAdminWhenNoUsersExist()
        {
            var services = BuildServices("admin@workshop.local", "Str0ng!Passw0rd");

            await AdminSeeder.SeedInitialAdminAsync(services);

            using var scope = services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var admin = await userManager.FindByEmailAsync("admin@workshop.local");

            Assert.NotNull(admin);
            Assert.True(await userManager.IsInRoleAsync(admin!, "Admin"));
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
