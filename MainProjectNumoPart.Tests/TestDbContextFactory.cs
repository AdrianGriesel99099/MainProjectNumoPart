using MainProjectNumoPart.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MainProjectNumoPart.Tests
{
    public static class TestDbContextFactory
    {
        // Real SQLite (not EF's InMemory provider) so unique-index and constraint
        // behavior in tests matches what production SQLite actually enforces.
        public static AppDbContext CreateInMemory()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;

            var context = new AppDbContext(options);
            context.Database.EnsureCreated();
            return context;
        }
    }
}
