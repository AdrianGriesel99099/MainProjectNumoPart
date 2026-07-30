using System.Collections.Generic;
using MainProjectNumoPart.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MainProjectNumoPart.Tests
{
    // Builds a real UserManager/RoleManager over an in-memory SQLite database. Real Identity
    // rather than mocks, because what these tests need to verify is exactly the behaviour mocks
    // would paper over: that AddToRoleAsync is additive, that security stamps actually change,
    // and that role queries see what was written.
    //
    // The SqliteConnection is deliberately kept open by the caller's ServiceProvider — an
    // in-memory SQLite database is destroyed the moment its last connection closes.
    public static class TestIdentityFactory
    {
        public static ServiceProvider Build(string? adminEmail = null, string? adminPassword = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();

            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));
            services.AddLogging();
            services.AddIdentity<IdentityUser, IdentityRole>()
                .AddEntityFrameworkStores<AppDbContext>()
                .AddDefaultTokenProviders();

            var configData = new Dictionary<string, string?>();
            if (adminEmail is not null) configData["InitialAdmin:Email"] = adminEmail;
            if (adminPassword is not null) configData["InitialAdmin:Password"] = adminPassword;
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(configData).Build());

            var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();

            return provider;
        }
    }
}
